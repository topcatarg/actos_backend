using actos_backend.Datos;

namespace actos_backend.Bloques;

/// <summary>
/// Decide qué pestañas (actos_partes) aplican a un trámite, para cualquier acto.
/// La condición de cada parte (actos_partes.condicion) puede ser:
///   null / vacío          → siempre
///   "subtipo:bancario"     → el subtipo del acto es ese (en el poder, el tipo de poder)
///   "categoria:inmuebles"  → alguna cláusula elegida es de esa categoría
///   "a&b"                  → se tienen que cumplir las dos (ej. "subtipo:especial&categoria:inmuebles")
///   varias separadas por "|" → alcanza con que se cumpla una
/// Además, una parte con bloque de datos se activa si ya se dijo algo de ese bloque.
/// </summary>
public static class ActivadorPartes
{
    public static void Aplicar(DatosActo datos, IReadOnlyList<ParteActo> partes, string? subtipo, ISet<string> categorias)
    {
        var activas = partes.Where(p => Cumple(p.Condicion, subtipo, categorias) || (p.Bloque is { } b && datos.TieneDatos(b))).ToList();

        datos.PartesActivas = activas.Select(p => p.Codigo).ToList();
        datos.BloquesActivos = CatalogoBloques.De(activas.Select(p => p.Bloque)).Select(b => b.Codigo).ToList();
    }

    private static bool Cumple(string? condicion, string? subtipo, ISet<string> categorias)
    {
        if (string.IsNullOrWhiteSpace(condicion)) return true;

        return condicion.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(alternativa => alternativa.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .All(termino =>
                {
                    var partes = termino.Split(':', 2, StringSplitOptions.TrimEntries);
                    if (partes.Length != 2) return false;
                    return partes[0] switch
                    {
                        "subtipo" => string.Equals(subtipo, partes[1], StringComparison.OrdinalIgnoreCase),
                        "categoria" => categorias.Contains(partes[1]),
                        _ => false
                    };
                }));
    }
}
