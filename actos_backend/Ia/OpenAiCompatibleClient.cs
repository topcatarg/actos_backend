using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace actos_backend.Ia;

/// <summary>
/// Cliente para cualquier endpoint con el formato de chat completions de OpenAI.
/// Con Gemini se usa su capa de compatibilidad; para pasar a OpenAI se cambia la configuración.
/// </summary>
public class OpenAiCompatibleClient : IIaClient
{
    private readonly HttpClient _http;
    private readonly IaOptions _opciones;
    private readonly ILogger<OpenAiCompatibleClient> _logger;
    private readonly ModeloIaActual _modeloActual;

    public OpenAiCompatibleClient(HttpClient http, IOptions<IaOptions> opciones, ModeloIaActual modeloActual,
        ILogger<OpenAiCompatibleClient> logger)
    {
        _opciones = opciones.Value;
        _modeloActual = modeloActual;
        _logger = logger;
        _http = http;

        // Sin la barra final, HttpClient descarta el último segmento ("openai") al combinar rutas.
        var baseUrl = _opciones.BaseUrl.EndsWith('/') ? _opciones.BaseUrl : _opciones.BaseUrl + "/";
        _http.BaseAddress = new Uri(baseUrl);
        _http.Timeout = TimeSpan.FromSeconds(_opciones.TimeoutSegundos);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opciones.ApiKey);
    }

    public async Task<IaRespuesta> CompletarAsync(string instrucciones, string texto, IaEsquema? esquema = null,
        double? temperatura = null, ModeloIa? modelo = null, CancellationToken ct = default)
    {
        modelo ??= await _modeloActual.ObtenerAsync(ct);

        var pedido = new ChatRequest(
            modelo.Modelo,
            [
                new ChatMessage("system", instrucciones),
                new ChatMessage("user", texto)
            ],
            modelo.NivelRazonamiento,
            esquema is null ? null : FormatoRespuesta(esquema),
            temperatura);

        // Id corto por llamada: permite seguir en el log las líneas de una misma llamada.
        var id = Guid.NewGuid().ToString("N")[..8];
        var cuerpoPedido = JsonSerializer.Serialize(pedido);
        var url = new Uri(_http.BaseAddress!, "chat/completions");

        _logger.LogInformation(
            "IA[{Id}] → POST {Url} | modelo {Modelo} | razonamiento {Nivel} | esquema {Esquema} | temperatura {Temperatura} | timeout {Timeout}s | {Bytes} bytes",
            id, url, modelo.Modelo, pedido.ReasoningEffort ?? "default", esquema?.Nombre ?? "-",
            temperatura?.ToString() ?? "default", _opciones.TimeoutSegundos, cuerpoPedido.Length);
        _logger.LogDebug("IA[{Id}] cuerpo del pedido: {Cuerpo}", id, Recortar(cuerpoPedido));

        var reloj = Stopwatch.StartNew();
        HttpResponseMessage respuesta;
        try
        {
            // Cuerpo armado como string: se envía con Content-Length en vez de chunked.
            using var contenido = new StringContent(cuerpoPedido, Encoding.UTF8, "application/json");
            respuesta = await _http.PostAsync("chat/completions", contenido, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("IA[{Id}] ✗ TIMEOUT del HttpClient tras {Ms} ms (límite {Timeout}s). Modelo {Modelo}.",
                id, reloj.ElapsedMilliseconds, _opciones.TimeoutSegundos, modelo.Modelo);
            throw new IaException(null, $"Timeout: el proveedor no respondió en {_opciones.TimeoutSegundos} s.", ex.Message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Lo cortó quien llamó (prueba de conexión con límite propio, o el navegador cerró el pedido).
            _logger.LogWarning("IA[{Id}] ✗ cancelada por el llamador tras {Ms} ms. Modelo {Modelo}.",
                id, reloj.ElapsedMilliseconds, modelo.Modelo);
            throw;
        }
        catch (HttpRequestException ex)
        {
            // Acá caen DNS, TLS, conexión rechazada, proxy...: la causa real suele estar en la excepción interna.
            _logger.LogError(ex, "IA[{Id}] ✗ error de red tras {Ms} ms: {Causa}",
                id, reloj.ElapsedMilliseconds, ex.GetBaseException().Message);
            throw new IaException(null, "No se pudo conectar con el proveedor de IA.", ex.GetBaseException().Message);
        }

        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        reloj.Stop();
        var status = (int)respuesta.StatusCode;

        if (!respuesta.IsSuccessStatusCode)
        {
            var reintentar = respuesta.Headers.RetryAfter?.ToString();
            _logger.LogWarning("IA[{Id}] ✗ HTTP {Status} en {Ms} ms{RetryAfter}: {Cuerpo}",
                id, status, reloj.ElapsedMilliseconds,
                reintentar is null ? "" : $" (Retry-After: {reintentar})", Recortar(cuerpo));
            throw new IaException(status, $"El proveedor de IA respondió {status}.", cuerpo);
        }

        ChatResponse? datos;
        try
        {
            datos = JsonSerializer.Deserialize<ChatResponse>(cuerpo);
        }
        catch (JsonException ex)
        {
            _logger.LogError("IA[{Id}] ✗ HTTP {Status} pero el cuerpo no es JSON válido: {Cuerpo}", id, status, Recortar(cuerpo));
            throw new IaException(status, "El proveedor de IA devolvió una respuesta que no es JSON.", ex.Message);
        }

        if (datos is null)
        {
            _logger.LogError("IA[{Id}] ✗ respuesta vacía (HTTP {Status}).", id, status);
            throw new IaException(status, "Respuesta vacía del proveedor de IA.");
        }

        var textoRespuesta = datos.Choices?.FirstOrDefault()?.Message?.Content ?? "";

        _logger.LogInformation("IA[{Id}] ✓ HTTP {Status} en {Ms} ms | modelo {Modelo} | tokens entrada {In}, salida {Out} | {Chars} caracteres",
            id, status, reloj.ElapsedMilliseconds, datos.Model ?? modelo.Modelo,
            datos.Usage?.PromptTokens, datos.Usage?.CompletionTokens, textoRespuesta.Length);
        _logger.LogDebug("IA[{Id}] respuesta: {Texto}", id, Recortar(textoRespuesta));

        if (textoRespuesta.Length == 0)
            _logger.LogWarning("IA[{Id}] el proveedor respondió OK pero sin texto. Cuerpo: {Cuerpo}", id, Recortar(cuerpo));

        return new IaRespuesta(
            textoRespuesta,
            datos.Model ?? modelo.Modelo,
            datos.Usage?.PromptTokens,
            datos.Usage?.CompletionTokens,
            reloj.ElapsedMilliseconds);
    }

    /// <summary>Los cuerpos pueden ser enormes (instrucciones con todo el catálogo): al log va el principio.</summary>
    private static string Recortar(string s) => s.Length > 4000 ? s[..4000] + $"… (+{s.Length - 4000} caracteres)" : s;

    /// <summary>response_format con json_schema estricto (formato OpenAI, también lo acepta Gemini).</summary>
    private static JsonObject FormatoRespuesta(IaEsquema esquema) => new()
    {
        ["type"] = "json_schema",
        ["json_schema"] = new JsonObject
        {
            ["name"] = esquema.Nombre,
            ["strict"] = true,
            ["schema"] = esquema.Schema.DeepClone()
        }
    };

    // --- Contratos del formato OpenAI (solo lo que usamos) ---

    private record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] List<ChatMessage> Messages,
        [property: JsonPropertyName("reasoning_effort"),
                   JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReasoningEffort,
        [property: JsonPropertyName("response_format"),
                   JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonObject? ResponseFormat,
        [property: JsonPropertyName("temperature"),
                   JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Temperature);

    private record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string? Content);

    private record ChatResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("choices")] List<ChatChoice>? Choices,
        [property: JsonPropertyName("usage")] ChatUsage? Usage);

    private record ChatChoice(
        [property: JsonPropertyName("message")] ChatMessage? Message);

    private record ChatUsage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens);
}
