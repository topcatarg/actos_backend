using System.Text.RegularExpressions;

namespace actos_backend.Poderes;

/// <summary>
/// Ubica en el texto original el fragmento que citó el modelo. Prueba de más estricto a
/// más tolerante: exacto, sin distinguir mayúsculas, y con espacios/saltos de línea flexibles.
/// </summary>
public static class BuscadorFragmentos
{
    /// <returns>Posición (0-based) o null si el fragmento no está en el texto.</returns>
    public static int? Buscar(string texto, string fragmento)
    {
        if (string.IsNullOrWhiteSpace(fragmento)) return null;
        fragmento = fragmento.Trim();

        var i = texto.IndexOf(fragmento, StringComparison.Ordinal);
        if (i >= 0) return i;

        i = texto.IndexOf(fragmento, StringComparison.OrdinalIgnoreCase);
        if (i >= 0) return i;

        // Mismas palabras con cualquier cantidad/tipo de espacios entre medio.
        var palabras = Regex.Split(fragmento, @"\s+").Where(p => p.Length > 0).Select(Regex.Escape);
        var patron = string.Join(@"\s+", palabras);
        var m = Regex.Match(texto, patron, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return m.Success ? m.Index : null;
    }
}
