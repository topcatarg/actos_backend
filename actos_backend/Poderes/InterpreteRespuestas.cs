using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using actos_backend.Bloques;
using actos_backend.Datos;
using actos_backend.Ia;

namespace actos_backend.Poderes;

public static class TipoAccion
{
    public const string Confirmar = "confirmar"; // acepta el valor propuesto
    public const string Rechazar = "rechazar";   // no es correcto y no da otro (identidad: es persona nueva)
    public const string Valor = "valor";         // da un valor nuevo o corregido
    public const string Agregar = "agregar";     // solo facultades
    public const string Quitar = "quitar";       // solo facultades
}

/// <summary>Lo que la IA entendió de la respuesta. C# lo valida antes de aplicarlo.</summary>
public class AccionRespuesta
{
    public string Campo { get; set; } = "";
    public string Accion { get; set; } = "";
    public string? Valor { get; set; }
    public string? Fragmento { get; set; }
}

/// <summary>
/// Respuesta del usuario → lista de acciones sobre campos del poder, interpretada en el contexto
/// de lo que se le preguntó. La IA no modifica el poder: solo propone acciones.
/// </summary>
public class InterpreteRespuestas(IIaClient ia, ILogger<InterpreteRespuestas> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string Instrucciones = """
        Interpretás la respuesta que da el personal de una escribanía a una pregunta sobre un poder notarial.
        Devolvés acciones sobre campos del poder.

        ACCIONES:
        - confirmar: acepta el valor propuesto para ese campo.
        - rechazar: dice que el valor propuesto no es correcto y NO da otro. Para "identidad", rechazar
          significa que es una persona nueva, distinta de la de nuestros registros.
        - valor: da un valor nuevo o corregido. "valor" va normalizado y "fragmento" es la copia TEXTUAL
          del pedazo de la RESPUESTA que lo dice.
        - agregar / quitar: solo para el campo "facultades", con el código de la facultad en "valor".

        REGLAS:
        - Usá solo campos de la lista CAMPOS.
        - Si la respuesta es una afirmación general (sí, ok, dale, correcto, está bien, confirmo), devolvé
          "confirmar" para cada campo de la PREGUNTA.
        - Si corrige ("no, al revés", "no, es soltera"), usá "valor" con el valor correcto, no "rechazar".
        - Si aporta datos que no se le preguntaron, devolvelos también como "valor".
        - Nombres: si se pidió el apellido, devolvé partes[i].apellido con SOLO el apellido (ej. "Pérez");
          si corrige los nombres de pila, partes[i].nombres. El nombre completo lo arma el sistema.
          Usá partes[i].nombreCompleto solo si la respuesta da el nombre entero de nuevo.
        - Identidad: "confirmar" si es la persona de nuestros registros (si hay varias, poné en "valor" el DNI de la elegida).
        - Para una persona que todavía no está en el poder, usá el índice libre que indica CAMPOS.
        - Normalizá: DNI y CUIL solo dígitos; fechas AAAA-MM-DD; género M o F; rol otorgante o apoderado;
          facultades y condiciones con los códigos de las listas.
        - No inventes: si la respuesta no dice nada de un campo, no devuelvas acción para ese campo.
        """;

    public async Task<(List<AccionRespuesta> Acciones, IaRespuesta Ia)> InterpretarAsync(
        PoderExtraido poder, List<Pendiente> preguntado, string respuesta,
        IReadOnlyList<Clausula> clausulas, CancellationToken ct = default)
    {
        var contexto = Contexto(poder, preguntado, respuesta, clausulas);
        var r = await ia.CompletarAsync(Instrucciones, contexto, Esquema(), temperatura: 0, ct: ct);

        try
        {
            var raiz = JsonSerializer.Deserialize<Raiz>(r.Texto, Json) ?? throw new JsonException("Respuesta vacía.");
            return (raiz.Acciones ?? [], r);
        }
        catch (JsonException ex)
        {
            logger.LogWarning("La IA devolvió JSON inválido: {Texto}", r.Texto);
            throw new IaException(null, "La IA no devolvió un JSON con la forma esperada.", ex.Message);
        }
    }

    private static string Contexto(PoderExtraido poder, List<Pendiente> preguntado, string respuesta, IReadOnlyList<Clausula> clausulas)
    {
        var nombres = clausulas.ToDictionary(c => c.Codigo, c => c.Nombre);
        var sb = new StringBuilder();

        sb.AppendLine("PREGUNTA:");
        var estadoPreguntado = new EstadoPoder(preguntado, preguntado, false, false);
        sb.Append(RedactorPreguntas.Guion(poder, estadoPreguntado, nombres, primerMensaje: false));
        sb.AppendLine("Campos involucrados: " + string.Join(", ", preguntado.Select(p => p.Campo).Distinct()));
        sb.AppendLine();

        sb.AppendLine("CAMPOS:");
        for (var i = 0; i < poder.Partes.Count; i++)
        {
            var p = poder.Partes[i];
            sb.AppendLine($"- partes[{i}] = {p.NombreCompleto.Valor ?? "sin nombre"} ({p.Rol.Valor ?? "sin rol"}): " +
                          $"partes[{i}].rol, .nombreCompleto, .nombres, .apellido, .dni, .cuil, .genero, .estadoCivil, .nacionalidad, " +
                          $".fechaNacimiento, .domicilio, .padre, .madre" +
                          (p.Candidatos.Count > 0 ? $", .identidad (candidatos: {string.Join("; ", p.Candidatos.Select(c => $"{c.NombreCompleto} DNI {c.Dni}"))})" : ""));
        }
        sb.AppendLine($"- partes[{poder.Partes.Count}] = índice libre para agregar una persona nueva (mismos campos)");
        sb.AppendLine("- tipoPoder (general, especial, bancario, judicial o administrativo), facultades, sustitucion, vigencia, fechaVigencia, revocabilidad");
        sb.Append(EsquemaBloques.CamposParaRespuestas(poder));
        sb.AppendLine("- generar: confirmar si quiere generar el documento ahora; rechazar si todavía no");
        sb.AppendLine();

        sb.AppendLine("FACULTADES (código: nombre): " +
                      string.Join("; ", clausulas.Where(c => c.Tipo == "facultad").Select(c => $"{c.Codigo}: {c.Nombre}")));
        foreach (var cat in new[] { "sustitucion", "vigencia", "revocabilidad" })
            sb.AppendLine($"{cat.ToUpperInvariant()} (código: nombre): " +
                          string.Join("; ", clausulas.Where(c => c.Tipo == "condicion" && c.Categoria == cat).Select(c => $"{c.Codigo}: {c.Nombre}")));
        sb.AppendLine();

        sb.AppendLine("RESPUESTA:");
        sb.AppendLine(respuesta);
        return sb.ToString();
    }

    private static IaEsquema Esquema()
    {
        static JsonObject Texto(string? descripcion = null, bool nulo = false)
        {
            var o = new JsonObject { ["type"] = nulo ? new JsonArray("string", "null") : "string" };
            if (descripcion is not null) o["description"] = descripcion;
            return o;
        }

        var accion = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["campo"] = Texto("Uno de la lista CAMPOS, ej. partes[0].rol"),
                ["accion"] = Texto("confirmar, rechazar, valor, agregar o quitar"),
                ["valor"] = Texto(nulo: true),
                ["fragmento"] = Texto(nulo: true)
            },
            ["required"] = new JsonArray("campo", "accion", "valor", "fragmento"),
            ["additionalProperties"] = false
        };

        return new IaEsquema("acciones_respuesta", new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["acciones"] = new JsonObject { ["type"] = "array", ["items"] = accion } },
            ["required"] = new JsonArray("acciones"),
            ["additionalProperties"] = false
        });
    }

    private class Raiz
    {
        public List<AccionRespuesta>? Acciones { get; set; }
    }
}
