using actos_backend.Datos;
using actos_backend.Ia;
using actos_backend.Poderes;
using actos_backend.Tramites;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace actos_backend.Controllers;

/// <summary>
/// Conversación persistente: el trámite vive en Postgres y cada pedido trae solo el código y el texto.
/// </summary>
[ApiController]
[Route("api/tramites")]
public class TramitesController(TramitesServicio servicio, ITramitesRepositorio tramites,
    ILogger<TramitesController> logger) : ControllerBase
{
    public record MensajeRequest(string Texto);

    /// <summary>Crea un trámite de poder a partir del primer mensaje (sin pasar por el clasificador).</summary>
    [HttpPost("poder")]
    public async Task<IActionResult> Crear([FromBody] MensajeRequest pedido, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pedido.Texto))
            return BadRequest(new { error = "Falta 'texto'." });

        try
        {
            return Ok(await servicio.CrearPoderAsync(pedido.Texto, ct));
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
            var r = await servicio.ResponderAsync(codigo, pedido.Texto, ct);
            return r is null ? NotFound(new { error = $"No existe el trámite {codigo}." }) : Ok(r);
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
                datos = tramite.Poder,
                estado = MotorFaltantes.Evaluar(tramite.Poder),
                mensajes
            });
        }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

    private ObjectResult ErrorIa(IaException ex)
    {
        logger.LogError("Error de IA en {Ruta}: {Error} (status {Status}). {Detalle}",
            Request.Path, ex.Message, ex.StatusCode, ex.Detalle);
        return StatusCode(StatusCodes.Status502BadGateway,
            new { error = ex.Message, statusProveedor = ex.StatusCode, detalle = ex.Detalle });
    }

    private ObjectResult ErrorDb(NpgsqlException ex)
    {
        // Con PostgresException el log trae el código SQL (ej. 42703 = columna inexistente) y la sentencia.
        logger.LogError(ex, "Error de Postgres en {Ruta}: {Error}", Request.Path, ex.Message);
        return StatusCode(StatusCodes.Status503ServiceUnavailable,
            new { error = "Error consultando Postgres.", detalle = ex.Message });
    }
}
