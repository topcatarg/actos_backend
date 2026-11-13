using Dapper;
using Npgsql;

namespace actos_backend.Datos;

/// <summary>Persona física del maestro, con su domicilio y estado civil vigentes.</summary>
public class PersonaMaestro
{
    public int PfId { get; init; }
    public string NombreCompleto { get; init; } = "";
    public string? Nombres { get; init; }
    public string? Apellido { get; init; }
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
    /// <summary>Personas que tienen TODAS las palabras del nombre (palabras enteras) y ese apellido.</summary>
    Task<IReadOnlyList<CandidatoPersona>> BuscarPorNombreAsync(string nombreCompleto, string apellido, CancellationToken ct = default);
}

public class PersonasRepositorio(NpgsqlDataSource db) : IPersonasRepositorio
{
    public async Task<PersonaMaestro?> BuscarPorDniAsync(string dni, CancellationToken ct = default)
    {
        const string sql = """
            SELECT pf.pf_id, pf.nombre_completo, pf.nombres, pf.apellido, pf.dni, pf.cuit_cuil, pf.fecha_nacimiento,
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

    public async Task<IReadOnlyList<CandidatoPersona>> BuscarPorNombreAsync(string nombreCompleto, string apellido,
        CancellationToken ct = default)
    {
        // Todas las palabras del nombre tienen que estar, como palabras enteras (\m...\M), en el nombre
        // completo del registro; y el apellido tiene que coincidir con la columna apellido si está cargada.
        // Así "Juan Pérez" no trae a "Juana Pérez" ni a "Juan Carlos Gómez".
        const string sql = """
            SELECT pf.pf_id, pf.nombre_completo, pf.dni, cardinality(@Palabras)::bigint AS coincidencias
            FROM pf_maestro pf
            WHERE (SELECT bool_and(unaccent(lower(pf.nombre_completo)) ~ ('\m' || p.palabra || '\M'))
                   FROM unnest(@Palabras) AS p(palabra))
              AND (pf.apellido IS NULL OR unaccent(lower(pf.apellido)) = unaccent(lower(@Apellido)))
            ORDER BY pf.nombre_completo
            LIMIT 5
            """;

        // Solo letras: las palabras van dentro de una expresión regular.
        var palabras = Poderes.NombresPersona.Palabras($"{nombreCompleto} {apellido}")
            .Select(p => new string(p.Where(char.IsLetter).ToArray()))
            .Where(p => p.Length > 0)
            .Distinct()
            .ToArray();
        if (palabras.Length < 2) return [];

        await using var conn = await db.OpenConnectionAsync(ct);
        var filas = await conn.QueryAsync<CandidatoPersona>(new CommandDefinition(sql,
            new { Palabras = palabras, Apellido = apellido.Trim() }, cancellationToken: ct));
        return filas.AsList();
    }
}
