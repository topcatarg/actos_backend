using System.Text.RegularExpressions;
using actos_backend.Bloques;
using actos_backend.Datos;

namespace actos_backend.Poderes;

/// <param name="Resultado">"aplicada" o el motivo por el que se descartó.</param>
public record AccionAplicada(string Campo, string Accion, string? Valor, string Resultado);

/// <summary>
/// Aplica al poder las acciones que interpretó la IA. C# puro: valida el campo, normaliza el valor
/// y exige que todo valor nuevo esté respaldado por un fragmento de la respuesta (salvo los roles,
/// que son interpretación). Lo que no pasa, se descarta con el motivo.
/// </summary>
public static class AplicadorRespuestas
{
    private static readonly Regex CampoParte = new(
        @"^partes\[(\d+)\]\.(rol|nombreCompleto|nombres|apellido|dni|cuil|genero|estadoCivil|nacionalidad|fechaNacimiento|domicilio|padre|madre|identidad)$");
    private static readonly string[] CamposPoder = ["tipoPoder", "facultades", "sustitucion", "vigencia", "fechaVigencia", "revocabilidad"];

    /// <summary>Máximo de personas nuevas que se pueden agregar en una sola respuesta.</summary>
    private const int MaxPartesNuevas = 3;

    public static List<AccionAplicada> Aplicar(PoderExtraido poder, IEnumerable<AccionRespuesta> acciones,
        string respuesta, IReadOnlyList<Clausula> clausulas)
    {
        var resultado = new List<AccionAplicada>();
        foreach (var a in acciones)
        {
            var motivo = Aplicar(poder, a, respuesta, clausulas);
            resultado.Add(new AccionAplicada(a.Campo, a.Accion, a.Valor, motivo ?? "aplicada"));
        }

        // Si cambió cualquier dato, la confirmación de generar ya no vale: hay que volver a pedirla.
        var cambioDatos = resultado.Any(r => r.Resultado == "aplicada" && r.Campo != "generar" && r.Accion != TipoAccion.Confirmar);
        if (cambioDatos) poder.GeneracionConfirmada = false;
        // Personas que se crearon pero quedaron vacías (todas sus acciones se descartaron).
        poder.Partes.RemoveAll(p => !p.NombreCompleto.TieneValor && !p.Dni.TieneValor && !p.Rol.TieneValor);
        return resultado;
    }

