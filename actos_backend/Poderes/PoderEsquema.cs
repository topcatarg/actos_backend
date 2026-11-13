using System.Text.Json.Nodes;
using actos_backend.Bloques;
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
    /// <param name="bloques">Bloques de datos que puede llevar el poder (salen de actos_partes).</param>
    public static IaEsquema Crear(IReadOnlyList<Bloque> bloques) => new("poder_extraido", Objeto(
        ("tipoPoder", Dato("Tipo de poder: general, especial, bancario, judicial o administrativo")),
        ("partes", Lista(Objeto(
            ("rol", Dato("otorgante (quien da el poder) o apoderado (quien lo recibe). El valor se completa siempre que se entienda por el contexto; el fragmento puede ser null")),
            ("nombreCompleto", Dato("Nombres y apellido completos, tal como aparecen")),
            ("nombres", Dato("Solo los nombres de pila; null si no está claro cuáles son")),
            ("apellido", Dato("Solo el apellido; null si no está claro cuál es (ej. 'Juan Carlos' sin más)")),
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
        ("objetos", EsquemaBloques.EsquemaObjetos(bloques)),
        ("bloques", EsquemaBloques.Esquema(bloques))));

    private static JsonObject Dato(string descripcion) => EsquemaJson.Dato(descripcion);
    private static JsonObject Lista(JsonObject items) => EsquemaJson.Lista(items);
    private static JsonObject Objeto(params (string Nombre, JsonObject Schema)[] propiedades) => EsquemaJson.Objeto(propiedades);
}
