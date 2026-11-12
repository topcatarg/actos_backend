using actos_backend.Ia;
using actos_backend.Poderes;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace actos_backend.Controllers;

[ApiController]
[Route("api/poderes")]
public class PoderesController(PoderExtractor extractor, PoderCompletador completador) : ControllerBase
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
        catch (IaException ex) { return ErrorIa(ex); }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

    /// <summary>Extrae y completa: datos de la base para personas con DNI, candidatos por nombre y defaults.</summary>
    [HttpPost("procesar")]
    public async Task<IActionResult> Procesar([FromBody] ExtraerRequest pedido, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pedido.Texto))
            return BadRequest(new { error = "Falta 'texto'." });

        try
        {
            var extraccion = await extractor.ExtraerAsync(pedido.Texto, ct);
            var avisos = await completador.CompletarAsync(extraccion.Poder, ct);
            var estado = MotorFaltantes.Evaluar(extraccion.Poder, avisos);
            return Ok(new ResultadoProceso(extraccion.Poder, estado, extraccion.Descartes, avisos,
                extraccion.Modelo, extraccion.TokensEntrada, extraccion.TokensSalida, extraccion.DuracionMs));
        }
        catch (IaException ex) { return ErrorIa(ex); }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

    /// <summary>
    /// Evalúa un poder ya armado (sin IA ni base): sirve para probar el motor de faltantes
    /// editando a mano el JSON que devuelve /procesar.
    /// </summary>
    [HttpPost("estado")]
    public IActionResult Estado([FromBody] PoderExtraido poder) => Ok(MotorFaltantes.Evaluar(poder));

    private ObjectResult ErrorIa(IaException ex) =>
        StatusCode(StatusCodes.Status502BadGateway, new
        {
            error = ex.Message,
            statusProveedor = ex.StatusCode,
            detalle = ex.Detalle
        });

    private ObjectResult ErrorDb(NpgsqlException ex) =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            error = "Error consultando Postgres.",
            detalle = ex.Message
        });
}