    /// <returns>null si se aplicó; si no, el motivo del descarte.</returns>
    private static string? Aplicar(PoderExtraido poder, AccionRespuesta a, string respuesta, IReadOnlyList<Clausula> clausulas)
    {
        var campo = a.Campo.Trim();

        if (campo == "facultades")
            return Facultades(poder, a, respuesta, clausulas);

        // Inmueble, vehículos, cuenta bancaria, expediente...: lo resuelve la capa genérica de bloques.
        if (AplicadorBloques.EsCampoDeBloque(campo))
            return AplicadorBloques.Aplicar(poder, campo, a.Accion, a.Valor, a.Fragmento, respuesta);

        if (campo == "generar")
        {
            if (a.Accion is not (TipoAccion.Confirmar or TipoAccion.Rechazar)) return $"Acción '{a.Accion}' no válida para generar.";
            poder.GeneracionConfirmada = a.Accion == TipoAccion.Confirmar;
            return null;
        }

        ParteExtraida? parte = null;
        string nombreCampo;
        var m = CampoParte.Match(campo);
        if (m.Success)
        {
            var i = int.Parse(m.Groups[1].Value);
            if (i >= poder.Partes.Count + MaxPartesNuevas) return "Índice de persona fuera de rango.";
            while (poder.Partes.Count <= i) poder.Partes.Add(new ParteExtraida());
            parte = poder.Partes[i];
            nombreCampo = m.Groups[2].Value;

            if (nombreCampo == "identidad")
                return Identidad(parte, a);
        }
        else if (CamposPoder.Contains(campo))
        {
            nombreCampo = campo;
        }
        else
        {
            return "Campo desconocido.";
        }

        var dato = DatoDe(poder, parte, nombreCampo);

        switch (a.Accion)
        {
            case TipoAccion.Confirmar:
                if (!dato.TieneValor) return "No hay valor para confirmar.";
                dato.Origen = OrigenDato.Confirmado;
                return null;

            case TipoAccion.Rechazar:
                dato.Valor = null; dato.Fragmento = null; dato.Inicio = null; dato.Origen = null;
                return null;

            case TipoAccion.Valor:
                if (string.IsNullOrWhiteSpace(a.Valor)) return "Valor vacío.";

                var normalizar = Normalizador(nombreCampo, clausulas);
                var valor = normalizar is null ? a.Valor.Trim() : normalizar(a.Valor);
                if (valor is null) return $"Valor inválido: '{a.Valor}'.";

                // El rol es interpretación ("al revés"): no se le exige fragmento literal.
                var inicio = a.Fragmento is null ? null : BuscadorFragmentos.Buscar(respuesta, a.Fragmento);
                if (inicio is null && nombreCampo != "rol") return "El fragmento no aparece en la respuesta.";

                // Si cambia el DNI de alguien identificado, deja de ser esa persona: se sueltan sus datos de la base.
                if (nombreCampo == "dni" && parte is not null && parte.PfId is not null && dato.Valor != valor)
                    SoltarDatosDeBase(parte);

                dato.Valor = valor;
                dato.Fragmento = inicio is null ? null : a.Fragmento;
                dato.Inicio = inicio;
                dato.Origen = OrigenDato.Respuesta;

                // Los tres campos del nombre se mantienen coherentes.
                if (parte is not null && nombreCampo is "nombres" or "apellido")
                    NombresPersona.DesdePartes(parte, OrigenDato.Respuesta);
                else if (parte is not null && nombreCampo == "nombreCompleto")
                    NombresPersona.DesdeCompleto(parte);
                return null;

            default:
                return $"Acción '{a.Accion}' no válida para este campo.";
        }
    }

    private static string? Identidad(ParteExtraida parte, AccionRespuesta a)
    {
        if (a.Accion == TipoAccion.Rechazar)
        {
            parte.Candidatos = [];
            parte.PersonaNueva = true;
            return null;
        }

        if (a.Accion is not (TipoAccion.Confirmar or TipoAccion.Valor)) return $"Acción '{a.Accion}' no válida para identidad.";
        if (parte.Candidatos.Count == 0) return "No hay candidatos para confirmar.";

        // Con un solo candidato alcanza con "sí"; con varios, el valor tiene que decir cuál (DNI o id).
        CandidatoPersona? elegido;
        if (string.IsNullOrWhiteSpace(a.Valor))
        {
            if (parte.Candidatos.Count > 1) return "Hay varios candidatos y no se indicó cuál.";
            elegido = parte.Candidatos[0];
        }
        else
        {
            var digitos = Normalizadores.SoloDigitos(a.Valor);
            elegido = parte.Candidatos.FirstOrDefault(c => Normalizadores.SoloDigitos(c.Dni ?? "") == digitos || c.PfId.ToString() == digitos);
            if (elegido is null) return $"'{a.Valor}' no coincide con ningún candidato.";
        }

        // Confirmada por el usuario: queda identificada, y el nombre se toma tal cual figura en el registro
        // (al completar, desde el DNI). El DNI dispara la búsqueda en la base.
        parte.Dni = new Dato { Valor = Normalizadores.SoloDigitos(elegido.Dni ?? ""), Origen = OrigenDato.Confirmado };
        parte.PfId = elegido.PfId;
        parte.NombreCompleto = new Dato();
        parte.Nombres = new Dato();
        parte.Apellido = new Dato();
        parte.Candidatos = [];
        return null;
    }

