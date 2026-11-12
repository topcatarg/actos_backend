using System.Text.Json.Nodes;
using actos_backend.Ia;

namespace actos_backend.Poderes;

/// <summary>
/// JSON schema que se le exige al modelo en la extracción. Escrito a mano para controlarlo
/// del todo y que sirva tal cual con OpenAI en modo estricto: todas las propiedades
/// requeridas, additionalProperties false y "no lo sé" expresado como valor null.
/// Los códigos válidos no van como enum en el schema: van en las instrucciones y los
/// valida C# contra el catálogo (más portable entre proveedores).
/// </summary>
public static class PoderEsquema
{
    public static IaEsquema Crear() => new("poder_extraido", Objeto(
        ("tipoPoder", Dato("Tipo de poder: general o especial")),
        ("partes", Lista(Objeto(
            ("rol", Dato("otorgante (quien da el poder) o apoderado (quien lo recibe). El valor se completa siempre que se entienda por el contexto; el fragmento puede ser null")),
            ("nombreCompleto", Dato("Nombre y apellido")),
            ("dni", Dato("Solo dígitos")),
            ("cuil", Dato("Solo dígitos")),
            ("estadoCivil", Dato("soltero/a, casado/a, divorciado/a, viudo/a")),
            ("nacionalidad", Dato("Nacionalidad")),
            ("fechaNacimiento", Dato("AAAA-MM-DD")),
            ("domicilio", Dato("Domicilio completo")),
            ("padre", Dato("Nombre del padre")),
            ("madre", Dato("Nombre de la madre"))))),
        ("facultades", Lista(Dato("Código de facultad del catálogo"))),
        ("sustitucion", Dato("Código de condición de sustitución")),
        ("vigencia", Dato("Código de condición de vigencia")),
        ("fechaVigencia", Dato("AAAA-MM-DD, solo si la vigencia es hasta una fecha")),
        ("revocabilidad", Dato("Código de condición de revocabilidad")),
        ("inmueble", Objeto(
            ("direccion", Dato("Calle y número del inmueble")),
            ("localidad", Dato("Ciudad o localidad")),
            ("provincia", Dato("Provincia")),
            ("matricula", Dato("Matrícula")),
            ("nomenclatura", Dato("Nomenclatura catastral")),
            ("tipo", Dato("departamento, casa, lote, local..."))))));

    private static JsonObject Dato(string descripcion) => new()
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

    private static JsonObject Lista(JsonObject items) => new()
    {
        ["type"] = "array",
        ["items"] = items
    };

    private static JsonObject Objeto(params (string Nombre, JsonObject Schema)[] propiedades)
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
