using System.ComponentModel.DataAnnotations;

namespace actos_backend.Ia;

/// <summary>
/// Configuración del proveedor de IA. Cualquier proveedor compatible con la API de
/// OpenAI (Gemini, OpenAI, Groq...) se cambia solo con estos valores.
/// </summary>
public class IaOptions
{
    public const string Seccion = "Ia";

    /// <summary>Base URL del endpoint compatible con OpenAI. Ej. Gemini: https://generativelanguage.googleapis.com/v1beta/openai/</summary>
    [Required]
    public string BaseUrl { get; set; } = "";

    /// <summary>Id del modelo. Ej: gemini-3.8-flash</summary>
    [Required]
    public string Modelo { get; set; } = "";

    /// <summary>API key. Va en user secrets, nunca en appsettings.json.</summary>
    [Required(ErrorMessage = "Falta Ia:ApiKey. Cargala con: dotnet user-secrets set \"Ia:ApiKey\" \"<tu-key>\"")]
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Cuánto "piensa" el modelo antes de responder (reasoning_effort de OpenAI): minimal, low, medium o high.
    /// Más bajo = más rápido. Vacío = el default del modelo, que en Gemini puede tardar bastante.
    /// </summary>
    public string? NivelRazonamiento { get; set; }

    /// <summary>Tiempo máximo de toda la llamada (conectar + que el modelo responda).</summary>
    [Range(5, 300)]
    public int TimeoutSegundos { get; set; } = 90;

    /// <summary>
    /// Tiempo máximo para abrir la conexión TCP con cada dirección del proveedor. Si una no conecta
    /// (ej. IPv6 roto), se prueba la siguiente en vez de esperar todo TimeoutSegundos.
    /// </summary>
    [Range(1, 60)]
    public int TimeoutConexionSegundos { get; set; } = 5;
}
