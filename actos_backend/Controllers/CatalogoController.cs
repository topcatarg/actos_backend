using actos_backend.Datos;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace actos_backend.Controllers;

/// <summary>Catálogos que el front necesita para mostrar nombres en vez de códigos.</summary>
[ApiController]
[Route("api/catalogo")]
public class CatalogoController(ICatalogoRepositorio catalogo) : ControllerBase
{
    /// <summary>Facultades y condiciones activas (código, nombre, tipo, categoría, a qué tipos de poder aplica).</summary>
    [HttpGet("clausulas")]
    public async Task<IActionResult> Clausulas(CancellationToken ct)
    {
        try
        {
            return Ok(await catalogo.ObtenerClausulasActivasAsync(ct));
        }
        catch (NpgsqlException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Error consultando Postgres.", detalle = ex.Message });
        }
    }
}
