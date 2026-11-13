using actos_backend.Bloques;
using actos_backend.Datos;

namespace actos_backend.Poderes;

/// <summary>
/// Completa el poder extraído con lo que ya sabemos, sin IA:
/// 1. Personas con DNI del registro → si el nombre dicho es el mismo (todas las palabras), completa los datos
///    vacíos desde el maestro; si no, la ofrece como candidata y pregunta si es ella.
/// 2. Personas sin DNI → con nombre y apellido busca candidatos que tengan todas esas palabras, pero NO
///    completa nada: lo confirma el usuario.
/// 3. Defaults: poder general sin facultades → todas las aplicables; condiciones no dichas → sin
///    sustitución, vigencia indefinida, revocable.
/// Lo que dijo el usuario nunca se pisa: si difiere de la base, queda lo dicho y se genera un aviso.
/// </summary>
public class PoderCompletador(IPersonasRepositorio personas, ICatalogoRepositorio catalogo, IActosRepositorio actos)
{
    private const string SustitucionDefault = "sustitucion_sin";
    private const string VigenciaDefault = "vigencia_indefinida";
    private const string RevocabilidadDefault = "revocable";

    public async Task<List<Aviso>> CompletarAsync(PoderExtraido poder, CancellationToken ct = default)
    {
        var avisos = new List<Aviso>();

        for (var i = 0; i < poder.Partes.Count; i++)
            await CompletarParteAsync(poder.Partes[i], $"partes[{i}]", avisos, ct);

        await AplicarDefaultsAsync(poder, ct);
        await ActivarPartesAsync(poder, ct);

        return avisos;
    }

    private async Task CompletarParteAsync(ParteExtraida parte, string campo, List<Aviso> avisos, CancellationToken ct)
    {
        parte.Nombres ??= new();
        parte.Apellido ??= new();
        NombresPersona.Normalizar(parte);

        // Los candidatos se recalculan en cada vuelta; la elección del usuario ya quedó en PfId/DNI.
        parte.Candidatos = [];

        if (parte.Dni.TieneValor)
        {
            var persona = await personas.BuscarPorDniAsync(parte.Dni.Valor!, ct);
            if (persona is null) return; // persona nueva: lo que falte se pregunta

            // El DNI solo no alcanza: el nombre dicho tiene que ser el de esa persona (todas las palabras),
            // o el usuario tiene que haberla elegido. Si no, se le pregunta si es ella.
            var esLaMisma = parte.PfId == persona.PfId
                            || !parte.NombreCompleto.TieneValor
                            || NombresPersona.MismasPalabras(parte.NombreCompleto.Valor, persona.NombreCompleto);

            if (!esLaMisma)
            {
                parte.PfId = null;
                if (parte.PersonaNueva)
                {
                    // Dijo que no es la del registro, pero el DNI es de esa persona: hay que revisar el DNI.
                    if (parte.Dni.Origen != OrigenDato.Confirmado)
                        avisos.Add(new Aviso($"{campo}.dni",
                            $"El DNI {parte.Dni.Valor} figura en el registro a nombre de {persona.NombreCompleto}."));
                    return;
                }

                parte.Candidatos = [new CandidatoPersona
                {
                    PfId = persona.PfId, NombreCompleto = persona.NombreCompleto, Dni = persona.Dni, Coincidencias = 0
                }];
                return;
            }

            parte.PfId = persona.PfId;

            Completar(parte.NombreCompleto, persona.NombreCompleto, $"{campo}.nombreCompleto", avisos);
            Completar(parte.Nombres, persona.Nombres, $"{campo}.nombres", avisos);
            Completar(parte.Apellido, persona.Apellido, $"{campo}.apellido", avisos);
            NombresPersona.Normalizar(parte);
            Completar(parte.Cuil, SoloDigitos(persona.CuitCuil), $"{campo}.cuil", avisos);
            Completar(parte.EstadoCivil, persona.EstadoCivil, $"{campo}.estadoCivil", avisos);
            Completar(parte.Nacionalidad, persona.Nacionalidad, $"{campo}.nacionalidad", avisos);
            Completar(parte.FechaNacimiento, persona.FechaNacimiento?.ToString("yyyy-MM-dd"), $"{campo}.fechaNacimiento", avisos);
            Completar(parte.Domicilio, Domicilio(persona), $"{campo}.domicilio", avisos);
            Completar(parte.Padre, persona.Padre, $"{campo}.padre", avisos);
            Completar(parte.Madre, persona.Madre, $"{campo}.madre", avisos);
            Completar(parte.Genero, persona.Genero, $"{campo}.genero", avisos);
            return;
        }

        // Sin DNI: se busca en el registro solo con nombre y apellido, y solo personas que tengan
        // TODAS esas palabras. Nunca se toma una sola sin que el usuario la confirme.
        if (parte.PfId is null && !parte.PersonaNueva && parte.NombreCompleto.TieneValor && parte.Apellido.TieneValor)
        {
            parte.Candidatos = (await personas.BuscarPorNombreAsync(parte.NombreCompleto.Valor!, parte.Apellido.Valor!, ct)).ToList();
            if (parte.Candidatos.Count > 0)
                avisos.Add(new Aviso(campo,
                    $"'{parte.NombreCompleto.Valor}' no tiene DNI, pero hay {parte.Candidatos.Count} persona(s) con ese nombre en la base. Confirmar cuál es, o si es nueva."));
        }
    }

