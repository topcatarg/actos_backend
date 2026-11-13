using actos_backend.Migraciones;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace actos_backend.Controllers;

/// <summary>Versión del esquema y migraciones.</summary>
[ApiController]
[Route("api/db")]
public class BaseDatosController(MigradorBaseDatos migrador, IHostEnvironment entorno) : ControllerBase
{
    /// <summary>En qué versión está la base y qué falta aplicar.</summary>
    [HttpGet("version")]
    public async Task<IActionResult> Version(CancellationToken ct)
    {
        try
        {
            var estado = await migrador.ObtenerEstadoAsync(ct);
            return Ok(new
            {
                estado.Versionada,
                estado.VersionActual,
                estado.UltimaVersion,
                alDia = estado.Versionada && estado.Pendientes.Count == 0 && estado.Modificadas.Count == 0,
                estado.Pendientes,
                estado.Modificadas
            });
        }
        catch (MigracionException ex) { return Conflict(new { error = ex.Message }); }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

    /// <summary>Crea la base desde cero o la migra a la última versión, según dónde esté.</summary>
    [HttpPost("migrar")]
    public async Task<IActionResult> Migrar(CancellationToken ct)
    {
        try
        {
            return Ok(await migrador.MigrarAsync(ct));
        }
        catch (MigracionException ex) { return Conflict(new { error = ex.Message }); }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

    /// <summary>
    /// Borra toda la base y la vuelve a crear. Solo en Development y con ?confirmar=true.
    /// </summary>
    [HttpPost("regenerar")]
    public async Task<IActionResult> Regenerar([FromQuery] bool confirmar, CancellationToken ct)
    {
        if (!entorno.IsDevelopment()) return NotFound();
        if (!confirmar)
            return BadRequest(new { error = "Esto borra TODOS los datos. Repetí el pedido con ?confirmar=true." });

        try
        {
            return Ok(await migrador.RegenerarAsync(ct));
        }
        catch (MigracionException ex) { return Conflict(new { error = ex.Message }); }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

    private ObjectResult ErrorDb(NpgsqlException ex) => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { error = "Error consultando Postgres.", detalle = ex.Message });
}
