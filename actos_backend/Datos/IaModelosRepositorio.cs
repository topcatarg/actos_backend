using Dapper;
using Npgsql;

namespace actos_backend.Datos;

public class IaModeloFila
{
    public string Modelo { get; init; } = "";
    public string Nombre { get; init; } = "";
    public string Descripcion { get; init; } = "";
    public string? NivelRazonamiento { get; init; }
    public bool Activo { get; init; }
    public bool EnUso { get; init; }
}

public class PruebaConexionFila
{
    public int Id { get; init; }
    public DateTime Fecha { get; init; }
    public string Modelo { get; init; } = "";
    public bool Ok { get; init; }
    public long DuracionMs { get; init; }
    public int? StatusProveedor { get; init; }
    public string? Respuesta { get; init; }
    public string? Error { get; init; }
    public string? Detalle { get; init; }
}

public interface IIaModelosRepositorio
{
    /// <summary>Modelos activos, en orden de presentación.</summary>
    Task<IReadOnlyList<IaModeloFila>> ListarActivosAsync(CancellationToken ct = default);

    /// <summary>Un modelo activo por su id de proveedor, o null.</summary>
    Task<IaModeloFila?> ObtenerActivoAsync(string modelo, CancellationToken ct = default);

    /// <summary>El modelo en uso (si está activo), o null.</summary>
    Task<IaModeloFila?> ObtenerEnUsoAsync(CancellationToken ct = default);

    /// <summary>Marca el modelo como en uso. False si no existe o no está activo.</summary>
    Task<bool> SeleccionarAsync(string modelo, CancellationToken ct = default);

    Task<PruebaConexionFila> RegistrarPruebaAsync(PruebaConexionFila prueba, CancellationToken ct = default);

    Task<IReadOnlyList<PruebaConexionFila>> UltimasPruebasAsync(int cantidad, CancellationToken ct = default);
}

public class IaModelosRepositorio(NpgsqlDataSource db) : IIaModelosRepositorio
{
    private const string Columnas = "modelo, nombre, descripcion, nivel_razonamiento, activo, en_uso";

    public async Task<IReadOnlyList<IaModeloFila>> ListarActivosAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var filas = await conn.QueryAsync<IaModeloFila>(new CommandDefinition(
            $"SELECT {Columnas} FROM ia_modelos WHERE activo ORDER BY orden, nombre", cancellationToken: ct));
        return filas.AsList();
    }

    public async Task<IaModeloFila?> ObtenerActivoAsync(string modelo, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<IaModeloFila>(new CommandDefinition(
            $"SELECT {Columnas} FROM ia_modelos WHERE activo AND modelo = @modelo", new { modelo }, cancellationToken: ct));
    }

    public async Task<IaModeloFila?> ObtenerEnUsoAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<IaModeloFila>(new CommandDefinition(
            $"SELECT {Columnas} FROM ia_modelos WHERE activo AND en_uso", cancellationToken: ct));
    }

    public async Task<bool> SeleccionarAsync(string modelo, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var existe = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM ia_modelos WHERE activo AND modelo = @modelo)",
            new { modelo }, tx, cancellationToken: ct));
        if (!existe) return false;   // el dispose hace rollback

        // Primero apagar todos (el índice único no admite dos en_uso a la vez), después prender el elegido.
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE ia_modelos SET en_uso = FALSE WHERE en_uso", transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE ia_modelos SET en_uso = TRUE WHERE modelo = @modelo", new { modelo }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<PruebaConexionFila> RegistrarPruebaAsync(PruebaConexionFila p, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<PruebaConexionFila>(new CommandDefinition("""
            INSERT INTO ia_pruebas_conexion (modelo, ok, duracion_ms, status_proveedor, respuesta, error, detalle)
            VALUES (@Modelo, @Ok, @DuracionMs, @StatusProveedor, @Respuesta, @Error, @Detalle)
            RETURNING id, fecha, modelo, ok, duracion_ms, status_proveedor, respuesta, error, detalle
            """, p, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PruebaConexionFila>> UltimasPruebasAsync(int cantidad, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var filas = await conn.QueryAsync<PruebaConexionFila>(new CommandDefinition("""
            SELECT id, fecha, modelo, ok, duracion_ms, status_proveedor, respuesta, error, detalle
            FROM ia_pruebas_conexion
            ORDER BY fecha DESC
            LIMIT @cantidad
            """, new { cantidad }, cancellationToken: ct));
        return filas.AsList();
    }
}
