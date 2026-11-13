using actos_backend.Datos;
using actos_backend.Poderes;
using actos_backend.Registro;

namespace actos_backend.Tramites;

/// <summary>Lo que necesita el front para mostrar la conversación de un trámite.</summary>
/// <param name="Datos">Los datos del acto (PoderExtraido, y más adelante los de otros actos). object para que se serialice el tipo real.</param>
public record RespuestaTramite(string CodigoTramite, string EstadoTramite, string Mensaje, EstadoPoder Estado,
    object Datos, object Detalle);

/// <summary>
/// Crea y avanza trámites persistentes. Lo usan TramitesController (endpoints por acto) y
/// AsistenteController (cuando el clasificador ya decidió el acto).
/// En cada turno, antes de guardar el trámite, las personas van al registro (GuardadoPersonas):
/// así el pf_id de las nuevas queda guardado en el trámite.
/// </summary>
public class TramitesServicio(ConversacionPoder conversacion, ITramitesRepositorio tramites, GuardadoPersonas personas)
{
    /// <summary>Códigos de la tabla actos que tienen implementación. Un acto habilitado que no esté acá se trata como no disponible.</summary>
    public static readonly IReadOnlySet<string> ActosImplementados = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "poder" };

    public static bool Soporta(string codigoActo) => ActosImplementados.Contains(codigoActo);

    /// <summary>Crea el trámite del acto indicado a partir del primer mensaje.</summary>
    public Task<RespuestaTramite> CrearAsync(string codigoActo, string texto, CancellationToken ct = default) =>
        codigoActo.ToLowerInvariant() switch
        {
            "poder" => CrearPoderAsync(texto, ct),
            _ => throw new NotSupportedException($"El acto '{codigoActo}' todavía no está implementado.")
        };

    public async Task<RespuestaTramite> CrearPoderAsync(string texto, CancellationToken ct = default)
    {
        var r = await conversacion.IniciarAsync(texto, ct);
        var registro = await personas.GuardarAsync(r.Poder.Partes, ct);

        var estadoTramite = ConversacionPoder.EstadoTramite(r.Estado);
        var guardado = await tramites.CrearAsync("poder", estadoTramite, r.Poder, r.Estado.Siguiente,
            texto, r.Mensaje.Mensaje, ct);

        return new RespuestaTramite(guardado.CodigoTramite, estadoTramite, r.Mensaje.Mensaje, r.Estado, r.Poder,
            new { r.Descartes, r.Avisos, guion = r.Mensaje.Guion, registro });
    }

    /// <summary>Siguiente mensaje en un trámite existente. Null si el trámite no existe.</summary>
    public async Task<RespuestaTramite?> ResponderAsync(string codigo, string texto, CancellationToken ct = default)
    {
        var tramite = await tramites.ObtenerAsync(codigo, ct);
        if (tramite is null) return null;

        var r = await conversacion.ResponderAsync(tramite.Poder, tramite.Siguiente, texto, ct);
        var registro = await personas.GuardarAsync(r.Poder.Partes, ct);

        var estadoTramite = ConversacionPoder.EstadoTramite(r.Estado);
        await tramites.ActualizarAsync(tramite, estadoTramite, r.Poder, r.Estado.Siguiente, texto, r.Mensaje.Mensaje, ct);

        return new RespuestaTramite(codigo, estadoTramite, r.Mensaje.Mensaje, r.Estado, r.Poder,
            new { r.Acciones, r.Avisos, guion = r.Mensaje.Guion, registro });
    }
}
