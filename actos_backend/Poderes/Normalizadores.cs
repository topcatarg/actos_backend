namespace actos_backend.Poderes;

/// <summary>Normalizan un valor o devuelven null si es inválido. Compartidos por extracción y respuestas.</summary>
public static class Normalizadores
{
    public static readonly string[] Roles = ["otorgante", "apoderado"];
    public static readonly string[] TiposPoder = ["general", "especial"];

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
}
