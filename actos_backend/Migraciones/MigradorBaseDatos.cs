using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Npgsql;

namespace actos_backend.Migraciones;

/// <summary>Un script de migración embebido en el ensamblado (Migraciones/Scripts/V0001__nombre.sql).</summary>
public sealed record ScriptMigracion(int Version, string Nombre, string Sql, string Checksum);

public sealed record MigracionInfo(int Version, string Nombre);

public sealed record EstadoBaseDatos(
    bool Versionada,
    int VersionActual,
    int UltimaVersion,
    IReadOnlyList<MigracionInfo> Pendientes,
    IReadOnlyList<MigracionInfo> Modificadas);

public sealed record ResultadoMigracion(int VersionAnterior, int VersionActual, IReadOnlyList<MigracionInfo> Aplicadas);

/// <summary>La base no se puede migrar tal como está (se explica en el mensaje).</summary>
public class MigracionException(string mensaje, Exception? inner = null) : Exception(mensaje, inner);

public class VersionAplicada
{
    public int Version { get; init; }
    public string Nombre { get; init; } = "";
    public string Checksum { get; init; } = "";
    public DateTime AplicadaEn { get; init; }
}

/// <summary>
/// Lleva la base a la última versión aplicando, en orden, los scripts que falten.
/// Cada script corre en su propia transacción junto con el registro en version_esquema:
/// si un script falla, la base queda en la versión anterior y se informa cuál falló.
/// </summary>
public class MigradorBaseDatos(NpgsqlDataSource db, ILogger<MigradorBaseDatos> logger)
{
    /// <summary>Clave del advisory lock: evita que dos pedidos migren a la vez.</summary>
    private const long ClaveLock = 7_220_001;

    private static readonly Regex NombreScript = new(@"\.V(?<version>\d+)__(?<nombre>[A-Za-z0-9_]+)\.sql$", RegexOptions.Compiled);

    private static readonly Lazy<IReadOnlyList<ScriptMigracion>> ScriptsEmbebidos = new(CargarScripts);

    public IReadOnlyList<ScriptMigracion> Scripts => ScriptsEmbebidos.Value;

