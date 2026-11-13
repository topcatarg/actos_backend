using Dapper;
using Npgsql;

namespace actos_backend.Datos;

/// <summary>
/// Qué escribir de una persona. Lo decide GuardadoPersonas; el repositorio solo lo ejecuta.
/// </summary>
/// <param name="PfId">Null = persona nueva.</param>
/// <param name="Maestro">Columna de pf_maestro → valor ya normalizado.</param>
/// <param name="Domicilio">Domicilio vigente nuevo, o null si no cambia.</param>
/// <param name="EstadoCivil">Estado civil vigente nuevo, o null si no cambia.</param>
public record PlanPersona(int? PfId, IReadOnlyDictionary<string, string> Maestro, string? Domicilio, string? EstadoCivil)
{
    public bool Vacio => Maestro.Count == 0 && Domicilio is null && EstadoCivil is null;
}

/// <summary>Cómo está hoy la persona en el registro (con el domicilio y el estado civil vigentes).</summary>
public class PersonaRegistrada
{
    public int PfId { get; init; }
    public string? NombreCompleto { get; init; }
    public string? Nombres { get; init; }
    public string? Apellido { get; init; }
    public string? Dni { get; init; }
    public string? CuitCuil { get; init; }
    public string? FechaNacimiento { get; init; }   // AAAA-MM-DD
    public string? Nacionalidad { get; init; }
    public string? Padre { get; init; }
    public string? Madre { get; init; }
    public string? Genero { get; init; }
    public string? Domicilio { get; init; }          // dirección, ciudad, provincia
    public string? EstadoCivil { get; init; }

    public string? Valor(string columna) => columna switch
    {
        "nombre_completo" => NombreCompleto, "nombres" => Nombres, "apellido" => Apellido, "dni" => Dni,
        "cuit_cuil" => CuitCuil, "fecha_nacimiento" => FechaNacimiento, "nacionalidad" => Nacionalidad,
        "padre" => Padre, "madre" => Madre, "genero" => Genero,
        _ => throw new ArgumentException($"Columna desconocida: {columna}")
    };
}

public interface IRegistroPersonasRepositorio
{
    Task<int?> PfIdPorDniAsync(string dni, CancellationToken ct = default);
    Task<PersonaRegistrada?> ObtenerAsync(int pfId, CancellationToken ct = default);

    /// <summary>Ejecuta el plan en una transacción. Devuelve el pf_id (el nuevo si se creó).</summary>
    Task<int> AplicarAsync(PlanPersona plan, CancellationToken ct = default);
}

public class RegistroPersonasRepositorio(NpgsqlDataSource db) : IRegistroPersonasRepositorio
{
    /// <summary>Lo mismo que marcaba n8n para las personas cargadas desde el chat.</summary>
    private const string EstadoValidacionChat = "CARGA_MANUAL_CHAT";

    /// <summary>Columnas que se pueden escribir (los nombres van al SQL: nada fuera de esta lista).</summary>
    private static readonly HashSet<string> ColumnasMaestro =
    [
        "nombre_completo", "nombres", "apellido", "dni", "cuit_cuil", "fecha_nacimiento",
        "nacionalidad", "padre", "madre", "genero"
    ];

