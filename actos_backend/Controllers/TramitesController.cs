using actos_backend.Datos;
using actos_backend.Ia;
using actos_backend.Poderes;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace actos_backend.Controllers;

/// <summary>
/// Conversación persistente: el trámite vive en Postgres y cada pedido trae solo el código y el texto.
/// </summary>
[ApiController]
[Route("api/tramites")]
public class TramitesController(ConversacionPoder conversacion, ITramitesRepositorio tramites) : ControllerBase
{
    public record MensajeRequest(string Texto);

    /// <summary>Lo que necesita el front para mostrar la conversación.</summary>
    public record RespuestaTramite(string CodigoTramite, string EstadoTramite, string Mensaje, EstadoPoder Estado, PoderExtraido Poder, object Detalle);

    /// <summary>Crea un trámite de poder a partir del primer mensaje.</summary>
    [HttpPost("poder")]
    public async Task<IActionResult> Crear([FromBody] MensajeRequest pedido, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pedido.Texto))
            return BadRequest(new { error = "Falta 'texto'." });

        try
        {
            var r = await conversacion.IniciarAsync(pedido.Texto, ct);
            var estadoTramite = ConversacionPoder.EstadoTramite(r.Estado);
            var guardado = await tramites.CrearAsync("poder", estadoTramite, r.Poder, r.Estado.Siguiente,
                pedido.Texto, r.Mensaje.Mensaje, ct);

            return Ok(new RespuestaTramite(guardado.CodigoTramite, estadoTramite, r.Mensaje.Mensaje, r.Estado, r.Poder,
                new { r.Descartes, r.Avisos, guion = r.Mensaje.Guion }));
        }
        catch (IaException ex) { return ErrorIa(ex); }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

    /// <summary>Siguiente mensaje del usuario en un trámite existente.</summary>
    [HttpPost("{codigo}/mensajes")]
    public async Task<IActionResult> Responder(string codigo, [FromBody] MensajeRequest pedido, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pedido.Texto))
            return BadRequest(new { error = "Falta 'texto'." });

        try
        {
            var tramite = await tramites.ObtenerAsync(codigo, ct);
            if (tramite is null) return NotFound(new { error = $"No existe el trámite {codigo}." });

            var r = await conversacion.ResponderAsync(tramite.Poder, tramite.Siguiente, pedido.Texto, ct);
            var estadoTramite = ConversacionPoder.EstadoTramite(r.Estado);
            await tramites.ActualizarAsync(tramite, estadoTramite, r.Poder, r.Estado.Siguiente,
                pedido.Texto, r.Mensaje.Mensaje, ct);

            return Ok(new RespuestaTramite(codigo, estadoTramite, r.Mensaje.Mensaje, r.Estado, r.Poder,
                new { r.Acciones, r.Avisos, guion = r.Mensaje.Guion }));
        }
        catch (TramiteModificadoException ex) { return Conflict(new { error = ex.Message }); }
        catch (IaException ex) { return ErrorIa(ex); }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

    /// <summary>El trámite como está, con el historial de la conversación (para retomarlo).</summary>
    [HttpGet("{codigo}")]
    public async Task<IActionResult> Obtener(string codigo, CancellationToken ct)
    {
        try
        {
            var tramite = await tramites.ObtenerAsync(codigo, ct);
            if (tramite is null) return NotFound(new { error = $"No existe el trámite {codigo}." });

            var mensajes = await tramites.MensajesAsync(tramite.Id, ct);
            return Ok(new
            {
                codigoTramite = tramite.CodigoTramite,
                estadoTramite = tramite.Estado,
                poder = tramite.Poder,
                estado = MotorFaltantes.Evaluar(tramite.Poder),
                mensajes
            });
        }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

    private ObjectResult ErrorIa(IaException ex) => StatusCode(StatusCodes.Status502BadGateway,
        new { error = ex.Message, statusProveedor = ex.StatusCode, detalle = ex.Detalle });

    private ObjectResult ErrorDb(NpgsqlException ex) => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { error = "Error consultando Postgres.", detalle = ex.Message });
}
