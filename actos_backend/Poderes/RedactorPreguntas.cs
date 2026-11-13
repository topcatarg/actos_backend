using System.Text;
using actos_backend.Datos;
using actos_backend.Ia;

namespace actos_backend.Poderes;

/// <param name="Mensaje">Lo que se le muestra al usuario.</param>
/// <param name="Guion">Lo que C# le pidió a la IA que dijera (para depurar).</param>
public record MensajeRedactado(string Mensaje, string Guion, int? TokensEntrada, int? TokensSalida, long DuracionMs);

/// <summary>
/// Convierte el grupo "siguiente" del motor de faltantes en un mensaje natural.
/// C# arma un guion con QUÉ decir (en lenguaje humano, sin códigos); la IA solo decide CÓMO decirlo.
/// </summary>
public class RedactorPreguntas(IIaClient ia, ICatalogoRepositorio catalogo)
{
    // Más alta que en la extracción: acá queremos un texto natural, no una respuesta exacta.
    private const double Temperatura = 0.7;

    private const string Instrucciones = """
        Sos el asistente de una escribanía argentina. Le escribís al personal de la escribanía
        (escribanos y empleados) mientras arman un poder notarial por chat.
        Escribís en español rioplatense, con voseo.

        Te paso un GUION con lo que tenés que decir en este mensaje. Tu tarea es redactarlo de forma natural.

        TONO:
        - Directo y profesional, entre colegas. Sin rodeos ni cortesías de más.
        - Usá la terminología notarial tal cual (otorgante, apoderado, facultades, sustitución,
          vigencia, revocabilidad) sin explicarla: el lector la conoce.

        REGLAS:
        - Decí todo lo que pide el guion y nada más. No agregues preguntas, datos ni sugerencias propias.
        - No inventes ni cambies ningún dato: nombres, números y fechas van exactamente como en el guion.
        - Si el guion pide varias cosas, podés usar una lista corta. Si es una sola, una o dos oraciones.
        - No menciones "sistema", "guion", "base de datos" ni códigos internos. Para datos registrados decí "en nuestros registros".
        - Si el guion dice que es el primer mensaje, empezá con un saludo breve SIN preguntas (por ejemplo "¡Hola!").
          Nunca preguntes cómo está la persona. Si no es el primer mensaje, andá directo al punto.
        - Respondé solo con el mensaje.

        EJEMPLOS DE TONO:
        Guion: Confirmar que Ana Ruiz es la otorgante. Confirmar que Pablo Díaz es el apoderado.
        Mensaje: ¿Confirmás que Ana Ruiz es la otorgante y Pablo Díaz el apoderado?

        Guion: Pedir de Pablo Díaz: DNI, estado civil y domicilio.
        Mensaje: Faltan estos datos de Pablo Díaz:
        - DNI
        - Estado civil
        - Domicilio

        Guion: Confirmar la condición: Sin facultad de sustituir. Confirmar la condición: Vigencia indefinida.
        Mensaje: Quedó sin facultad de sustituir y con vigencia indefinida. ¿Lo dejamos así?
        """;

    public async Task<MensajeRedactado> RedactarAsync(PoderExtraido poder, EstadoPoder estado, bool primerMensaje, CancellationToken ct = default)
    {
        var clausulas = await catalogo.ObtenerClausulasActivasAsync(ct);
        var nombres = clausulas.ToDictionary(c => c.Codigo, c => c.Nombre);

        var guion = Guion(poder, estado, nombres, primerMensaje);
        var r = await ia.CompletarAsync(Instrucciones, guion, temperatura: Temperatura, ct: ct);

        return new MensajeRedactado(r.Texto.Trim(), guion, r.TokensEntrada, r.TokensSalida, r.DuracionMs);
    }