    public async Task<EstadoBaseDatos> ObtenerEstadoAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await CalcularEstadoAsync(conn, ct);
    }

    public async Task<ResultadoMigracion> MigrarAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync("SELECT pg_advisory_lock(@ClaveLock)", new { ClaveLock });
        try
        {
            var estado = await CalcularEstadoAsync(conn, ct);

            if (!estado.Versionada && await HayTablasAsync(conn, ct))
                throw new MigracionException(
                    "La base tiene tablas pero no tiene tabla de versión (se creó con los scripts viejos de db/init). " +
                    "Regenerala con POST /api/db/regenerar?confirmar=true.");

            if (estado.Modificadas.Count > 0)
                throw new MigracionException(
                    "Hay scripts ya aplicados que cambiaron desde que se aplicaron: " +
                    string.Join(", ", estado.Modificadas.Select(m => $"V{m.Version:D4} {m.Nombre}")) +
                    ". No se puede migrar encima; regenerá la base o creá un script nuevo con el cambio.");

            await AsegurarTablaVersionAsync(conn, ct);

            var aplicadas = new List<MigracionInfo>();
            foreach (var script in Scripts.Where(s => s.Version > estado.VersionActual))
            {
                await AplicarAsync(conn, script, ct);
                aplicadas.Add(new MigracionInfo(script.Version, script.Nombre));
            }

            var versionFinal = aplicadas.Count > 0 ? aplicadas[^1].Version : estado.VersionActual;
            return new ResultadoMigracion(estado.VersionActual, versionFinal, aplicadas);
        }
        finally
        {
            await conn.ExecuteAsync("SELECT pg_advisory_unlock(@ClaveLock)", new { ClaveLock });
        }
    }

    /// <summary>
    /// Borra TODO el esquema public (tablas, datos y extensiones) y migra desde cero.
    /// Solo para desarrollo: el controller no lo expone fuera de Development.
    /// </summary>
    public async Task<ResultadoMigracion> RegenerarAsync(CancellationToken ct = default)
    {
        await using (var conn = await db.OpenConnectionAsync(ct))
        {
            await conn.ExecuteAsync("SELECT pg_advisory_lock(@ClaveLock)", new { ClaveLock });
            try
            {
                logger.LogWarning("Regenerando la base: se borra el esquema public completo");
                await conn.ExecuteAsync(new CommandDefinition(
                    "DROP SCHEMA public CASCADE; CREATE SCHEMA public;", cancellationToken: ct));
            }
            finally
            {
                await conn.ExecuteAsync("SELECT pg_advisory_unlock(@ClaveLock)", new { ClaveLock });
            }
        }

        // Las conexiones del pool pueden tener cacheados tipos/OIDs del esquema borrado.
        db.Clear();
        return await MigrarAsync(ct);
    }

    private async Task AplicarAsync(NpgsqlConnection conn, ScriptMigracion script, CancellationToken ct)
    {
        logger.LogInformation("Aplicando migración V{Version:D4} {Nombre}", script.Version, script.Nombre);

        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(script.Sql, transaction: tx,
                commandTimeout: 300, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO version_esquema (version, nombre, checksum)
                VALUES (@Version, @Nombre, @Checksum)
                """, new { script.Version, script.Nombre, script.Checksum }, tx, cancellationToken: ct));

            await tx.CommitAsync(ct);
        }
        catch (PostgresException ex)
        {
            // El dispose de la transacción hace rollback: la base queda en la versión anterior.
            throw new MigracionException(
                $"Falló la migración V{script.Version:D4} {script.Nombre}: {ex.MessageText}" +
                (ex.Position > 0 ? $" (posición {ex.Position} del script)" : ""), ex);
        }
    }

    private async Task<EstadoBaseDatos> CalcularEstadoAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var ultima = Scripts.Count > 0 ? Scripts[^1].Version : 0;

        var versionada = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT to_regclass('public.version_esquema') IS NOT NULL", cancellationToken: ct));

        if (!versionada)
            return new EstadoBaseDatos(false, 0, ultima, Scripts.Select(Info).ToList(), []);

        var aplicadas = (await conn.QueryAsync<VersionAplicada>(new CommandDefinition(
            "SELECT version, nombre, checksum, aplicada_en FROM version_esquema ORDER BY version",
            cancellationToken: ct))).ToDictionary(a => a.Version);

        var actual = aplicadas.Count > 0 ? aplicadas.Keys.Max() : 0;

        // Un script con versión menor a la actual que nunca se aplicó rompe el orden: mejor frenar.
        var salteados = Scripts.Where(s => s.Version < actual && !aplicadas.ContainsKey(s.Version)).ToList();
        if (salteados.Count > 0)
            throw new MigracionException(
                "Hay scripts con versión menor a la actual que nunca se aplicaron: " +
                string.Join(", ", salteados.Select(s => $"V{s.Version:D4} {s.Nombre}")) +
                ". Renumeralos por encima de la versión actual o regenerá la base.");

        var modificadas = Scripts
            .Where(s => aplicadas.TryGetValue(s.Version, out var a) && a.Checksum != s.Checksum)
            .Select(Info).ToList();

        var pendientes = Scripts.Where(s => s.Version > actual).Select(Info).ToList();

        return new EstadoBaseDatos(true, actual, ultima, pendientes, modificadas);
    }

    private static Task AsegurarTablaVersionAsync(NpgsqlConnection conn, CancellationToken ct) =>
        conn.ExecuteAsync(new CommandDefinition("""
            CREATE TABLE IF NOT EXISTS version_esquema (
                version      INTEGER PRIMARY KEY,
                nombre       TEXT NOT NULL,
                checksum     TEXT NOT NULL,
                aplicada_en  TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """, cancellationToken: ct));

    private static Task<bool> HayTablasAsync(NpgsqlConnection conn, CancellationToken ct) =>
        conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public')",
            cancellationToken: ct));

    private static MigracionInfo Info(ScriptMigracion s) => new(s.Version, s.Nombre);

    private static IReadOnlyList<ScriptMigracion> CargarScripts()
    {
        var asm = Assembly.GetExecutingAssembly();
        var scripts = new List<ScriptMigracion>();

        foreach (var recurso in asm.GetManifestResourceNames().Where(n => n.Contains(".Migraciones.Scripts.")))
        {
            var m = NombreScript.Match(recurso);
            if (!m.Success)
                throw new InvalidOperationException(
                    $"Script de migración con nombre inválido: {recurso}. Formato esperado: V0001__descripcion.sql");

            using var stream = asm.GetManifestResourceStream(recurso)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            // Normalizar fin de línea: con autocrlf de git en Windows el checksum no debe cambiar.
            var sql = reader.ReadToEnd().Replace("\r\n", "\n").TrimStart('\uFEFF');
            var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));

            scripts.Add(new ScriptMigracion(int.Parse(m.Groups["version"].Value), m.Groups["nombre"].Value, sql, checksum));
        }

        var duplicadas = scripts.GroupBy(s => s.Version).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicadas.Count > 0)
            throw new InvalidOperationException($"Versiones de migración duplicadas: {string.Join(", ", duplicadas)}");

        return scripts.OrderBy(s => s.Version).ToList();
    }
}
