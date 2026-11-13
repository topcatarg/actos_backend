namespace actos_backend.Bloques;

/// <summary>Todos los bloques de datos que conoce el back, por código.</summary>
public static class CatalogoBloques
{
    private static readonly Dictionary<string, Bloque> PorCodigo =
        new Bloque[] { new BloqueInmueble(), new BloqueInmuebleRegistral(), new BloqueVehiculo(), new BloqueCuentaBancaria(), new BloqueExpediente() }
            .ToDictionary(b => b.Codigo, StringComparer.OrdinalIgnoreCase);

    public static Bloque? Obtener(string? codigo) =>
        codigo is not null && PorCodigo.TryGetValue(codigo, out var b) ? b : null;

    /// <summary>Los bloques de datos entre los códigos dados (ignora los que no son de datos: persona, facultades...).</summary>
    public static List<Bloque> De(IEnumerable<string?> codigos) =>
        codigos.Select(Obtener).OfType<Bloque>().DistinctBy(b => b.Codigo).ToList();
}
