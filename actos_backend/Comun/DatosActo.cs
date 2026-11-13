namespace actos_backend.Comun;

/// <summary>
/// Lo común a los datos de cualquier acto (poder, compraventa, ...). Cada acto hereda y suma lo suyo
/// (el poder, sus personas, facultades y condiciones).
/// </summary>
public abstract class DatosActo
{
    /// <summary>
    /// Datos de los bloques (inmueble, vehículo, cuenta bancaria, expediente...), por código de bloque.
    /// Cada bloque es una lista de instancias (un solo inmueble, varios vehículos); cada instancia, campo → dato.
    /// </summary>
    public Dictionary<string, List<Dictionary<string, Dato>>> Bloques { get; set; } = [];

    /// <summary>Pestañas (códigos de actos_partes) que aplican a este trámite. Las calcula ActivadorPartes.</summary>
    public List<string> PartesActivas { get; set; } = [];

    /// <summary>Bloques de datos que aplican (los de las partes activas). Los usa el evaluador de bloques.</summary>
    public List<string> BloquesActivos { get; set; } = [];

    /// <summary>Bloques con pregunta de alcance a los que el usuario dijo que sí es algo específico.</summary>
    public List<string> BloquesConfirmados { get; set; } = [];

    /// <summary>Bloques con pregunta de alcance a los que el usuario dijo que no aplica (ej. "es para operar en general").</summary>
    public List<string> BloquesDescartados { get; set; } = [];

    /// <summary>
    /// El usuario confirmó que se genere el documento. Se vuelve a false si después cambia algún dato,
    /// para no generar con datos que no confirmó.
    /// </summary>
    public bool GeneracionConfirmada { get; set; }
}
