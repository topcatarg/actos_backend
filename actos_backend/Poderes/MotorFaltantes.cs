using System.Text.RegularExpressions;

namespace actos_backend.Poderes;

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

public record EstadoPoder(
    List<Pendiente> Pendientes,
    /// <summary>El grupo a resolver ahora: los pendientes del primer grupo de la lista.</summary>
    List<Pendiente> Siguiente,
    /// <summary>Mínimo para guardar borrador (como el HTML): nombre y DNI del primer otorgante.</summary>
    bool PuedeGuardarBorrador,
    bool ListoParaGenerar,
    /// <summary>Listo y el usuario confirmó: el front ya puede pedir el documento.</summary>
    bool GenerarDocumento = false);

/// <summary>
/// Evalúa el poder y devuelve qué falta, qué confirmar y qué está mal, en el orden en que conviene
/// resolverlo. C# puro: la IA después solo redacta la pregunta.
/// Obligatorios tomados del HTML estático (persona física, igual para otorgante y apoderado),
/// más género (se pregunta siempre, no se deduce del nombre) y localidad del inmueble.
/// </summary>
public static class MotorFaltantes
{
    private static readonly (string Campo, string Nombre, Func<ParteExtraida, Dato> Dato)[] ObligatoriosPersona =
    [
        ("nombreCompleto", "Nombre completo", p => p.NombreCompleto),
        ("dni", "DNI", p => p.Dni),
        ("genero", "Género", p => p.Genero),
        ("estadoCivil", "Estado civil", p => p.EstadoCivil),
        ("nacionalidad", "Nacionalidad", p => p.Nacionalidad),
        ("fechaNacimiento", "Fecha de nacimiento", p => p.FechaNacimiento),
        ("domicilio", "Domicilio", p => p.Domicilio),
    ];

    public static EstadoPoder Evaluar(PoderExtraido poder, IEnumerable<Aviso>? avisos = null)
    {
        var pendientes = new List<Pendiente>();

        Estructura(poder, pendientes);

        // Los roles van en un grupo propio, antes que los datos de cada persona,
        // para confirmarlos todos en una sola pregunta.
        var roles = new List<Pendiente>();
        var datosPartes = new List<Pendiente>();

        // Otorgantes primero, después apoderados, después los que no tienen rol.
        var orden = poder.Partes
            .Select((parte, i) => (parte, i))
            .OrderBy(x => x.parte.Rol.Valor switch { "otorgante" => 0, "apoderado" => 1, _ => 2 })
            .ThenBy(x => x.i);
        foreach (var (parte, i) in orden)
            Parte(parte, i, avisos, roles, datosPartes);

        pendientes.AddRange(roles);
        pendientes.AddRange(datosPartes);

        Facultades(poder, pendientes);
        Inmueble(poder, pendientes);
        Condiciones(poder, pendientes);

        // Dentro de cada grupo: primero lo inconsistente, después confirmar, después lo que falta.
        var grupos = pendientes.Select(p => p.Grupo).Distinct().ToList();
        var ordenados = pendientes
            .OrderBy(p => grupos.IndexOf(p.Grupo))
            .ThenBy(p => p.Tipo switch { TipoPendiente.Inconsistente => 0, TipoPendiente.Confirmar => 1, _ => 2 })
            .ToList();

        var listo = ordenados.Count == 0;

        // Con todo completo, lo único que queda es confirmar la generación. Va como un pendiente más,
        // así la respuesta del usuario ("sí, generalo") tiene un campo donde aplicarse.
        var siguiente = !listo
            ? ordenados.Where(p => p.Grupo == ordenados[0].Grupo).ToList()
            : poder.GeneracionConfirmada
                ? []
                : [new Pendiente(TipoPendiente.Confirmar, "generar", "generar", "Generar el documento")];

        var primerOtorgante = poder.Partes.FirstOrDefault(p => p.Rol.Valor == "otorgante");
        var puedeGuardar = primerOtorgante is not null
                           && primerOtorgante.NombreCompleto.TieneValor
                           && primerOtorgante.Dni.TieneValor;

        return new EstadoPoder(ordenados, siguiente, puedeGuardar, listo, listo && poder.GeneracionConfirmada);
    }

