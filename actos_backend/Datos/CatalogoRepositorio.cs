using Dapper;
using Npgsql;

namespace actos_backend.Datos;

public class Clausula
{
    public string Codigo { get; init; } = "";
    public string Nombre { get; init; } = "";
    public string Tipo { get; init; } = "";        // facultad | condicion
    public string Categoria { get; init; } = "";   // para condiciones: sustitucion | vigencia | revocabilidad
    public string[] AplicableA { get; init; } = [];
}

public interface ICatalogoRepositorio
{
    Task<IReadOnlyList<Clausula>> ObtenerClausulasActivasAsync(CancellationToken ct = default);
}

public class CatalogoRepositorio(NpgsqlDataSource db) : ICatalogoRepositorio
{
    public async Task<IReadOnlyList<Clausula>> ObtenerClausulasActivasAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT codigo, nombre, tipo, categoria, aplicable_a
            FROM clausulas_template
            WHERE activo = true
            ORDER BY tipo, categoria, orden
            """;

        await using var conn = await db.OpenConnectionAsync(ct);
        var filas = await conn.QueryAsync<Clausula>(new CommandDefinition(sql, cancellationToken: ct));
        return filas.AsList();
    }
}
