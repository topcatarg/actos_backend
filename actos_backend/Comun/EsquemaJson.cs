using System.Text.Json.Nodes;

namespace actos_backend.Comun;

/// <summary>
/// Piezas de JSON schema para la salida estructurada de la IA (modo estricto: todo requerido,
/// additionalProperties false, "no lo sé" como null).
/// </summary>
public static class EsquemaJson
{
    /// <summary>Un Dato: { valor, fragmento }.</summary>
    public static JsonObject Dato(string descripcion) => new()
    {
        ["type"] = "object",
        ["description"] = descripcion,
        ["properties"] = new JsonObject
        {
            ["valor"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["fragmento"] = new JsonObject { ["type"] = new JsonArray("string", "null") }
        },
        ["required"] = new JsonArray("valor", "fragmento"),
        ["additionalProperties"] = false
    };

    public static JsonObject Lista(JsonObject items) => new()
    {
        ["type"] = "array",
        ["items"] = items
    };

    public static JsonObject Objeto(params (string Nombre, JsonObject Schema)[] propiedades)
    {
        var props = new JsonObject();
        var requeridas = new JsonArray();
        foreach (var (nombre, schema) in propiedades)
        {
            props[nombre] = schema;
            requeridas.Add(nombre);
        }
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = requeridas,
            ["additionalProperties"] = false
        };
    }
}
