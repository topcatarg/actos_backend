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
    private const int LimiteMaximo = 200;

    public record MensajeRequest(string Texto);

    /// <summary>
    /// Historial de trámites, del más reciente al más viejo.
    /// Por defecto solo los abiertos (todo lo que no está confirmado).
    /// </summary>
    /// <param name="todos">true = incluye los confirmados.</param>
    /// <param name="tipo">Código del acto (poder, compraventa...). Vacío = todos.</param>
    /// <param name="limite">Cuántos traer (1 a 200).</param>
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] bool todos = false, [FromQuery] string? tipo = null,
        [FromQuery] int limite = 50, CancellationToken ct = default)
    {
        try
        {
            var filtroTipo = string.IsNullOrWhiteSpace(tipo) ? null : tipo.Trim().ToLowerInvariant();
            return Ok(await tramites.ListarAsync(!todos, filtroTipo, Math.Clamp(limite, 1, LimiteMaximo), ct));
        }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

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
                // El front lo necesita al retomar: con esto sabe qué acto es y qué pestañas mostrar.
                tipoTramite = tramite.TipoTramite,
                estadoTramite = tramite.Estado,
                datos = tramite.Poder,
                estado = MotorFaltantes.Evaluar(tramite.Poder),
                mensajes
            });
        }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

    /// <summary>Por demanda: mensaje amigable (503). Otro error: el técnico (502). Ver ErroresIa.</summary>
    private ObjectResult ErrorIa(IaException ex) => ErroresIa.Respuesta(this, ex, logger);

    private ObjectResult ErrorDb(NpgsqlException ex)
    {
        // Con PostgresException el log trae el código SQL (ej. 42703 = columna inexistente) y la sentencia.
        logger.LogError(ex, "Error de Postgres en {Ruta}: {Error}", Request.Path, ex.Message);
        return StatusCode(StatusCodes.Status503ServiceUnavailable,
            new { error = "Error consultando Postgres.", detalle = ex.Message });
    }
}
