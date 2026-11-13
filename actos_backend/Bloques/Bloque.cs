namespace actos_backend.Bloques;

/// <param name="Clave">Nombre del campo. Es parte del contrato con el front: no cambiarlo.</param>
/// <param name="DescripcionIa">Cómo se describe el campo en el esquema que se le pide a la IA.</param>
/// <param name="Normalizar">Devuelve el valor normalizado, o null si es inválido (el dato se descarta).</param>
/// <param name="Validar">Regla extra sobre el valor ya normalizado: devuelve el problema, o null si está bien.</param>
public record CampoBloque(
    string Clave,
    string Etiqueta,
    bool Obligatorio,
    string DescripcionIa,
    Func<string, string?>? Normalizar = null,
    Func<string, string?>? Validar = null);

/// <summary>
/// Un tipo de dato que se repite entre actos (inmueble, vehículo, cuenta bancaria, expediente...).
/// El back define acá sus campos y reglas; el front tiene su propia definición del mismo bloque.
/// Lo único común entre los dos es el Codigo (y las claves de los campos).
/// No conoce ningún acto: qué bloques lleva cada acto lo dice la tabla actos_partes.
/// </summary>
public abstract class Bloque
{
    /// <summary>Código del bloque: la referencia común con el front y con actos_partes.bloque.</summary>
    public abstract string Codigo { get; }

    /// <summary>Para armar textos: "Dominio del vehículo", "Banco de la cuenta".</summary>
    public abstract string DelBloque { get; }

    /// <summary>Si puede haber más de una instancia (ej. varios vehículos).</summary>
    public virtual bool Multiple => false;

    /// <summary>
    /// Si el bloque puede no aplicar aunque la pestaña esté activa, la pregunta para saberlo
    /// (ej. "¿Es para una cuenta específica o para operar en general?"). Null = siempre aplica.
    /// </summary>
    public virtual string? PreguntaAlcance => null;

    public abstract IReadOnlyList<CampoBloque> Campos { get; }

    public CampoBloque? Campo(string clave) =>
        Campos.FirstOrDefault(c => c.Clave.Equals(clave, StringComparison.OrdinalIgnoreCase));
}
