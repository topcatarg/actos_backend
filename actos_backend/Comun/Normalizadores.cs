namespace actos_backend.Comun;

/// <summary>Normalizan un valor o devuelven null si es inválido. Compartidos por extracción y respuestas.</summary>
public static class Normalizadores
{
    public static Func<string, string?> Codigo(IEnumerable<string> validos) =>
        v => validos.FirstOrDefault(c => c.Equals(v.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string? SoloDigitos(string v)
    {
        var digitos = new string(v.Where(char.IsDigit).ToArray());
        return digitos.Length > 0 ? digitos : null;
    }

    public static string? Fecha(string v) =>
        DateOnly.TryParseExact(v.Trim(), "yyyy-MM-dd", out var f) ? f.ToString("yyyy-MM-dd") : null;

    public static string? Genero(string v) => v.Trim().ToLowerInvariant() switch
    {
        "m" or "masculino" or "hombre" or "varon" or "varón" => "M",
        "f" or "femenino" or "mujer" => "F",
        _ => null
    };

    /// <summary>Año de 4 dígitos entre 1900 y el año que viene.</summary>
    public static string? Anio(string v)
    {
        var d = SoloDigitos(v);
        if (d is not { Length: 4 }) return null;
        var anio = int.Parse(d);
        return anio >= 1900 && anio <= DateTime.Today.Year + 1 ? d : null;
    }

    /// <summary>Patente/dominio: mayúsculas, sin espacios ni guiones (AB123CD, ABC123).</summary>
    public static string? Dominio(string v)
    {
        var s = new string(v.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return s.Length is >= 6 and <= 7 ? s : null;
    }

    /// <summary>CBU: 22 dígitos.</summary>
    public static string? Cbu(string v) => SoloDigitos(v) is { Length: 22 } d ? d : null;
}
