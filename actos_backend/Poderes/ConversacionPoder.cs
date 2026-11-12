using actos_backend.Datos;

namespace actos_backend.Poderes;

/// <summary>
/// El ciclo completo de la conversación, sin saber dónde se guarda el trámite.
/// Lo usan tanto los endpoints sin estado (/api/poderes) como los persistentes (/api/tramites).
/// </summary>
public class ConversacionPoder(
    PoderExtractor extractor,
    PoderCompletador completador,
    RedactorPreguntas redactor,
    InterpreteRespuestas interprete,
    ICatalogoRepositorio catalogo)
{
    /// <summary>Primer mensaje: extraer → completar → evaluar → redactar.</summary>
    public async Task<ResultadoProceso> IniciarAsync(string texto, CancellationToken ct = default)
    {
        var extraccion = await extractor.ExtraerAsync(texto, ct);
        var avisos = await completador.CompletarAsync(extraccion.Poder, ct);
        var estado = MotorFaltantes.Evaluar(extraccion.Poder, avisos);
        var mensaje = await redactor.RedactarAsync(extraccion.Poder, estado, primerMensaje: true, ct);

        return new ResultadoProceso(mensaje, extraccion.Poder, estado, extraccion.Descartes, avisos,
            extraccion.Modelo, extraccion.TokensEntrada, extraccion.TokensSalida, extraccion.DuracionMs);
    }

    /// <summary>Mensajes siguientes: interpretar → aplicar → completar → evaluar → redactar.</summary>
    /// <param name="poder">Se modifica en el lugar.</param>
    public async Task<ResultadoRespuesta> ResponderAsync(PoderExtraido poder, List<Pendiente> preguntado, string respuesta,
        CancellationToken ct = default)
    {
        var clausulas = await catalogo.ObtenerClausulasActivasAsync(ct);
        var (acciones, ia) = await interprete.InterpretarAsync(poder, preguntado, respuesta, clausulas, ct);
        var aplicadas = AplicadorRespuestas.Aplicar(poder, acciones, respuesta, clausulas);

        var avisos = await completador.CompletarAsync(poder, ct);
        var estado = MotorFaltantes.Evaluar(poder, avisos);
        var mensaje = await redactor.RedactarAsync(poder, estado, primerMensaje: false, ct);

        return new ResultadoRespuesta(mensaje, poder, estado, aplicadas, avisos,
            ia.Modelo, ia.TokensEntrada, ia.TokensSalida, ia.DuracionMs);
    }

    /// <summary>Estado del trámite para guardar: en_curso, listo (falta confirmar) o confirmado (para generar).</summary>
    public static string EstadoTramite(EstadoPoder e) =>
        e.GenerarDocumento ? "confirmado" : e.ListoParaGenerar ? "listo" : "en_curso";
}
