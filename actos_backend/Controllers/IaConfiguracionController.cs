using System.Diagnostics;
using actos_backend.Datos;
using actos_backend.Ia;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Npgsql;

namespace actos_backend.Controllers;

/// <summary>
/// Configuración de IA desde el front: elegir modelo (tabla ia_modelos) y probar la conexión.
/// Las pruebas quedan registradas en ia_pruebas_conexion.
/// </summary>
[ApiController]
[Route("api/ia")]
public class IaConfiguracionController(
    IIaModelosRepositorio modelos,
    ModeloIaActual modeloActual,
    IIaClient ia,
    IOptions<IaOptions> opciones,
    ILogger<IaConfiguracionController> logger) : ControllerBase
{
    /// <summary>La prueba usa el mismo límite que las llamadas normales (Ia:TimeoutSegundos).</summary>
    private TimeSpan TimeoutPrueba => TimeSpan.FromSeconds(opciones.Value.TimeoutSegundos);

    public record ModelosResponse(string EnUso, string PorDefecto, IReadOnlyList<IaModeloFila> Modelos);
    public record SeleccionRequest(string Modelo);
    public record PruebaRequest(string? Modelo);

    /// <summary>Modelos activos y cuál se está usando.</summary>
    [HttpGet("modelos")]
    public async Task<IActionResult> Modelos(CancellationToken ct)
    {
        try
        {
            return Ok(await EstadoModelosAsync(ct));
        }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

    /// <summary>Cambia el modelo en uso. Aplica a todas las llamadas siguientes.</summary>
    [HttpPut("modelo")]
    public async Task<IActionResult> Seleccionar([FromBody] SeleccionRequest pedido, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pedido.Modelo))
            return BadRequest(new { error = "Falta 'modelo'." });

        try
        {
            if (!await modelos.SeleccionarAsync(pedido.Modelo, ct))
                return NotFound(new { error = $"El modelo '{pedido.Modelo}' no existe o está apagado." });

            modeloActual.Invalidar();
            return Ok(await EstadoModelosAsync(ct));
        }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

    /// <summary>
    /// Prueba la conexión con el modelo indicado (o el que está en uso) y la registra.
    /// Siempre devuelve 200 con ok true/false: el error del proveedor es el resultado de la prueba, no una falla del endpoint.
    /// </summary>
    [HttpPost("conexion")]
    public async Task<IActionResult> Probar([FromBody] PruebaRequest? pedido, CancellationToken ct)
    {
        try
        {
            ModeloIa modelo;
            if (string.IsNullOrWhiteSpace(pedido?.Modelo))
            {
                modelo = await modeloActual.ObtenerAsync(ct);
            }
            else
            {
                var fila = await modelos.ObtenerActivoAsync(pedido.Modelo, ct);
                if (fila is null)
                    return NotFound(new { error = $"El modelo '{pedido.Modelo}' no existe o está apagado." });
                modelo = modeloActual.DesdeFila(fila);
            }

            var resultado = await EjecutarPruebaAsync(modelo, ct);
            logger.LogInformation("Prueba de conexión {Modelo}: {Resultado} en {Ms} ms {Error}",
                resultado.Modelo, resultado.Ok ? "OK" : "FALLÓ", resultado.DuracionMs, resultado.Error ?? "");
            return Ok(await modelos.RegistrarPruebaAsync(resultado, ct));
        }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

    /// <summary>Últimas pruebas de conexión (las más nuevas primero).</summary>
    [HttpGet("conexion")]
    public async Task<IActionResult> Pruebas([FromQuery] int cantidad = 10, CancellationToken ct = default)
    {
        try
        {
            return Ok(await modelos.UltimasPruebasAsync(Math.Clamp(cantidad, 1, 50), ct));
        }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

    private async Task<PruebaConexionFila> EjecutarPruebaAsync(ModeloIa modelo, CancellationToken ct)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeoutPrueba);
        var reloj = Stopwatch.StartNew();

        try
        {
            var r = await ia.CompletarAsync(
                "Sos una prueba de conexión. Respondé en una sola oración corta, en español rioplatense.",
                "¿Estás funcionando?", modelo: modelo, ct: limite.Token);

            return new PruebaConexionFila
            {
                Modelo = modelo.Modelo, Ok = true, DuracionMs = r.DuracionMs, Respuesta = r.Texto
            };
        }
        catch (IaException ex)
        {
            return new PruebaConexionFila
            {
                Modelo = modelo.Modelo, Ok = false, DuracionMs = reloj.ElapsedMilliseconds,
                StatusProveedor = ex.StatusCode, Error = ex.Message, Detalle = Recortar(ex.Detalle)
            };
        }
        catch (OperationCanceledException) when (limite.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new PruebaConexionFila
            {
                Modelo = modelo.Modelo, Ok = false, DuracionMs = reloj.ElapsedMilliseconds,
                Error = $"Timeout: el proveedor no respondió en {TimeoutPrueba.TotalSeconds:0} s."
            };
        }
    }

    private async Task<ModelosResponse> EstadoModelosAsync(CancellationToken ct)
    {
        var lista = await modelos.ListarActivosAsync(ct);
        var enUso = await modeloActual.ObtenerAsync(ct);
        return new ModelosResponse(enUso.Modelo, modeloActual.PorDefecto.Modelo, lista);
    }

    /// <summary>El cuerpo de error del proveedor puede ser largo; para el registro alcanza con el principio.</summary>
    private static string? Recortar(string? s) => s is { Length: > 2000 } ? s[..2000] + "…" : s;

    private ObjectResult ErrorDb(NpgsqlException ex) => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { error = "Error consultando Postgres.", detalle = ex.Message });
}
