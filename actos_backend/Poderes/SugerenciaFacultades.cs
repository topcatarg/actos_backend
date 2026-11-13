using actos_backend.Datos;

namespace actos_backend.Poderes;

/// <summary>
/// En un poder especial, si el pedido menciona un objeto (un inmueble, un auto) y ninguna facultad elegida
/// es de la categoría que lo cubre, propone la facultad típica con origen "default". El motor ya pide
/// confirmar las facultades por defecto, así que el usuario la acepta o la saca.
/// Se aplica solo al primer mensaje: si el usuario la saca, no vuelve a aparecer.
/// </summary>
public static class SugerenciaFacultades
{
    /// <summary>Objeto (código de bloque) → categoría de facultad que lo cubre y facultad que se propone.</summary>
    private static readonly Dictionary<string, (string Categoria, string Facultad)> PorObjeto = new()
    {
        ["inmueble"] = ("inmuebles", "otorgar_escrituras"),
        ["vehiculo"] = ("automotor", "transferir_automotor"),
    };

    public static void Aplicar(PoderExtraido poder, IReadOnlyList<Clausula> clausulas)
    {
        if (poder.TipoPoder.Valor != "especial") return;

        foreach (var (objeto, (categoria, facultad)) in PorObjeto)
        {
            if (!poder.Menciona(objeto)) continue;
            if (!clausulas.Any(c => c.Tipo == "facultad" && c.Codigo == facultad)) continue;

            var cubierta = poder.Facultades.Any(f =>
                clausulas.FirstOrDefault(c => c.Codigo == f.Valor)?.Categoria == categoria);
            if (!cubierta)
                poder.Facultades.Add(new Dato { Valor = facultad, Origen = OrigenDato.Default });
        }
    }
}