    private static void Estructura(PoderExtraido poder, List<Pendiente> pendientes)
    {
        const string g = "estructura";

        if (!poder.TipoPoder.TieneValor)
            pendientes.Add(new(TipoPendiente.Falta, g, "tipoPoder", "Tipo de poder", "general o especial"));

        // Si hay personas sin rol o con rol inferido, primero se pregunta/confirma el rol de cada una
        // (grupo "roles"): recién con todos los roles firmes tiene sentido decir que falta el otorgante o el apoderado.
        var todasConRol = poder.Partes.All(p => p.Rol.TieneValor && p.Rol.Origen != OrigenDato.Inferido);

        if (todasConRol && !poder.Partes.Any(p => p.Rol.Valor == "otorgante"))
            pendientes.Add(new(TipoPendiente.Falta, g, "partes.otorgante", "Otorgante", "Todavía no se indicó quién otorga el poder."));

        if (todasConRol && !poder.Partes.Any(p => p.Rol.Valor == "apoderado"))
            pendientes.Add(new(TipoPendiente.Falta, g, "partes.apoderado", "Apoderado", "Todavía no se indicó a favor de quién se otorga el poder."));

        // La misma persona (por DNI) no puede aparecer dos veces.
        foreach (var repetido in poder.Partes.Where(p => p.Dni.TieneValor).GroupBy(p => p.Dni.Valor).Where(x => x.Count() > 1))
            pendientes.Add(new(TipoPendiente.Inconsistente, g, "partes", $"DNI {repetido.Key} repetido",
                $"Aparece en: {string.Join(", ", repetido.Select(Nombre))}."));
    }

    private static void Parte(ParteExtraida parte, int i, IEnumerable<Aviso>? avisos, List<Pendiente> roles, List<Pendiente> pendientes)
    {
        var g = $"partes[{i}]";
        var quien = Nombre(parte);

        if (!parte.Rol.TieneValor)
            roles.Add(new(TipoPendiente.Falta, "roles", $"{g}.rol", $"Rol de {quien}", "otorgante o apoderado"));
        else if (parte.Rol.Origen == OrigenDato.Inferido)
            roles.Add(new(TipoPendiente.Confirmar, "roles", $"{g}.rol", $"Rol de {quien}", parte.Rol.Valor));

        // Si hay candidatos sin confirmar, eso va primero y no se piden datos: al confirmar se completan solos.
        if (parte.PfId is null && parte.Candidatos.Count > 0)
        {
            var opciones = string.Join("; ", parte.Candidatos.Select(c => $"{c.NombreCompleto} (DNI {c.Dni ?? "sin DNI"}, id {c.PfId})"));
            pendientes.Add(new(TipoPendiente.Confirmar, g, $"{g}.identidad", $"Identidad de {quien}",
                $"Coincide con: {opciones}. ¿Es alguna de estas personas o es nueva?"));
            return;
        }

        foreach (var (campo, nombreCampo, dato) in ObligatoriosPersona)
            if (!dato(parte).TieneValor)
                pendientes.Add(new(TipoPendiente.Falta, g, $"{g}.{campo}", $"{nombreCampo} de {quien}"));

        // Formatos (mismas reglas que el HTML al guardar).
        if (parte.Dni.TieneValor && !Regex.IsMatch(parte.Dni.Valor!, @"^\d{7,8}$"))
            pendientes.Add(new(TipoPendiente.Inconsistente, g, $"{g}.dni", $"DNI de {quien}",
                $"'{parte.Dni.Valor}' no tiene 7 u 8 dígitos."));

        if (parte.Cuil.TieneValor && !Regex.IsMatch(parte.Cuil.Valor!, @"^\d{11}$"))
            pendientes.Add(new(TipoPendiente.Inconsistente, g, $"{g}.cuil", $"CUIL de {quien}",
                $"'{parte.Cuil.Valor}' no tiene 11 dígitos."));

        if (parte.FechaNacimiento.TieneValor)
        {
            if (!DateOnly.TryParseExact(parte.FechaNacimiento.Valor, "yyyy-MM-dd", out var f))
                pendientes.Add(new(TipoPendiente.Inconsistente, g, $"{g}.fechaNacimiento", $"Fecha de nacimiento de {quien}",
                    $"'{parte.FechaNacimiento.Valor}' no es AAAA-MM-DD."));
            else if (f.Year < 1900 || f > DateOnly.FromDateTime(DateTime.Today))
                pendientes.Add(new(TipoPendiente.Inconsistente, g, $"{g}.fechaNacimiento", $"Fecha de nacimiento de {quien}",
                    $"'{parte.FechaNacimiento.Valor}' está fuera de rango."));
        }

        if (parte.Genero.TieneValor && parte.Genero.Valor is not ("M" or "F"))
            pendientes.Add(new(TipoPendiente.Inconsistente, g, $"{g}.genero", $"Género de {quien}",
                $"'{parte.Genero.Valor}' no es M ni F."));

        // Datos dichos distintos a los de la base.
        foreach (var aviso in avisos?.Where(a => a.Campo.StartsWith($"{g}.")) ?? [])
            pendientes.Add(new(TipoPendiente.Confirmar, g, aviso.Campo, $"{Etiqueta(aviso.Campo)} de {quien}", aviso.Mensaje));
    }

