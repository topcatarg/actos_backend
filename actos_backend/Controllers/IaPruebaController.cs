using actos_backend.Ia;
using Microsoft.AspNetCore.Mvc;

namespace actos_backend.Controllers;

/// <summary>
/// Endpoint temporal para probar la conexión con el proveedor de IA. Se borra cuando
/// tengamos los endpoints reales.
/// </summary>
[ApiController]
[Route("api/ia")]
public class IaPruebaController(IIaClient ia) : ControllerBase
{
    public record PruebaRequest(string Texto, string? Instrucciones);

    [HttpPost("prueba")]
    public async Task<IActionResult> Probar([FromBody] PruebaRequest pedido, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pedido.Texto))
            return BadRequest(new { error = "Falta 'texto'." });

        var instrucciones = string.IsNullOrWhiteSpace(pedido.Instrucciones)
            ? "Sos un asistente. Respondé en español rioplatense, en una o dos oraciones."
            : pedido.Instrucciones;

        try
        {
            var respuesta = await ia.CompletarAsync(instrucciones, pedido.Texto, ct);
            return Ok(respuesta);
        }
        catch (IaException ex)
        {
            // 502: el problema está del lado del proveedor, no en nuestro request.
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = ex.Message,
                statusProveedor = ex.StatusCode,
                detalle = ex.Detalle
            });
        }
    }
}
