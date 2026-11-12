using actos_backend.Ia;
using actos_backend.Poderes;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace actos_backend.Controllers;

[ApiController]
[Route("api/poderes")]
public class PoderesController(PoderExtractor extractor) : ControllerBase
{
    public record ExtraerRequest(string Texto);

    /// <summary>Texto libre → datos del poder, con el fragmento y la posición de cada dato.</summary>
    [HttpPost("extraer")]
    public async Task<IActionResult> Extraer([FromBody] ExtraerRequest pedido, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pedido.Texto))
            return BadRequest(new { error = "Falta 'texto'." });

        try
        {
            return Ok(await extractor.ExtraerAsync(pedido.Texto, ct));
        }
        catch (IaException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = ex.Message,
                statusProveedor = ex.StatusCode,
                detalle = ex.Detalle
            });
        }
        catch (NpgsqlException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "No se pudo leer el catálogo de Postgres.",
                detalle = ex.Message
            });
        }
    }
}
