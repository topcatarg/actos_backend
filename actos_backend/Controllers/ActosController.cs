using actos_backend.Datos;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace actos_backend.Controllers;

/// <summary>Catálogo de actos: qué partes lleva cada uno (para armar los pasos del front).</summary>
[ApiController]
[Route("api/actos")]
public class ActosController(IActosRepositorio actos) : ControllerBase
{
    /// <summary>Partes del acto, en orden. 404 si el acto no existe o está apagado.</summary>
    [HttpGet("{codigo}/partes")]
    public async Task<IActionResult> Partes(string codigo, CancellationToken ct)
    {
        try
        {
            if (!await actos.ExisteAsync(codigo, ct))
                return NotFound(new { error = $"No existe el acto '{codigo}'." });

            return Ok(await actos.ObtenerPartesAsync(codigo, ct));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "La base no está migrada. Llamá a POST /api/db/migrar.",
                detalle = ex.MessageText
            });
        }
        catch (NpgsqlException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Error consultando Postgres.", detalle = ex.Message });
        }
    }
}
