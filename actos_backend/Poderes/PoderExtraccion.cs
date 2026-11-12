namespace actos_backend.Poderes;

using System.Text.Json.Serialization;
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

    [JsonIgnore]
    public bool TieneValor => !string.IsNullOrWhiteSpace(Valor);
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
}

public class InmuebleExtraido
{
    public Dato Direccion { get; set; } = new();
    public Dato Provincia { get; set; } = new();
    public Dato Matricula { get; set; } = new();
    public Dato Nomenclatura { get; set; } = new();
    public Dato Tipo { get; set; } = new();               // departamento, casa, lote...
}

/// <summary>Dato que el modelo devolvió pero C# rechazó.</summary>
public record Descarte(string Campo, string? Valor, string? Fragmento, string Motivo);

public record ResultadoExtraccion(
    PoderExtraido Poder,
    List<Descarte> Descartes,
    string Modelo,
    int? TokensEntrada,
    int? TokensSalida,
    long DuracionMs);
