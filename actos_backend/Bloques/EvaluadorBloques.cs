namespace actos_backend.Bloques;

/// <summary>
/// Pendientes de los bloques de datos activos, para cualquier acto. Rutas que entiende también el front:
///   grupo  "bloques.{codigo}"
///   campo  "bloques.{codigo}[{i}].{clave}"   (o "bloques.{codigo}.alcance" para la pregunta de alcance)
/// </summary>
public static class EvaluadorBloques
{
    public static List<Pendiente> Pendientes(DatosActo datos)
    {
        var pendientes = new List<Pendiente>();

        foreach (var bloque in CatalogoBloques.De(datos.BloquesActivos))
        {
            var g = $"bloques.{bloque.Codigo}";
            if (datos.BloquesDescartados.Contains(bloque.Codigo)) continue;

            var instancias = datos.Instancias(bloque.Codigo);

            // ¿Aplica? Si el bloque lo pregunta y todavía no se sabe (ni hay datos), es lo único que se pide.
            if (bloque.PreguntaAlcance is { } pregunta && instancias.Count == 0 && !datos.BloquesConfirmados.Contains(bloque.Codigo))
            {
                pendientes.Add(new(TipoPendiente.Confirmar, g, $"{g}.alcance", $"Alcance {bloque.DelBloque}", pregunta));
                continue;
            }

            // Sin instancias: se piden los obligatorios de la primera.
            var cantidad = Math.Max(instancias.Count, 1);
            for (var i = 0; i < cantidad; i++)
            {
                var instancia = i < instancias.Count ? instancias[i] : [];
                var sufijo = bloque.Multiple && cantidad > 1 ? $" {i + 1}" : "";

                foreach (var campo in bloque.Campos)
                {
                    instancia.TryGetValue(campo.Clave, out var dato);
                    var ruta = $"{g}[{i}].{campo.Clave}";
                    var etiqueta = $"{campo.Etiqueta} {bloque.DelBloque}{sufijo}";

                    if (dato is null || !dato.TieneValor)
                    {
                        if (campo.Obligatorio) pendientes.Add(new(TipoPendiente.Falta, g, ruta, etiqueta));
                        continue;
                    }

                    if (campo.Validar?.Invoke(dato.Valor!) is { } problema)
                        pendientes.Add(new(TipoPendiente.Inconsistente, g, ruta, etiqueta, problema));
                    else if (dato.Origen == OrigenDato.Inferido)
                        pendientes.Add(new(TipoPendiente.Confirmar, g, ruta, etiqueta, dato.Valor));
                }
            }
        }

        return pendientes;
    }
}
