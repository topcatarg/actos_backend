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

    public OpenAiCompatibleClient(HttpClient http, IOptions<IaOptions> opciones, ILogger<OpenAiCompatibleClient> logger)
    {
        _opciones = opciones.Value;
        _logger = logger;
        _http = http;

        // Sin la barra final, HttpClient descarta el último segmento ("openai") al combinar rutas.
        var baseUrl = _opciones.BaseUrl.EndsWith('/') ? _opciones.BaseUrl : _opciones.BaseUrl + "/";
        _http.BaseAddress = new Uri(baseUrl);
        _http.Timeout = TimeSpan.FromSeconds(_opciones.TimeoutSegundos);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opciones.ApiKey);
    }

    public async Task<IaRespuesta> CompletarAsync(string instrucciones, string texto, IaEsquema? esquema = null,
        double? temperatura = null, CancellationToken ct = default)
    {
        var pedido = new ChatRequest(
            _opciones.Modelo,
            [
                new ChatMessage("system", instrucciones),
                new ChatMessage("user", texto)
            ],
            string.IsNullOrWhiteSpace(_opciones.NivelRazonamiento) ? null : _opciones.NivelRazonamiento,
            esquema is null ? null : FormatoRespuesta(esquema),
            temperatura);

        _logger.LogInformation("Llamando a IA {Modelo} (razonamiento: {Nivel})...",
            _opciones.Modelo, pedido.ReasoningEffort ?? "default");

        var reloj = Stopwatch.StartNew();
        HttpResponseMessage respuesta;
        try
        {
            // Cuerpo armado como string: se envía con Content-Length en vez de chunked.
            using var contenido = new StringContent(JsonSerializer.Serialize(pedido), Encoding.UTF8, "application/json");
            respuesta = await _http.PostAsync("chat/completions", contenido, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new IaException(null, $"Timeout: el proveedor no respondió en {_opciones.TimeoutSegundos} s.", ex.Message);
        }
        catch (HttpRequestException ex)
        {
            throw new IaException(null, "No se pudo conectar con el proveedor de IA.", ex.Message);
        }
        reloj.Stop();

        if (!respuesta.IsSuccessStatusCode)
        {
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("IA respondió {Status} en {Ms} ms: {Cuerpo}", (int)respuesta.StatusCode, reloj.ElapsedMilliseconds, cuerpo);
            throw new IaException((int)respuesta.StatusCode, $"El proveedor de IA respondió {(int)respuesta.StatusCode}.", cuerpo);
        }

        var datos = await respuesta.Content.ReadFromJsonAsync<ChatResponse>(ct)
                    ?? throw new IaException(null, "Respuesta vacía del proveedor de IA.");

        var textoRespuesta = datos.Choices?.FirstOrDefault()?.Message?.Content ?? "";

        _logger.LogInformation("IA {Modelo}: {Ms} ms, tokens entrada {In}, salida {Out}",
            datos.Model ?? _opciones.Modelo, reloj.ElapsedMilliseconds, datos.Usage?.PromptTokens, datos.Usage?.CompletionTokens);

        return new IaRespuesta(
            textoRespuesta,
            datos.Model ?? _opciones.Modelo,
            datos.Usage?.PromptTokens,
            datos.Usage?.CompletionTokens,
            reloj.ElapsedMilliseconds);
    }

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
