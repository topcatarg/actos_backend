using actos_backend.Asistente;
using actos_backend.Datos;
using actos_backend.Ia;
using actos_backend.Tramites;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace actos_backend.Controllers;

/// <summary>
/// Entrada al asistente: bienvenida, ayuda y primer mensaje en texto libre.
/// El primer mensaje se clasifica contra la tabla actos y, si está claro, se crea el trámite.
/// Los mensajes siguientes van directo a /api/tramites/{codigo}/mensajes.
/// </summary>
[ApiController]
[Route("api/asistente")]
public class AsistenteController(
    IActosRepositorio actos,
    ClasificadorIntencion clasificador,
    TramitesServicio tramites) : ControllerBase
{
    // TODO: si hace falta personalizarlos por escribanía, pasarlos a configuración o a una tabla.
    private const string MensajeBienvenida = "¡Hola! Soy Neo. Contame qué necesitás: un acto para preparar o algo que quieras consultar.";
    private const string MensajeAyuda = "Esto es lo que puedo hacer por ahora. Escribime el pedido con tus palabras, como en los ejemplos:";
    /// <summary>Código de la tabla actos que abre el historial de trámites.</summary>
    private const string ConsultaHistorial = "consulta";
    private const string MensajeHistorial = "Te abro la lista de trámites abiertos. Tocá uno para retomarlo donde quedó.";
    public record InicioResponse(string Mensaje, IReadOnlyList<OpcionActo> Opciones);

    /// <param name="Acto">Si viene (el usuario eligió una opción de una aclaración), no se clasifica: se usa ese acto.</param>
    public record MensajeRequest(string Texto, string? Acto = null);

    /// <summary>
    /// tipo: tramite | ayuda | aclaracion | no_disponible.
    /// Opciones viene en ayuda y aclaracion; Tramite solo cuando tipo = tramite.
    /// </summary>
    public record RespuestaAsistente(string Tipo, string Mensaje, IReadOnlyList<OpcionActo>? Opciones = null,
        string? Acto = null, RespuestaTramite? Tramite = null);

    [HttpGet("inicio")]
    public async Task<IActionResult> Inicio(CancellationToken ct)
    {
        try
        {
            var opciones = await actos.ObtenerOpcionesAsync(ct);
            return Ok(new InicioResponse(MensajeBienvenida, opciones));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable) { return BaseSinMigrar(ex); }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

    /// <summary>Primer mensaje de una conversación nueva.</summary>
    [HttpPost("mensajes")]
    public async Task<IActionResult> Mensaje([FromBody] MensajeRequest pedido, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pedido.Texto))
            return BadRequest(new { error = "Falta 'texto'." });

        try
        {
            var opciones = await actos.ObtenerOpcionesAsync(ct);

            var intencion = pedido.Acto is { Length: > 0 }
                ? new Intencion(TipoIntencion.Opcion, pedido.Acto.ToLowerInvariant(), [])
                : await clasificador.ClasificarAsync(pedido.Texto, opciones, ct);

            return Ok(await ResolverAsync(intencion, pedido.Texto, opciones, ct));
        }
        catch (IaException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = ex.Message, statusProveedor = ex.StatusCode, detalle = ex.Detalle });
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable) { return BaseSinMigrar(ex); }
        catch (NpgsqlException ex) { return ErrorDb(ex); }
    }

    private async Task<RespuestaAsistente> ResolverAsync(Intencion intencion, string texto,
        IReadOnlyList<OpcionActo> opciones, CancellationToken ct)
    {
        switch (intencion.Tipo)
        {
            case TipoIntencion.Ayuda:
                return new RespuestaAsistente("ayuda", MensajeAyuda, opciones);

            case TipoIntencion.FueraDeCatalogo:
                return new RespuestaAsistente("no_disponible",
                    "Eso todavía no lo puedo preparar. Si querés ver lo que sí hago, tocá «¿Qué puedo hacer?».");

            case TipoIntencion.Opcion:
                var opcion = opciones.FirstOrDefault(o => o.Codigo.Equals(intencion.Codigo, StringComparison.OrdinalIgnoreCase));
                if (opcion is null)
                    return Aclarar(intencion.Alternativas, opciones);
                // Las consultas no crean un trámite: el front muestra el historial.
                if (opcion.Habilitado && opcion.Codigo.Equals(ConsultaHistorial, StringComparison.OrdinalIgnoreCase))
                    return new RespuestaAsistente("historial", MensajeHistorial, Acto: opcion.Codigo);
                if (!opcion.Habilitado || !TramitesServicio.Soporta(opcion.Codigo))
                    return new RespuestaAsistente("no_disponible",
                        $"{opcion.Nombre}: todavía no está disponible. Por ahora puedo ayudarte con lo que ves en «¿Qué puedo hacer?».",
                        Acto: opcion.Codigo);

                var tramite = await tramites.CrearAsync(opcion.Codigo, texto, ct);
                return new RespuestaAsistente("tramite", tramite.Mensaje, Acto: opcion.Codigo, Tramite: tramite);

            default:
                return Aclarar(intencion.Alternativas, opciones);
        }
    }

    private static RespuestaAsistente Aclarar(IReadOnlyList<string> alternativas, IReadOnlyList<OpcionActo> opciones)
    {
        var sugeridas = alternativas
            .Select(c => opciones.FirstOrDefault(o => o.Codigo.Equals(c, StringComparison.OrdinalIgnoreCase)))
            .OfType<OpcionActo>()
            .ToList();

        return sugeridas.Count > 0
            ? new RespuestaAsistente("aclaracion", "No me quedó del todo claro. ¿Te referís a alguna de estas?", sugeridas)
            : new RespuestaAsistente("aclaracion",
                "No me quedó claro qué necesitás. Contame con un poco más de detalle, o tocá «¿Qué puedo hacer?» para ver ejemplos.",
                []);
    }

    private ObjectResult BaseSinMigrar(PostgresException ex) => StatusCode(StatusCodes.Status503ServiceUnavailable, new
    {
        error = "La base no está migrada. Llamá a POST /api/db/migrar.",
        detalle = ex.MessageText
    });

    private ObjectResult ErrorDb(NpgsqlException ex) => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { error = "Error consultando Postgres.", detalle = ex.Message });
}
