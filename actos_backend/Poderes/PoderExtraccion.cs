using System.Text.Json.Serialization;

namespace actos_backend.Poderes;

/// <summary>Datos del poder. Lo común a cualquier acto (bloques, pestañas activas, confirmación) está en DatosActo.</summary>
public class PoderExtraido : DatosActo
{
    public Dato TipoPoder { get; set; } = new();          // general | especial | bancario | judicial | administrativo
    public List<ParteExtraida> Partes { get; set; } = [];
    public List<Dato> Facultades { get; set; } = [];      // códigos de clausulas_template
    public Dato Sustitucion { get; set; } = new();        // código de condición
    public Dato Vigencia { get; set; } = new();           // código de condición
    public Dato FechaVigencia { get; set; } = new();      // solo si vigencia es hasta una fecha
    public Dato Revocabilidad { get; set; } = new();      // código de condición
}

public class ParteExtraida
{
    public Dato Rol { get; set; } = new();                // otorgante | apoderado
    /// <summary>Siempre "nombres apellido". Lo arma C# a partir de las partes cuando están las dos.</summary>
    public Dato NombreCompleto { get; set; } = new();
    public Dato Nombres { get; set; } = new();
    /// <summary>Nunca se adivina: si no está claro cuál es, queda vacío y se pregunta aparte.</summary>
    public Dato Apellido { get; set; } = new();
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

    /// <summary>El usuario dijo que no es ninguno de los candidatos: no volver a buscar por nombre.</summary>
    public bool PersonaNueva { get; set; }

    /// <summary>Posibles coincidencias por nombre, para que el usuario confirme. No completan datos.</summary>
    public List<Datos.CandidatoPersona> Candidatos { get; set; } = [];
}

/// <summary>Dato que el modelo devolvió pero C# rechazó.</summary>
public record Descarte(string Campo, string? Valor, string? Fragmento, string Motivo);

/// <summary>Algo que el usuario debería revisar, ej. un dato dicho distinto al registrado.</summary>
public record Aviso(string Campo, string Mensaje);

public record ResultadoProceso(
    MensajeRedactado Mensaje,
    PoderExtraido Poder,
    EstadoPoder Estado,
    List<Descarte> Descartes,
    List<Aviso> Avisos,
    string Modelo,
    int? TokensEntrada,
    int? TokensSalida,
    long DuracionMs);

public record ResultadoRespuesta(
    MensajeRedactado Mensaje,
    PoderExtraido Poder,
    EstadoPoder Estado,
    List<AccionAplicada> Acciones,
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
