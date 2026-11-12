using System.Text.Json.Nodes;

namespace actos_backend.Ia;

/// <summary>
/// Puerta única hacia el modelo de lenguaje. El resto del backend depende de esta
/// interfaz y no del proveedor concreto.
/// </summary>
public interface IIaClient
{
    /// <param name="instrucciones">Mensaje de sistema: qué tiene que hacer el modelo.</param>
    /// <param name="texto">Mensaje de usuario: el texto a procesar.</param>
    /// <param name="esquema">Si viene, el modelo queda obligado a responder JSON con esa forma.</param>
    Task<IaRespuesta> CompletarAsync(string instrucciones, string texto, IaEsquema? esquema = null, CancellationToken ct = default);
}

/// <summary>JSON schema que se le exige a la respuesta (salida estructurada).</summary>
public record IaEsquema(string Nombre, JsonObject Schema);

public record IaRespuesta(
    string Texto,
    string Modelo,
    int? TokensEntrada,
    int? TokensSalida,
    long DuracionMs);

/// <summary>Error devuelto por el proveedor (key inválida, 429 por cuota, modelo inexistente, etc.).</summary>
public class IaException(int? statusCode, string mensaje, string? detalle = null) : Exception(mensaje)
{
    public int? StatusCode { get; } = statusCode;
    public string? Detalle { get; } = detalle;
}
