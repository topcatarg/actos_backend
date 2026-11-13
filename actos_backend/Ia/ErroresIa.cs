using Microsoft.AspNetCore.Mvc;

namespace actos_backend.Ia;

/// <summary>
/// Cómo se le contesta al front cuando falla la IA. Los errores por demanda (proveedor saturado,
/// límite de pedidos, timeout) llevan un mensaje para el usuario; el resto, el mensaje técnico.
/// Lo usan los controllers del chat (asistente y trámites).
/// </summary>
public static class ErroresIa
{
    /// <summary>Lo lee el front para mostrar el mensaje tal cual, sin "No pude procesar...".</summary>
    public const string CodigoSaturada = "ia_saturada";
    public const string CodigoError = "ia_error";

    public const string MensajeSaturada =
        "En este momento la IA está cansada 😴. Volvé a intentarlo en unos segundos, o cambiá de modelo desde ⚙️.";

    /// <summary>Segundos sugeridos para reintentar (header Retry-After).</summary>
    private const int SegundosReintento = 10;

    /// <summary>
    /// 429: demasiados pedidos / cuota. 503: modelo sobrecargado (Gemini responde así cuando hay demanda).
    /// 529: "overloaded" de algunos proveedores.
    /// </summary>
    private static readonly HashSet<int> StatusPorDemanda = [429, 503, 529];

    /// <summary>Si el error es por demanda: reintentar más tarde o cambiar de modelo lo resuelve.</summary>
    public static bool EsPorDemanda(IaException ex) =>
        (ex.StatusCode is int s && StatusPorDemanda.Contains(s))
        // OpenAiCompatibleClient arma así el mensaje cuando el proveedor no contesta a tiempo.
        || ex.Message.StartsWith("Timeout", StringComparison.OrdinalIgnoreCase);

    public static ObjectResult Respuesta(ControllerBase controller, IaException ex, ILogger logger)
    {
        var ruta = controller.Request.Path;

        if (EsPorDemanda(ex))
        {
            // Es esperable: warning, no error.
            logger.LogWarning("IA saturada en {Ruta}: {Error} (status {Status}). {Detalle}",
                ruta, ex.Message, ex.StatusCode, ex.Detalle);
            controller.Response.Headers.RetryAfter = SegundosReintento.ToString();
            return controller.StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = MensajeSaturada,
                codigo = CodigoSaturada,
                statusProveedor = ex.StatusCode,
                detalle = ex.Detalle
            });
        }

        logger.LogError("Error de IA en {Ruta}: {Error} (status {Status}). {Detalle}",
            ruta, ex.Message, ex.StatusCode, ex.Detalle);
        return controller.StatusCode(StatusCodes.Status502BadGateway, new
        {
            error = ex.Message,
            codigo = CodigoError,
            statusProveedor = ex.StatusCode,
            detalle = ex.Detalle
        });
    }
}
