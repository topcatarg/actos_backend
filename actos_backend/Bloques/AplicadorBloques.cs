using System.Text.RegularExpressions;

namespace actos_backend.Bloques;

/// <summary>Aplica una respuesta del usuario sobre un campo de bloque (cualquier acto).</summary>
public static class AplicadorBloques
{
    private static readonly Regex Ruta = new(@"^bloques\.([a-z_]+)(?:\[(\d+)\]\.([A-Za-z]+)|\.(alcance))$");

    public static bool EsCampoDeBloque(string campo) => campo.StartsWith("bloques.", StringComparison.Ordinal);

    /// <returns>null si se aplicó; si no, el motivo del descarte.</returns>
    public static string? Aplicar(DatosActo datos, string campo, string accion, string? valor, string? fragmento, string respuesta)
    {
        var m = Ruta.Match(campo.Trim());
        if (!m.Success) return "Campo de bloque inválido.";

        var bloque = CatalogoBloques.Obtener(m.Groups[1].Value);
        if (bloque is null) return $"Bloque desconocido: '{m.Groups[1].Value}'.";

        // Pregunta de alcance: ¿es algo específico o en general?
        if (m.Groups[4].Success)
        {
            datos.BloquesConfirmados.Remove(bloque.Codigo);
            datos.BloquesDescartados.Remove(bloque.Codigo);
            switch (accion)
            {
                case "confirmar": datos.BloquesConfirmados.Add(bloque.Codigo); return null;
                case "rechazar": datos.BloquesDescartados.Add(bloque.Codigo); return null;
                default: return $"Acción '{accion}' no válida para el alcance.";
            }
        }

        var indice = int.Parse(m.Groups[2].Value);
        var def = bloque.Campo(m.Groups[3].Value);
        if (def is null) return $"El bloque {bloque.Codigo} no tiene el campo '{m.Groups[3].Value}'.";

        var dato = datos.DatoDe(bloque, indice, def.Clave, crear: accion == "valor");
        if (dato is null) return "No existe esa instancia del bloque.";

        switch (accion)
        {
            case "confirmar":
                if (!dato.TieneValor) return "No hay valor para confirmar.";
                dato.Origen = OrigenDato.Confirmado;
                return null;

            case "rechazar":
                dato.Valor = null; dato.Fragmento = null; dato.Inicio = null; dato.Origen = null;
                datos.Limpiar();
                return null;

            case "valor":
                if (string.IsNullOrWhiteSpace(valor)) return "Valor vacío.";
                var normalizado = def.Normalizar is null ? valor.Trim() : def.Normalizar(valor);
                if (normalizado is null) { datos.Limpiar(); return $"Valor inválido: '{valor}'."; }
                var inicio = fragmento is null ? null : BuscadorFragmentos.Buscar(respuesta, fragmento);
                if (inicio is null) { datos.Limpiar(); return "El fragmento no aparece en la respuesta."; }

                dato.Valor = normalizado;
                dato.Fragmento = fragmento;
                dato.Inicio = inicio;
                dato.Origen = OrigenDato.Respuesta;
                // Dar datos de un bloque con pregunta de alcance responde la pregunta.
                datos.BloquesDescartados.Remove(bloque.Codigo);
                return null;

            default:
                return $"Acción '{accion}' no válida para un campo de bloque.";
        }
    }
}
