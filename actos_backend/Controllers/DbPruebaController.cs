using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace actos_backend.Controllers;

/// <summary>
/// Endpoint temporal para probar la conexión con Postgres. Se borra cuando tengamos
/// los endpoints reales.
/// </summary>
[ApiController]
[Route("api/db")]
public class DbPruebaController(NpgsqlDataSource db) : ControllerBase
{
    [HttpGet("prueba")]
    public async Task<IActionResult> Probar(CancellationToken ct)
    {
        const string sql = """
            SELECT
                current_database(),
                version(),
                (SELECT count(*) FROM pf_maestro),
                (SELECT count(*) FROM clausulas_template WHERE tipo = 'facultad'),
                (SELECT count(*) FROM clausulas_template WHERE tipo = 'condicion')
            """;

        try
        {
            await using var cmd = db.CreateCommand(sql);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);

            return Ok(new
            {
                baseDeDatos = reader.GetString(0),
                version = reader.GetString(1),
                personas = reader.GetInt64(2),
                facultades = reader.GetInt64(3),
                condiciones = reader.GetInt64(4)
            });
        }
        catch (NpgsqlException ex)
        {
            // Cubre tanto "no se pudo conectar" como errores de Postgres (ej. tabla inexistente).
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "No se pudo consultar Postgres.",
                detalle = ex.Message
            });
        }
    }
}
