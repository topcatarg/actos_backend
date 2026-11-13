namespace actos_backend.Comun;

public static class TipoPendiente
{
    public const string Inconsistente = "inconsistente"; // una regla no se cumple o un formato es inválido
    public const string Confirmar = "confirmar";         // hay un valor, pero no salió literal del texto
    public const string Falta = "falta";                 // dato obligatorio vacío
}

/// <param name="Grupo">Pendientes del mismo grupo se pueden preguntar juntos (ej. todos los datos de una persona).</param>
/// <param name="Campo">Ruta del dato, ej. partes[1].dni.</param>
/// <param name="Etiqueta">Texto legible, ej. "DNI de Lucía Fernández".</param>
/// <param name="Detalle">Información extra para armar la pregunta (valor actual, candidatos, motivo).</param>
public record Pendiente(string Tipo, string Grupo, string Campo, string Etiqueta, string? Detalle = null);
