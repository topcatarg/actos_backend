using Dapper;
using Npgsql;

namespace actos_backend.Datos;

/// <summary>Persona física del maestro, con su domicilio y estado civil vigentes.</summary>
public class PersonaMaestro
{
    public int PfId { get; init; }
    public string NombreCompleto { get; init; } = "";
    public string? Dni { get; init; }
    public string? CuitCuil { get; init; }
    public DateTime? FechaNacimiento { get; init; }
    public string? Nacionalidad { get; init; }
    public string? Padre { get; init; }
    public string? Madre { get; init; }
    public string? Genero { get; init; }
    public string? Direccion { get; init; }
    public string? Ciudad { get; init; }
    public string? Provincia { get; init; }
    public string? EstadoCivil { get; init; }
    public string? ConyugeNombre { get; init; }
}

/// <summary>Posible coincidencia por nombre. Nunca se usa para completar datos sin confirmación.</summary>
public class CandidatoPersona
{
    public int PfId { get; init; }
    public string NombreCompleto { get; init; } = "";
    public string? Dni { get; init; }
    /// <summary>Cuántas palabras del nombre buscado coinciden.</summary>
    public long Coincidencias { get; init; }
}

public interface IPersonasRepositorio
{
    Task<PersonaMaestro?> BuscarPorDniAsync(string dni, CancellationToken ct = default);
    Task<IReadOnlyList<CandidatoPersona>> BuscarPorNombreAsync(string nombre, CancellationToken ct = default);
}

public class PersonasRepositorio(NpgsqlDataSource db) : IPersonasRepositorio
{
    public async Task<PersonaMaestro?> BuscarPorDniAsync(string dni, CancellationToken ct = default)
    {
        const string sql = """
            SELECT pf.pf_id, pf.nombre_completo, pf.dni, pf.cuit_cuil, pf.fecha_nacimiento,
                   pf.nacionalidad, pf.padre, pf.madre, pf.genero,
                   dom.direccion, dom.ciudad, dom.provincia,
                   ec.estado_civil, ec.conyuge_nombre
            FROM pf_maestro pf
            LEFT JOIN LATERAL (SELECT * FROM pf_domicilios d WHERE d.pf_id = pf.pf_id
                               ORDER BY d.desde_fecha DESC LIMIT 1) dom ON true
            LEFT JOIN LATERAL (SELECT * FROM pf_estado_civil e WHERE e.pf_id = pf.pf_id
                               ORDER BY e.desde_fecha DESC LIMIT 1) ec ON true
            WHERE regexp_replace(COALESCE(pf.dni, ''), '[^0-9]', '', 'g') = @Dni
            LIMIT 1
            """;

        var soloDigitos = new string(dni.Where(char.IsDigit).ToArray());
        if (soloDigitos.Length == 0) return null;

        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<PersonaMaestro>(
            new CommandDefinition(sql, new { Dni = soloDigitos }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<CandidatoPersona>> BuscarPorNombreAsync(string nombre, CancellationToken ct = default)
    {
        // Cuenta cuántas palabras del nombre aparecen (sin acentos ni mayúsculas).
        // Con dos o más palabras exige al menos dos coincidencias, para no traer a todos los "Pérez".
        const string sql = """
            SELECT pf.pf_id, pf.nombre_completo, pf.dni, s.coincidencias
            FROM pf_maestro pf
            CROSS JOIN LATERAL (
                SELECT count(*) AS coincidencias
                FROM unnest(@Palabras) AS p(palabra)
                WHERE unaccent(lower(pf.nombre_completo)) LIKE '%' || unaccent(lower(p.palabra)) || '%'
            ) s
            WHERE s.coincidencias >= LEAST(2, cardinality(@Palabras))
            ORDER BY s.coincidencias DESC, pf.nombre_completo
            LIMIT 5
            """;

        // Palabras de menos de 3 letras ("de", "la") no discriminan.
        var palabras = nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                             .Where(p => p.Length >= 3)
                             .ToArray();
        if (palabras.Length == 0) return [];

        await using var conn = await db.OpenConnectionAsync(ct);
        var filas = await conn.QueryAsync<CandidatoPersona>(
            new CommandDefinition(sql, new { Palabras = palabras }, cancellationToken: ct));
        return filas.AsList();
    }
}
