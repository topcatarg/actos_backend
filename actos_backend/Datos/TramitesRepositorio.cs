using System.Text.Json;
using actos_backend.Poderes;
using Dapper;
using Npgsql;

namespace actos_backend.Datos;

public class TramiteGuardado
{
    public int Id { get; init; }
    public string CodigoTramite { get; init; } = "";
    public string TipoTramite { get; init; } = "";
    public string Estado { get; init; } = "";
    public PoderExtraido Poder { get; init; } = new();
    public List<Pendiente> Siguiente { get; init; } = [];
    public int Version { get; init; }
}

public record MensajeChat(string Rol, string Texto, DateTime CreatedAt);

/// <summary>Otro proceso modificó el trámite entre que se leyó y se guardó.</summary>
public class TramiteModificadoException(string codigo)
    : Exception($"El trámite {codigo} fue modificado por otro pedido. Volvé a intentar.");

public interface ITramitesRepositorio
{
    Task<TramiteGuardado> CrearAsync(string tipo, string estado, PoderExtraido poder, List<Pendiente> siguiente,
        string mensajeUsuario, string mensajeAsistente, CancellationToken ct = default);
    Task<TramiteGuardado?> ObtenerAsync(string codigo, CancellationToken ct = default);
    Task<int> ActualizarAsync(TramiteGuardado anterior, string estado, PoderExtraido poder, List<Pendiente> siguiente,
        string mensajeUsuario, string mensajeAsistente, CancellationToken ct = default);
    Task<IReadOnlyList<MensajeChat>> MensajesAsync(int tramiteId, CancellationToken ct = default);
}

public class TramitesRepositorio(NpgsqlDataSource db) : ITramitesRepositorio
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<TramiteGuardado> CrearAsync(string tipo, string estado, PoderExtraido poder, List<Pendiente> siguiente,
        string mensajeUsuario, string mensajeAsistente, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO chat_tramites (codigo_tramite, tipo_tramite, estado, datos, siguiente)
            VALUES (@Codigo, @Tipo, @Estado, @Datos::jsonb, @Siguiente::jsonb)
            RETURNING id
            """;

        var codigo = NuevoCodigo(tipo);
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var id = await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new
        {
            Codigo = codigo, Tipo = tipo, Estado = estado,
            Datos = JsonSerializer.Serialize(poder, Json),
            Siguiente = JsonSerializer.Serialize(siguiente, Json)
        }, tx, cancellationToken: ct));

        await AgregarMensajesAsync(conn, tx, id, mensajeUsuario, mensajeAsistente, ct);
        await tx.CommitAsync(ct);

        return new TramiteGuardado
        {
            Id = id, CodigoTramite = codigo, TipoTramite = tipo, Estado = estado,
            Poder = poder, Siguiente = siguiente, Version = 1
        };
    }

    public async Task<TramiteGuardado?> ObtenerAsync(string codigo, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, codigo_tramite, tipo_tramite, estado, datos::text AS datos, siguiente::text AS siguiente, version
            FROM chat_tramites
            WHERE codigo_tramite = @Codigo
            """;

        await using var conn = await db.OpenConnectionAsync(ct);
        var fila = await conn.QuerySingleOrDefaultAsync<FilaTramite>(new CommandDefinition(sql, new { Codigo = codigo }, cancellationToken: ct));
        if (fila is null) return null;

        return new TramiteGuardado
        {
            Id = fila.Id, CodigoTramite = fila.CodigoTramite, TipoTramite = fila.TipoTramite, Estado = fila.Estado,
            Poder = JsonSerializer.Deserialize<PoderExtraido>(fila.Datos, Json) ?? new(),
            Siguiente = JsonSerializer.Deserialize<List<Pendiente>>(fila.Siguiente, Json) ?? [],
            Version = fila.Version
        };
    }

    public async Task<int> ActualizarAsync(TramiteGuardado anterior, string estado, PoderExtraido poder, List<Pendiente> siguiente,
        string mensajeUsuario, string mensajeAsistente, CancellationToken ct = default)
    {
        // Solo actualiza si nadie lo cambió desde que se leyó (misma versión).
        const string sql = """
            UPDATE chat_tramites
            SET estado = @Estado, datos = @Datos::jsonb, siguiente = @Siguiente::jsonb,
                version = version + 1, updated_at = NOW()
            WHERE id = @Id AND version = @Version
            """;

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var filas = await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            anterior.Id, anterior.Version, Estado = estado,
            Datos = JsonSerializer.Serialize(poder, Json),
            Siguiente = JsonSerializer.Serialize(siguiente, Json)
        }, tx, cancellationToken: ct));

        if (filas == 0)
            throw new TramiteModificadoException(anterior.CodigoTramite);

        await AgregarMensajesAsync(conn, tx, anterior.Id, mensajeUsuario, mensajeAsistente, ct);
        await tx.CommitAsync(ct);
        return anterior.Version + 1;
    }

    public async Task<IReadOnlyList<MensajeChat>> MensajesAsync(int tramiteId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT rol, texto, created_at AS createdat
            FROM chat_mensajes
            WHERE tramite_id = @TramiteId
            ORDER BY id
            """;

        await using var conn = await db.OpenConnectionAsync(ct);
        var filas = await conn.QueryAsync<MensajeChat>(new CommandDefinition(sql, new { TramiteId = tramiteId }, cancellationToken: ct));
        return filas.AsList();
    }

    private static async Task AgregarMensajesAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int tramiteId,
        string usuario, string asistente, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO chat_mensajes (tramite_id, rol, texto)
            VALUES (@TramiteId, 'usuario', @Usuario), (@TramiteId, 'asistente', @Asistente)
            """;
        await conn.ExecuteAsync(new CommandDefinition(sql, new { TramiteId = tramiteId, Usuario = usuario, Asistente = asistente }, tx, cancellationToken: ct));
    }

    /// <summary>Ej. PODER-20261112-4F3A. Legible para el personal y único con muy alta probabilidad (UNIQUE en la tabla igual).</summary>
    private static string NuevoCodigo(string tipo) =>
        $"{tipo.ToUpperInvariant()}-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(0x10000):X4}";

    private class FilaTramite
    {
        public int Id { get; init; }
        public string CodigoTramite { get; init; } = "";
        public string TipoTramite { get; init; } = "";
        public string Estado { get; init; } = "";
        public string Datos { get; init; } = "";
        public string Siguiente { get; init; } = "";
        public int Version { get; init; }
    }
}
