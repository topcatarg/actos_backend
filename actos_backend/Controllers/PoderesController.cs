using actos_backend.Ia;
using actos_backend.Poderes;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace actos_backend.Controllers;

/// <summary>
/// Endpoints SIN estado: el poder viaja completo en cada pedido. Sirven para probar cada pieza
/// por separado. La conversación persistente está en /api/tramites.
/// </summary>
[ApiController]
[Route("api/poderes")]
public class PoderesController(PoderExtractor extractor, ConversacionPoder conversacion, RedactorPreguntas redactor) : ControllerBase
{
    public record ExtraerRequest(string Texto);

    /// <summary>Texto libre → datos del poder, con el fragmento y la posición de cada dato.</summary>
    [HttpPost("extraer")]
    public async Task<IActionResult> Extraer([FromBody] ExtraerRequest pedido, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pedido.Texto))
            return BadRequest(new { error = "Falta 'texto'." });

        try { return Ok(await extractor.ExtraerAsync(pedido.Texto, ct)); }
        catch (IaException ex) { return ErrorIa(ex); }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

    /// <summary>Primer mensaje: extrae, completa, evalúa y redacta la primera pregunta.</summary>
    [HttpPost("procesar")]
    public async Task<IActionResult> Procesar([FromBody] ExtraerRequest pedido, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pedido.Texto))
            return BadRequest(new { error = "Falta 'texto'." });

        try { return Ok(await conversacion.IniciarAsync(pedido.Texto, ct)); }
        catch (IaException ex) { return ErrorIa(ex); }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

    /// <summary>
    /// Dos formas de mandar el contexto:
    /// - "anterior": la respuesta completa del paso previo (/procesar o /responder), pegada tal cual.
    /// - "poder" + "preguntado": el poder y el grupo "siguiente" que se usó para la última pregunta.
    /// </summary>
    public record ResponderRequest(string Respuesta, PasoAnterior? Anterior, PoderExtraido? Poder, List<Pendiente>? Preguntado);

    /// <summary>Lo único que se lee de la respuesta anterior; el resto del JSON se ignora.</summary>
    public record PasoAnterior(PoderExtraido? Poder, EstadoPoder? Estado);

    /// <summary>Aplica la respuesta del usuario a la última pregunta.</summary>
    [HttpPost("responder")]
    public async Task<IActionResult> Responder([FromBody] ResponderRequest pedido, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pedido.Respuesta))
            return BadRequest(new { error = "Falta 'respuesta'." });

        var poder = pedido.Anterior?.Poder ?? pedido.Poder;
        var preguntado = pedido.Anterior?.Estado?.Siguiente ?? pedido.Preguntado ?? [];
        if (poder is null)
            return BadRequest(new { error = "Falta el poder: mandá 'anterior' (respuesta del paso previo) o 'poder' + 'preguntado'." });

        try { return Ok(await conversacion.ResponderAsync(poder, preguntado, pedido.Respuesta, ct)); }
        catch (IaException ex) { return ErrorIa(ex); }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

    /// <summary>Evalúa un poder ya armado (sin IA ni base): para probar el motor de faltantes.</summary>
    [HttpPost("estado")]
    public IActionResult Estado([FromBody] PoderExtraido poder) => Ok(MotorFaltantes.Evaluar(poder));

    public record RedactarRequest(PoderExtraido Poder, bool PrimerMensaje = false);

    /// <summary>Evalúa un poder ya armado y redacta la próxima pregunta: para probar el tono.</summary>
    [HttpPost("redactar")]
    public async Task<IActionResult> Redactar([FromBody] RedactarRequest pedido, CancellationToken ct)
    {
        try
        {
            var estado = MotorFaltantes.Evaluar(pedido.Poder);
            var mensaje = await redactor.RedactarAsync(pedido.Poder, estado, pedido.PrimerMensaje, ct);
            return Ok(new { mensaje, estado });
        }
        catch (IaException ex) { return ErrorIa(ex); }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

    private ObjectResult ErrorIa(IaException ex) => StatusCode(StatusCodes.Status502BadGateway,
        new { error = ex.Message, statusProveedor = ex.StatusCode, detalle = ex.Detalle });

    private ObjectResult ErrorDb(NpgsqlException ex) => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { error = "Error consultando Postgres.", detalle = ex.Message });
}
