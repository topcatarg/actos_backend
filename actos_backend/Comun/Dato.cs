using System.Text.Json.Serialization;

namespace actos_backend.Comun;

/// <summary>
/// Un dato sacado del texto. El modelo completa Valor y Fragmento; Inicio lo calcula C#
/// buscando el fragmento en el texto original. Si el fragmento no aparece, el dato se descarta.
/// </summary>
public class Dato
{
    /// <summary>Valor normalizado (DNI solo dígitos, fechas AAAA-MM-DD, códigos del catálogo). Null si el texto no lo dice.</summary>
    public string? Valor { get; set; }

    /// <summary>Copia textual del pedazo del texto de donde salió el valor.</summary>
    public string? Fragmento { get; set; }

    /// <summary>Posición del fragmento en el texto original (0-based). La completa C#.</summary>
    public int? Inicio { get; set; }

    /// <summary>De dónde salió el valor: texto, base o default (ver <see cref="OrigenDato"/>).</summary>
    public string? Origen { get; set; }

    [JsonIgnore]
    public bool TieneValor => !string.IsNullOrWhiteSpace(Valor);
}

public static class OrigenDato
{
    public const string Texto = "texto";      // lo dijo el usuario (con fragmento que lo respalda)
    public const string Inferido = "inferido"; // la IA lo interpretó del contexto, sin frase literal: confirmar
    public const string Respuesta = "respuesta";   // lo dio el usuario al contestar una pregunta
    public const string Confirmado = "confirmado"; // el usuario confirmó el valor que había (inferido, default o distinto a la base)
    public const string Base = "base";        // vino del maestro de personas
    public const string Default = "default";  // lo puso el sistema por regla
}
