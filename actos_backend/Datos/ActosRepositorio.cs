using Dapper;
using Npgsql;

namespace actos_backend.Datos;

public class OpcionActo
{
    public string Codigo { get; init; } = "";
    public string Nombre { get; init; } = "";
    public string Descripcion { get; init; } = "";
    public string Ejemplo { get; init; } = "";
    public string Tipo { get; init; } = "";     // acto | consulta
    public bool Habilitado { get; init; }
}

public class ParteActo
{
    public string Codigo { get; init; } = "";
    public string Nombre { get; init; } = "";
}

public interface IActosRepositorio
{
    /// <summary>Actos y consultas activos, en el orden en que se muestran.</summary>
    Task<IReadOnlyList<OpcionActo>> ObtenerOpcionesAsync(CancellationToken ct = default);

    /// <summary>Si el acto existe y está activo.</summary>
    Task<bool> ExisteAsync(string codigoActo, CancellationToken ct = default);

    /// <summary>Partes activas del acto, en orden. Vacío si el acto no tiene partes cargadas.</summary>
    Task<IReadOnlyList<ParteActo>> ObtenerPartesAsync(string codigoActo, CancellationToken ct = default);
}

public class ActosRepositorio(NpgsqlDataSource db) : IActosRepositorio
{
    public async Task<IReadOnlyList<OpcionActo>> ObtenerOpcionesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT codigo, nombre, descripcion, ejemplo, tipo, habilitado
            FROM actos
            WHERE activo = true
            ORDER BY orden, nombre
            """;

        await using var conn = await db.OpenConnectionAsync(ct);
        var filas = await conn.QueryAsync<OpcionActo>(new CommandDefinition(sql, cancellationToken: ct));
        return filas.AsList();
    }

    public async Task<bool> ExisteAsync(string codigoActo, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM actos WHERE activo AND codigo = @codigoActo)",
            new { codigoActo }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ParteActo>> ObtenerPartesAsync(string codigoActo, CancellationToken ct = default)
    {
        const string sql = """
            SELECT codigo, nombre
            FROM actos_partes
            WHERE activo AND acto_codigo = @codigoActo
            ORDER BY orden, nombre
            """;

        await using var conn = await db.OpenConnectionAsync(ct);
        var filas = await conn.QueryAsync<ParteActo>(new CommandDefinition(sql, new { codigoActo }, cancellationToken: ct));
        return filas.AsList();
    }
}