    private static void Facultades(PoderExtraido poder, List<Pendiente> pendientes)
    {
        const string g = "facultades";

        if (poder.Facultades.Count == 0)
        {
            pendientes.Add(new(TipoPendiente.Falta, g, "facultades", "Facultades", "Al menos una."));
            return;
        }

        var porDefault = poder.Facultades.Where(f => f.Origen == OrigenDato.Default).Select(f => f.Valor).ToList();
        if (porDefault.Count > 0)
            pendientes.Add(new(TipoPendiente.Confirmar, g, "facultades", "Facultades aplicadas por defecto",
                string.Join(", ", porDefault)));
    }

    private static void Inmueble(PoderExtraido poder, List<Pendiente> pendientes)
    {
        // Por ahora el único poder especial es el de inmueble.
        if (poder.TipoPoder.Valor != "especial") return;

        const string g = "inmueble";
        var inm = poder.Inmueble;
        if (!inm.Direccion.TieneValor) pendientes.Add(new(TipoPendiente.Falta, g, "inmueble.direccion", "Dirección del inmueble"));
        if (!inm.Localidad.TieneValor) pendientes.Add(new(TipoPendiente.Falta, g, "inmueble.localidad", "Localidad del inmueble"));
        if (!inm.Provincia.TieneValor) pendientes.Add(new(TipoPendiente.Falta, g, "inmueble.provincia", "Provincia del inmueble"));
    }

    private static void Condiciones(PoderExtraido poder, List<Pendiente> pendientes)
    {
        const string g = "condiciones";

        Condicion(poder.Sustitucion, "sustitucion", "Sustitución");
        Condicion(poder.Vigencia, "vigencia", "Vigencia");
        Condicion(poder.Revocabilidad, "revocabilidad", "Revocabilidad");

        if (poder.Vigencia.Valor == "vigencia_fecha")
        {
            if (!poder.FechaVigencia.TieneValor)
                pendientes.Add(new(TipoPendiente.Falta, g, "fechaVigencia", "Fecha de vencimiento del poder"));
            else if (DateOnly.TryParseExact(poder.FechaVigencia.Valor, "yyyy-MM-dd", out var f) && f <= DateOnly.FromDateTime(DateTime.Today))
                pendientes.Add(new(TipoPendiente.Inconsistente, g, "fechaVigencia", "Fecha de vencimiento del poder",
                    $"'{poder.FechaVigencia.Valor}' no es una fecha futura."));
        }

        void Condicion(Dato dato, string campo, string nombre)
        {
            if (!dato.TieneValor)
                pendientes.Add(new(TipoPendiente.Falta, g, campo, nombre));
            else if (dato.Origen == OrigenDato.Default)
                pendientes.Add(new(TipoPendiente.Confirmar, g, campo, $"{nombre} (por defecto)", dato.Valor));
        }
    }

    private static string Nombre(ParteExtraida p) =>
        p.NombreCompleto.Valor ?? (p.Dni.TieneValor ? $"DNI {p.Dni.Valor}" : "persona sin nombre");

    private static string Etiqueta(string campo) => campo[(campo.LastIndexOf('.') + 1)..] switch
    {
        "nombreCompleto" => "Nombre", "dni" => "DNI", "cuil" => "CUIL", "estadoCivil" => "Estado civil",
        "nacionalidad" => "Nacionalidad", "fechaNacimiento" => "Fecha de nacimiento", "domicilio" => "Domicilio",
        "padre" => "Padre", "madre" => "Madre", "genero" => "Género", var otro => otro
    };
}
