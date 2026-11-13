using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using actos_backend.Datos;
using actos_backend.Ia;

namespace actos_backend.Asistente;

/// <summary>Qué quiere hacer el usuario con su primer mensaje.</summary>
public enum TipoIntencion
{
    /// <summary>Eligió un acto o consulta del catálogo (Codigo trae cuál).</summary>
    Opcion,
    /// <summary>Pregunta qué se puede hacer.</summary>
    Ayuda,
    /// <summary>Pide algo notarial que no está en el catálogo (ej. una hipoteca).</summary>
    FueraDeCatalogo,
    /// <summary>No se entiende o es ambiguo: hay que preguntar.</summary>
    Dudosa
}

/// <param name="Alternativas">Códigos posibles cuando hay duda (solo códigos del catálogo, máximo 3).</param>
public record Intencion(TipoIntencion Tipo, string? Codigo, IReadOnlyList<string> Alternativas);

/// <summary>
/// Texto libre → a qué opción del catálogo (tabla actos) va. La IA solo puede elegir códigos
/// de la lista que le pasamos; C# valida el resultado y ante cualquier duda pide aclaración.
/// Lo obvio (pedir ayuda) se resuelve sin IA.
/// </summary>
public class ClasificadorIntencion(IIaClient ia, ILogger<ClasificadorIntencion> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Regex PideAyuda = new(
        @"^\s*(\?+|ayuda|help|men[uú]|opciones|qu[eé] (pod[eé]s|puedo|se puede) hacer\??)\s*[.!?]*\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<Intencion> ClasificarAsync(string texto, IReadOnlyList<OpcionActo> opciones, CancellationToken ct = default)
    {
        if (PideAyuda.IsMatch(texto))
            return new Intencion(TipoIntencion.Ayuda, null, []);

        var respuesta = await ia.CompletarAsync(Instrucciones(opciones), texto, Esquema(), temperatura: 0, ct: ct);

        RespuestaIa? r;
        try
        {
            r = JsonSerializer.Deserialize<RespuestaIa>(respuesta.Texto, Json);
        }
        catch (JsonException)
        {
            logger.LogWarning("Clasificador: la IA devolvió JSON inválido: {Texto}", respuesta.Texto);
            r = null;
        }

        return Validar(r, opciones);
    }

    /// <summary>Todo lo que no cierra (código inventado, confianza baja) se degrada a "Dudosa".</summary>
    private static Intencion Validar(RespuestaIa? r, IReadOnlyList<OpcionActo> opciones)
    {
        var codigos = opciones.Select(o => o.Codigo).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var alternativas = (r?.Alternativas ?? [])
            .Where(codigos.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (r is null) return new Intencion(TipoIntencion.Dudosa, null, alternativas);

        switch (r.Intencion)
        {
            case "ayuda":
                return new Intencion(TipoIntencion.Ayuda, null, []);

            case "fuera_de_catalogo":
                return new Intencion(TipoIntencion.FueraDeCatalogo, null, alternativas);

            case "opcion" when r.Codigo is not null && codigos.Contains(r.Codigo) && r.Confianza == "alta":
                return new Intencion(TipoIntencion.Opcion, r.Codigo.ToLowerInvariant(), []);

            case "opcion" when r.Codigo is not null && codigos.Contains(r.Codigo):
                // Confianza media/baja: se lo ofrecemos como primera alternativa en vez de asumirlo.
                alternativas.Remove(r.Codigo);
                alternativas.Insert(0, r.Codigo.ToLowerInvariant());
                return new Intencion(TipoIntencion.Dudosa, null, alternativas.Take(3).ToList());

            default:
                return new Intencion(TipoIntencion.Dudosa, null, alternativas);
        }
    }

    private static string Instrucciones(IReadOnlyList<OpcionActo> opciones)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
            Sos el recepcionista de un sistema de una escribanía argentina. Leés el primer mensaje del usuario
            (español rioplatense) y decidís qué quiere hacer. NO respondés al usuario: solo clasificás.

            Respondé:
            - intencion = "opcion" si el pedido corresponde a una de las opciones de la lista. En "codigo" va su código.
            - intencion = "ayuda" si pregunta qué se puede hacer, cómo funciona o pide el menú.
            - intencion = "fuera_de_catalogo" si pide un trámite notarial que NO está en la lista (ej. hipoteca, testamento).
            - intencion = "dudoso" si no se entiende, es un saludo, o podría ser más de una opción.
            - "codigo": SOLO un código de la lista, o null. Nunca inventes códigos.
            - "confianza": "alta" solo si el pedido es claro; "media" o "baja" si estás infiriendo.
            - "alternativas": hasta 3 códigos de la lista que podrían corresponder (vacío si no hay ninguno).
            - Buscar, traer, mostrar o consultar algo que ya existe es una consulta, no un acto nuevo.

            OPCIONES (código: nombre — descripción. Ejemplo de pedido):
            """);

        foreach (var o in opciones)
            sb.AppendLine($"- {o.Codigo}: {o.Nombre} — {o.Descripcion}. Ejemplo: \"{o.Ejemplo}\"");

        return sb.ToString();
    }

    /// <summary>Mismo criterio que PoderEsquema: todo requerido, "no sé" como null, sin enums (los valores los valida C#).</summary>
    private static IaEsquema Esquema() => new("intencion_usuario", new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["intencion"] = new JsonObject { ["type"] = "string", ["description"] = "opcion | ayuda | fuera_de_catalogo | dudoso" },
            ["codigo"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["confianza"] = new JsonObject { ["type"] = "string", ["description"] = "alta | media | baja" },
            ["alternativas"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" }
            }
        },
        ["required"] = new JsonArray("intencion", "codigo", "confianza", "alternativas"),
        ["additionalProperties"] = false
    });

    private class RespuestaIa
    {
        public string Intencion { get; init; } = "";
        public string? Codigo { get; init; }
        public string Confianza { get; init; } = "";
        public List<string>? Alternativas { get; init; }
    }
}