    /// <summary>Si el dato está vacío, lo completa desde la base; si el usuario dijo otra cosa, avisa.</summary>
    private static void Completar(Dato dato, string? valorBase, string campo, List<Aviso> avisos)
    {
        if (string.IsNullOrWhiteSpace(valorBase)) return;

        if (!dato.TieneValor)
        {
            dato.Valor = valorBase;
            dato.Origen = OrigenDato.Base;
            return;
        }

        // Si el usuario ya confirmó su valor, no se vuelve a avisar la diferencia.
        if (dato.Origen != OrigenDato.Confirmado && !Equivalentes(dato.Valor!, valorBase))
            avisos.Add(new Aviso(campo, $"Se dijo '{dato.Valor}' pero en la base figura '{valorBase}'. Se usa lo dicho."));
    }

    /// <summary>
    /// Qué pestañas aplican: el subtipo del poder es el tipo de poder, y las categorías son las de las
    /// facultades elegidas (ej. una facultad de categoría "automotor" activa la pestaña de vehículos).
    /// </summary>
    private async Task ActivarPartesAsync(PoderExtraido poder, CancellationToken ct)
    {
        var clausulas = await catalogo.ObtenerClausulasActivasAsync(Actos.Poder, ct);
        var categorias = poder.Facultades
            .Select(f => clausulas.FirstOrDefault(c => c.Codigo == f.Valor)?.Categoria)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        poder.Limpiar();
        ActivadorPartes.Aplicar(poder, await actos.ObtenerPartesAsync(Actos.Poder, ct), poder.TipoPoder.Valor, categorias);
    }

    private async Task AplicarDefaultsAsync(PoderExtraido poder, CancellationToken ct)
    {
        var clausulas = await catalogo.ObtenerClausulasActivasAsync(Actos.Poder, ct);
        bool Existe(string codigo) => clausulas.Any(c => c.Codigo == codigo);


        if (poder.TipoPoder.Valor == "general" && poder.Facultades.Count == 0)
        {
            poder.Facultades = clausulas
                .Where(c => c.Tipo == "facultad" && c.AplicableA.Contains("general"))
                .Select(c => new Dato { Valor = c.Codigo, Origen = OrigenDato.Default })
                .ToList();
        }

        Default(poder.Sustitucion, SustitucionDefault);
        Default(poder.Vigencia, VigenciaDefault);
        Default(poder.Revocabilidad, RevocabilidadDefault);

        void Default(Dato dato, string codigo)
        {
            if (dato.TieneValor || !Existe(codigo)) return;
            // Vigencia hasta una fecha dicha sin el código: no pisar con "indefinida".
            if (codigo == VigenciaDefault && poder.FechaVigencia.TieneValor) return;
            dato.Valor = codigo;
            dato.Origen = OrigenDato.Default;
        }
    }

    private static string? Domicilio(PersonaMaestro p)
    {
        var partes = new[] { p.Direccion, p.Ciudad, p.Provincia }.Where(x => !string.IsNullOrWhiteSpace(x));
        var texto = string.Join(", ", partes);
        return texto.Length > 0 ? texto : null;
    }

    private static string? SoloDigitos(string? v) =>
        v is null ? null : new string(v.Where(char.IsDigit).ToArray()) is { Length: > 0 } d ? d : null;

    /// <summary>
    /// Comparación tolerante: sin acentos ni mayúsculas, y "casada" ≈ "casado".
    /// Si uno contiene al otro también cuenta (ej. "Av. Siempreviva 742" vs el domicilio completo).
    /// </summary>
    private static bool Equivalentes(string a, string b)
    {
        static string N(string s)
        {
            var sinAcentos = new string(s.Normalize(System.Text.NormalizationForm.FormD)
                .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                .ToArray());
            var n = string.Join(' ', sinAcentos.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return n.EndsWith('a') || n.EndsWith('o') ? n[..^1] : n; // género gramatical
        }
        var x = N(a); var y = N(b);
        return x == y || x.Contains(y) || y.Contains(x);
    }
}