    private static string? Facultades(PoderExtraido poder, AccionRespuesta a, string respuesta, IReadOnlyList<Clausula> clausulas)
    {
        var codigos = clausulas.Where(c => c.Tipo == "facultad").Select(c => c.Codigo);

        switch (a.Accion)
        {
            case TipoAccion.Confirmar:
                foreach (var f in poder.Facultades.Where(f => f.Origen is OrigenDato.Default or OrigenDato.Inferido))
                    f.Origen = OrigenDato.Confirmado;
                return null;

            case TipoAccion.Rechazar:
                poder.Facultades.RemoveAll(f => f.Origen == OrigenDato.Default);
                return null;

            case TipoAccion.Agregar:
            {
                var codigo = a.Valor is null ? null : Normalizadores.Codigo(codigos)(a.Valor);
                if (codigo is null) return $"Facultad desconocida: '{a.Valor}'.";
                var inicio = a.Fragmento is null ? null : BuscadorFragmentos.Buscar(respuesta, a.Fragmento);
                if (inicio is null) return "El fragmento no aparece en la respuesta.";
                if (poder.Facultades.All(f => f.Valor != codigo))
                    poder.Facultades.Add(new Dato { Valor = codigo, Fragmento = a.Fragmento, Inicio = inicio, Origen = OrigenDato.Respuesta });
                return null;
            }

            case TipoAccion.Quitar:
            {
                var codigo = a.Valor is null ? null : Normalizadores.Codigo(codigos)(a.Valor);
                if (codigo is null) return $"Facultad desconocida: '{a.Valor}'.";
                poder.Facultades.RemoveAll(f => f.Valor == codigo);
                return null;
            }

            default:
                return $"Acción '{a.Accion}' no válida para facultades.";
        }
    }

    private static Dato DatoDe(PoderExtraido poder, ParteExtraida? parte, string campo) => parte is not null
        ? campo switch
        {
            "rol" => parte.Rol, "nombreCompleto" => parte.NombreCompleto, "nombres" => parte.Nombres,
            "apellido" => parte.Apellido, "dni" => parte.Dni, "cuil" => parte.Cuil,
            "genero" => parte.Genero, "estadoCivil" => parte.EstadoCivil, "nacionalidad" => parte.Nacionalidad,
            "fechaNacimiento" => parte.FechaNacimiento, "domicilio" => parte.Domicilio,
            "padre" => parte.Padre, _ => parte.Madre
        }
        : campo switch
        {
            "tipoPoder" => poder.TipoPoder, "sustitucion" => poder.Sustitucion, "vigencia" => poder.Vigencia,
            "fechaVigencia" => poder.FechaVigencia, _ => poder.Revocabilidad
        };

    private static Func<string, string?>? Normalizador(string campo, IReadOnlyList<Clausula> clausulas)
    {
        IEnumerable<string> Condiciones(string cat) =>
            clausulas.Where(c => c.Tipo == "condicion" && c.Categoria == cat).Select(c => c.Codigo);

        return campo switch
        {
            "rol" => Normalizadores.Codigo(CodigosPoder.Roles),
            "tipoPoder" => Normalizadores.Codigo(CodigosPoder.TiposPoder),
            "dni" or "cuil" => Normalizadores.SoloDigitos,
            "fechaNacimiento" or "fechaVigencia" => Normalizadores.Fecha,
            "genero" => Normalizadores.Genero,
            "sustitucion" or "vigencia" or "revocabilidad" => Normalizadores.Codigo(Condiciones(campo)),
            _ => null
        };
    }

    private static void SoltarDatosDeBase(ParteExtraida parte)
    {
        parte.PfId = null;
        foreach (var d in new[] { parte.NombreCompleto, parte.Nombres, parte.Apellido, parte.Cuil, parte.Genero, parte.EstadoCivil, parte.Nacionalidad,
                                  parte.FechaNacimiento, parte.Domicilio, parte.Padre, parte.Madre })
            if (d.Origen == OrigenDato.Base)
            {
                d.Valor = null; d.Fragmento = null; d.Inicio = null; d.Origen = null;
            }
    }
}
