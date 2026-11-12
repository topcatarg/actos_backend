using System.Text.Json.Serialization;

namespace actos_backend.Poderes;

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
    public const string Base = "base";        // vino del maestro de personas
    public const string Default = "default";  // lo puso el sistema por regla
}

public class PoderExtraido
{
    public Dato TipoPoder { get; set; } = new();          // general | especial
    public List<ParteExtraida> Partes { get; set; } = [];
    public List<Dato> Facultades { get; set; } = [];      // códigos de clausulas_template
    public Dato Sustitucion { get; set; } = new();        // código de condición
    public Dato Vigencia { get; set; } = new();           // código de condición
    public Dato FechaVigencia { get; set; } = new();      // solo si vigencia es hasta una fecha
    public Dato Revocabilidad { get; set; } = new();      // código de condición
    public InmuebleExtraido Inmueble { get; set; } = new();
}

public class ParteExtraida
{
    public Dato Rol { get; set; } = new();                // otorgante | apoderado
    public Dato NombreCompleto { get; set; } = new();
    public Dato Dni { get; set; } = new();
    public Dato Cuil { get; set; } = new();
    public Dato EstadoCivil { get; set; } = new();
    public Dato Nacionalidad { get; set; } = new();
    public Dato FechaNacimiento { get; set; } = new();
    public Dato Domicilio { get; set; } = new();
    public Dato Padre { get; set; } = new();
    public Dato Madre { get; set; } = new();

    // --- Estos no los extrae la IA: los completa C# ---

    /// <summary>Solo desde la base (no se le pide a la IA que lo deduzca del nombre).</summary>
    public Dato Genero { get; set; } = new();

    /// <summary>Id en pf_maestro si se identificó a la persona por DNI.</summary>
    public int? PfId { get; set; }

    /// <summary>Posibles coincidencias por nombre, para que el usuario confirme. No completan datos.</summary>
    public List<Datos.CandidatoPersona> Candidatos { get; set; } = [];
}

public class InmuebleExtraido
{
    public Dato Direccion { get; set; } = new();
    public Dato Localidad { get; set; } = new();
    public Dato Provincia { get; set; } = new();
    public Dato Matricula { get; set; } = new();
    public Dato Nomenclatura { get; set; } = new();
    public Dato Tipo { get; set; } = new();               // departamento, casa, lote...
}

/// <summary>Dato que el modelo devolvió pero C# rechazó.</summary>
public record Descarte(string Campo, string? Valor, string? Fragmento, string Motivo);

/// <summary>Algo que el usuario debería revisar, ej. un dato dicho distinto al registrado.</summary>
public record Aviso(string Campo, string Mensaje);

public record ResultadoProceso(
    PoderExtraido Poder,
    EstadoPoder Estado,
    List<Descarte> Descartes,
    List<Aviso> Avisos,
    string Modelo,
    int? TokensEntrada,
    int? TokensSalida,
    long DuracionMs);

public record ResultadoExtraccion(
    PoderExtraido Poder,
    List<Descarte> Descartes,
    string Modelo,
    int? TokensEntrada,
    int? TokensSalida,
    long DuracionMs);