    public async Task<int?> PfIdPorDniAsync(string dni, CancellationToken ct = default)
    {
        const string sql = """
            SELECT pf_id FROM pf_maestro
            WHERE regexp_replace(COALESCE(dni, ''), '[^0-9]', '', 'g') = @Dni
            ORDER BY pf_id
            LIMIT 1
            """;
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(sql, new { Dni = dni }, cancellationToken: ct));
    }

    public async Task<PersonaRegistrada?> ObtenerAsync(int pfId, CancellationToken ct = default)
    {
        // Mismo criterio de "vigente" que PersonasRepositorio: la fila de desde_fecha más reciente.
        const string sql = """
            SELECT pf.pf_id, pf.nombre_completo, pf.nombres, pf.apellido, pf.dni, pf.cuit_cuil,
                   to_char(pf.fecha_nacimiento, 'YYYY-MM-DD') AS fecha_nacimiento,
                   pf.nacionalidad, pf.padre, pf.madre, pf.genero,
                   concat_ws(', ', NULLIF(dom.direccion, ''), NULLIF(dom.ciudad, ''), NULLIF(dom.provincia, '')) AS domicilio,
                   ec.estado_civil
            FROM pf_maestro pf
            LEFT JOIN LATERAL (SELECT * FROM pf_domicilios d WHERE d.pf_id = pf.pf_id
                               ORDER BY d.desde_fecha DESC, d.id DESC LIMIT 1) dom ON true
            LEFT JOIN LATERAL (SELECT * FROM pf_estado_civil e WHERE e.pf_id = pf.pf_id
                               ORDER BY e.desde_fecha DESC, e.id DESC LIMIT 1) ec ON true
            WHERE pf.pf_id = @PfId
            """;
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<PersonaRegistrada>(
            new CommandDefinition(sql, new { PfId = pfId }, cancellationToken: ct));
    }

    public async Task<int> AplicarAsync(PlanPersona plan, CancellationToken ct = default)
    {
        foreach (var c in plan.Maestro.Keys)
            if (!ColumnasMaestro.Contains(c)) throw new ArgumentException($"Columna no permitida: {c}");

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var pfId = plan.PfId is null
            ? await CrearAsync(conn, tx, plan.Maestro, ct)
            : await ActualizarAsync(conn, tx, plan.PfId.Value, plan.Maestro, ct);

        if (plan.Domicilio is not null)
            await conn.ExecuteAsync(new CommandDefinition(SqlHistorico("pf_domicilios", "direccion", "ciudad = NULL, provincia = NULL"),
                new { PfId = pfId, Valor = plan.Domicilio }, tx, cancellationToken: ct));

        if (plan.EstadoCivil is not null)
            await conn.ExecuteAsync(new CommandDefinition(SqlHistorico("pf_estado_civil", "estado_civil", null),
                new { PfId = pfId, Valor = plan.EstadoCivil }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return pfId;
    }

    private static async Task<int> CrearAsync(NpgsqlConnection conn, NpgsqlTransaction tx,
        IReadOnlyDictionary<string, string> maestro, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var columnas = new List<string>();
        var valores = new List<string>();
        foreach (var (columna, valor) in maestro)
        {
            columnas.Add(columna);
            valores.Add(Parametro(p, columna, valor));
        }
        columnas.Add("estado_validacion");
        valores.Add("@EstadoValidacion");
        p.Add("EstadoValidacion", EstadoValidacionChat);

        var sql = $"INSERT INTO pf_maestro ({string.Join(", ", columnas)}) VALUES ({string.Join(", ", valores)}) RETURNING pf_id";
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, p, tx, cancellationToken: ct));
    }

    private static async Task<int> ActualizarAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int pfId,
        IReadOnlyDictionary<string, string> maestro, CancellationToken ct)
    {
        if (maestro.Count == 0) return pfId;

        var p = new DynamicParameters(new { PfId = pfId });
        var sets = maestro.Select(kv => $"{kv.Key} = {Parametro(p, kv.Key, kv.Value)}");
        var sql = $"UPDATE pf_maestro SET {string.Join(", ", sets)}, updated_at = NOW() WHERE pf_id = @PfId";
        await conn.ExecuteAsync(new CommandDefinition(sql, p, tx, cancellationToken: ct));
        return pfId;
    }

    /// <summary>Agrega el parámetro y devuelve cómo referenciarlo en el SQL (con cast para las fechas).</summary>
    private static string Parametro(DynamicParameters p, string columna, string valor)
    {
        var nombre = "p_" + columna;
        p.Add(nombre, valor);
        return columna == "fecha_nacimiento" ? $"@{nombre}::date" : $"@{nombre}";
    }

    /// <summary>
    /// Histórico con fecha: si la última fila ya es de hoy se corrige esa; si no, se agrega una nueva desde hoy.
    /// </summary>
    private static string SqlHistorico(string tabla, string columna, string? limpiarAlCorregir) => $"""
        WITH ultima AS (
            SELECT id, desde_fecha FROM {tabla}
            WHERE pf_id = @PfId
            ORDER BY desde_fecha DESC, id DESC
            LIMIT 1
        ),
        corregida AS (
            UPDATE {tabla} t SET {columna} = @Valor{(limpiarAlCorregir is null ? "" : ", " + limpiarAlCorregir)}
            FROM ultima
            WHERE t.id = ultima.id AND ultima.desde_fecha = CURRENT_DATE
            RETURNING t.id
        )
        INSERT INTO {tabla} (pf_id, {columna}, desde_fecha)
        SELECT @PfId, @Valor, CURRENT_DATE
        WHERE NOT EXISTS (SELECT 1 FROM corregida)
        """;
}
