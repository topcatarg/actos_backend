namespace actos_backend.Bloques;

/// <summary>Acceso a los datos de bloques de un acto (cualquiera).</summary>
public static class DatosBloques
{
    public static List<Dictionary<string, Dato>> Instancias(this DatosActo datos, string codigo) =>
        datos.Bloques.TryGetValue(codigo, out var lista) ? lista : [];

    /// <summary>El dato de un campo; si no existe, lo crea (y la instancia, si es la siguiente libre).</summary>
    public static Dato? DatoDe(this DatosActo datos, Bloque bloque, int indice, string clave, bool crear)
    {
        if (!datos.Bloques.TryGetValue(bloque.Codigo, out var lista))
        {
            if (!crear) return null;
            datos.Bloques[bloque.Codigo] = lista = [];
        }

        if (indice == lista.Count && crear && (bloque.Multiple || lista.Count == 0))
            lista.Add([]);
        if (indice < 0 || indice >= lista.Count) return null;

        var instancia = lista[indice];
        if (!instancia.TryGetValue(clave, out var dato))
        {
            if (!crear) return null;
            instancia[clave] = dato = new Dato();
        }
        return dato;
    }

    public static bool TieneDatos(this DatosActo datos, string codigo) =>
        datos.Instancias(codigo).Any(i => i.Values.Any(d => d.TieneValor));

    /// <summary>Saca instancias vacías y campos que no son del bloque (lo que la IA pueda haber inventado).</summary>
    public static void Limpiar(this DatosActo datos)
    {
        datos.Bloques ??= [];
        foreach (var codigo in datos.Bloques.Keys.ToList())
        {
            var bloque = CatalogoBloques.Obtener(codigo);
            if (bloque is null) { datos.Bloques.Remove(codigo); continue; }

            var lista = datos.Bloques[codigo] ?? [];
            foreach (var instancia in lista)
                foreach (var clave in instancia.Keys.Where(k => bloque.Campo(k) is null).ToList())
                    instancia.Remove(clave);
            lista.RemoveAll(i => !i.Values.Any(d => d?.TieneValor == true));
            if (!bloque.Multiple && lista.Count > 1) lista.RemoveRange(1, lista.Count - 1);

            if (lista.Count == 0) datos.Bloques.Remove(codigo);
            else datos.Bloques[codigo] = lista;
        }
    }
}