    /// <summary>El "qué decir", en lenguaje humano. Es determinista: lo arma C#, no la IA.</summary>
    public static string Guion(PoderExtraido poder, EstadoPoder estado, IReadOnlyDictionary<string, string> nombres, bool primerMensaje)
    {
        var sb = new StringBuilder();
        if (primerMensaje)
            sb.AppendLine("Es el primer mensaje de la conversación.");

        var tipo = poder.TipoPoder.Valor is { } t ? $"poder {t}" : "poder";
        sb.AppendLine($"Contexto: se está armando un {tipo}.");

        if (estado.GenerarDocumento)
        {
            sb.AppendLine("El usuario confirmó. Avisar que se genera el documento del poder.");
            return sb.ToString();
        }

        if (estado.ListoParaGenerar)
        {
            sb.AppendLine("Ya están todos los datos. Avisar que el poder está completo y preguntar si generamos el documento.");
            return sb.ToString();
        }

        string Nombre(string codigo) => nombres.TryGetValue(codigo, out var n) ? n : codigo;

        foreach (var p in estado.Siguiente)
        {
            var parte = ParteDe(poder, p.Campo);
            var quien = parte?.NombreCompleto.Valor ?? "la persona";

            var linea = (p.Tipo, Campo(p.Campo)) switch
            {
                (TipoPendiente.Confirmar, "rol") =>
                    $"Confirmar que {quien} es {Rol(p.Detalle, parte?.Genero.Valor)}.",

                (TipoPendiente.Falta, "rol") =>
                    $"Preguntar si {quien} es otorgante o apoderado.",

                (TipoPendiente.Confirmar, "identidad") =>
                    $"En nuestros registros hay coincidencias para {quien}: " +
                    string.Join("; ", parte!.Candidatos.Select(c => $"{c.NombreCompleto}, DNI {FormatearDni(c.Dni)}")) +
                    ". Preguntar si es esa persona o si es otra.",

                (TipoPendiente.Confirmar, "facultades") =>
                    "Confirmar que el poder incluya estas facultades: " +
                    string.Join(", ", poder.Facultades.Select(f => Nombre(f.Valor!))) + ".",

                (TipoPendiente.Confirmar, "sustitucion" or "vigencia" or "revocabilidad") =>
                    $"Confirmar la condición: {Nombre(p.Detalle!)}.",

                (TipoPendiente.Confirmar, _) =>
                    $"Confirmar: {p.Detalle}",

                (TipoPendiente.Inconsistente, _) =>
                    $"Señalar el problema y pedir el dato correcto. {p.Etiqueta}: {p.Detalle}",

                (TipoPendiente.Falta, "otorgante") =>
                    "Todavía no se indicó quién otorga el poder. Pedir nombre completo y DNI del otorgante.",

                (TipoPendiente.Falta, "apoderado") =>
                    "Todavía no se indicó a favor de quién se otorga el poder. Pedir nombre completo y DNI del apoderado.",

                (TipoPendiente.Falta, "apellido") =>
                    $"Pedir el apellido de {quien}, por separado (de «{quien}» no está claro cuál es el apellido).",

                (TipoPendiente.Falta, "genero") =>
                    $"Pedir el género de {quien} tal como figura en su DNI (masculino o femenino).",

                (TipoPendiente.Falta, _) =>
                    $"Pedir: {p.Etiqueta}" + (p.Detalle is null ? "." : $" ({p.Detalle})."),

                _ => p.Etiqueta
            };
            sb.AppendLine("- " + linea);
        }

        return sb.ToString();
    }

    private static ParteExtraida? ParteDe(PoderExtraido poder, string campo)
    {
        // "partes[3].dni" → 3
        if (!campo.StartsWith("partes[")) return null;
        var fin = campo.IndexOf(']');
        return int.TryParse(campo[7..fin], out var i) && i < poder.Partes.Count ? poder.Partes[i] : null;
    }

    /// <summary>"el otorgante", "la apoderada"... Sin género conocido, la forma doble ("apoderado/a")
    /// y la redacción elige; el género real se pregunta aparte para el documento.</summary>
    private static string Rol(string? rol, string? genero) => (rol, genero) switch
    {
        ("otorgante", "F") => "la otorgante",
        ("otorgante", "M") => "el otorgante",
        ("otorgante", _) => "otorgante",
        ("apoderado", "F") => "la apoderada",
        ("apoderado", "M") => "el apoderado",
        _ => "apoderado/a"
    };

    private static string Campo(string campo) => campo[(campo.LastIndexOf('.') + 1)..];

    private static string FormatearDni(string? dni) =>
        dni is { Length: 8 } ? $"{dni[..2]}.{dni[2..5]}.{dni[5..]}" :
        dni is { Length: 7 } ? $"{dni[..1]}.{dni[1..4]}.{dni[4..]}" :
        dni ?? "sin DNI";
}
