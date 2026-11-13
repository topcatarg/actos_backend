using System.Globalization;
using System.Text;

namespace actos_backend.Poderes;

/// <summary>
/// Reglas de nombres de una persona: nombre completo, nombres y apellido.
/// - El nombre completo es siempre "nombres apellido".
/// - El apellido no se adivina: si no está claro, queda vacío y se pregunta aparte.
/// - Para decir que una persona del registro es la del pedido, las palabras tienen que coincidir todas.
/// </summary>
public static class NombresPersona
{
    /// <summary>Palabras normalizadas: sin acentos, en minúscula.</summary>
    public static List<string> Palabras(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return [];
        var sinAcentos = new string(s.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());
        return sinAcentos.ToLowerInvariant()
            .Split([' ', ',', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    /// <summary>Mismas palabras, sin importar orden, acentos ni mayúsculas ("Pérez Juan Carlos" = "Juan Carlos Pérez").</summary>
    public static bool MismasPalabras(string? a, string? b)
    {
        var x = Palabras(a);
        var y = Palabras(b);
        return x.Count > 0 && x.Count == y.Count && x.Order().SequenceEqual(y.Order());
    }

    /// <summary>
    /// Al extraer: deja los tres campos coherentes con lo que vino.
    /// Nunca deduce el apellido; solo arma lo que se puede armar sin adivinar.
    /// </summary>
    public static void Normalizar(ParteExtraida p)
    {
        // Nombres y apellido sin nombre completo: se arma.
        if (!p.NombreCompleto.TieneValor && p.Nombres.TieneValor && p.Apellido.TieneValor)
        {
            Asignar(p.NombreCompleto, $"{p.Nombres.Valor} {p.Apellido.Valor}", OrigenMasDebil(p.Nombres, p.Apellido));
            return;
        }

        if (!p.NombreCompleto.TieneValor) return;

        // Hay apellido y no nombres: los nombres son lo que queda del nombre completo sin el apellido.
        if (p.Apellido.TieneValor && !p.Nombres.TieneValor)
        {
            var resto = Quitar(p.NombreCompleto.Valor!, p.Apellido.Valor!);
            if (resto is not null)
                Asignar(p.Nombres, resto, p.NombreCompleto.Origen);
            else
            {
                // El apellido no está dentro del nombre completo ("Juan" + "Pérez"): el completo pasa a ser los nombres.
                Asignar(p.Nombres, p.NombreCompleto.Valor!, p.NombreCompleto.Origen);
                Asignar(p.NombreCompleto, $"{p.Nombres.Valor} {p.Apellido.Valor}", OrigenMasDebil(p.Nombres, p.Apellido));
            }
        }
    }

    /// <summary>Después de responder el apellido o los nombres: recalcula el nombre completo.</summary>
    public static void DesdePartes(ParteExtraida p, string origen)
    {
        if (p.Apellido.TieneValor && !p.Nombres.TieneValor && p.NombreCompleto.TieneValor)
        {
            // "Juan" + apellido "Pérez": lo que había eran los nombres (salvo que ya incluyera el apellido).
            var resto = Quitar(p.NombreCompleto.Valor!, p.Apellido.Valor!);
            Asignar(p.Nombres, resto ?? p.NombreCompleto.Valor!, p.NombreCompleto.Origen);
        }

        if (p.Nombres.TieneValor && p.Apellido.TieneValor)
            Asignar(p.NombreCompleto, $"{p.Nombres.Valor} {p.Apellido.Valor}", origen);
    }

    /// <summary>Después de corregir el nombre completo: si sigue terminando en el apellido conocido se re-separa; si no, se vuelve a preguntar.</summary>
    public static void DesdeCompleto(ParteExtraida p)
    {
        var resto = p.Apellido.TieneValor && p.NombreCompleto.TieneValor ? Quitar(p.NombreCompleto.Valor!, p.Apellido.Valor!) : null;
        if (resto is not null)
        {
            Asignar(p.Nombres, resto, p.NombreCompleto.Origen);
            return;
        }
        Vaciar(p.Nombres);
        Vaciar(p.Apellido);
    }

    /// <summary>Saca el apellido del final (o del principio) del nombre completo. Null si no está.</summary>
    private static string? Quitar(string completo, string apellido)
    {
        var palabras = completo.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var ap = Palabras(apellido);
        var norm = palabras.Select(w => Palabras(w).FirstOrDefault() ?? "").ToList();
        if (ap.Count == 0 || ap.Count >= palabras.Length) return null;

        if (norm.TakeLast(ap.Count).SequenceEqual(ap))
            return string.Join(' ', palabras.Take(palabras.Length - ap.Count));
        if (norm.Take(ap.Count).SequenceEqual(ap))   // "Pérez Juan Carlos"
            return string.Join(' ', palabras.Skip(ap.Count));
        return null;
    }

    private static void Asignar(Dato d, string valor, string? origen)
    {
        d.Valor = valor.Trim();
        d.Fragmento = null;
        d.Inicio = null;
        d.Origen = origen;
    }

    private static void Vaciar(Dato d)
    {
        d.Valor = null; d.Fragmento = null; d.Inicio = null; d.Origen = null;
    }

    /// <summary>Si alguna de las partes hay que confirmarla, el todo también.</summary>
    private static string? OrigenMasDebil(Dato a, Dato b) =>
        a.Origen == OrigenDato.Inferido || b.Origen == OrigenDato.Inferido ? OrigenDato.Inferido : b.Origen ?? a.Origen;
}
