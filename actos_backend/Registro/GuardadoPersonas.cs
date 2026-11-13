using System.Globalization;
using System.Text;
using actos_backend.Comun;
using actos_backend.Datos;
using Npgsql;

namespace actos_backend.Registro;

/// <param name="Resultado">creada | actualizada | sin_cambios | omitida | error.</param>
/// <param name="Detalle">Qué se escribió, o por qué no.</param>
public record PersonaGuardada(string? Nombre, int? PfId, string Resultado, IReadOnlyList<string> Detalle);

/// <summary>
/// Guarda automáticamente en el registro las personas que se cargan en cualquier acto.
///
/// Qué se guarda: solo lo que dijo el usuario (origen texto, respuesta o confirmado). Lo inferido espera a
/// que se confirme; lo que vino de la base o por default no se reescribe.
///
/// Persona nueva: se crea cuando tiene nombre completo y DNI válido, sin candidatos pendientes y con un DNI
/// que no esté ya en el registro (si está, la conversación tiene que resolver si es esa persona).
///
/// Persona existente (PfId):
/// - pf_maestro: se completan los campos vacíos. Un valor distinto al registrado se escribe solo si el usuario
///   lo confirmó (mientras tanto el completador avisa la diferencia). El DNI nunca se cambia.
/// - Domicilio y estado civil son históricos: un valor nuevo agrega una fila con fecha de hoy; si la última
///   fila ya es de hoy, se corrige esa (es una corrección, no una mudanza).
///
/// Es idempotente: se corre en cada turno y lo que ya está igual no se toca. Si falla, se loguea y la
/// conversación sigue; en el turno siguiente se vuelve a intentar.
/// </summary>
public class GuardadoPersonas(IRegistroPersonasRepositorio registro, ILogger<GuardadoPersonas> logger)
{
    private static readonly HashSet<string> OrigenesNoPropios = [OrigenDato.Inferido, OrigenDato.Base, OrigenDato.Default];

    private record Campo(string Columna, Func<IPersonaRegistrable, Dato?> Dato, Func<string, string?> Normalizar,
        Func<string, string> Clave);

    /// <summary>Columnas de pf_maestro. Normalizar devuelve null si el valor no es válido (no se guarda).</summary>
    private static readonly Campo[] CamposMaestro =
    [
        new("nombre_completo", p => p.NombreCompleto, Texto, ClaveTexto),
        new("nombres", p => p.Nombres, Texto, ClaveTexto),
        new("apellido", p => p.Apellido, Texto, ClaveTexto),
        new("dni", p => p.Dni, Dni, Digitos),
        new("cuit_cuil", p => p.Cuil, Cuil, Digitos),
        new("fecha_nacimiento", p => p.FechaNacimiento, FechaNacimiento, s => s),
        new("nacionalidad", p => p.Nacionalidad, Texto, ClaveTexto),
        new("padre", p => p.Padre, Texto, ClaveTexto),
        new("madre", p => p.Madre, Texto, ClaveTexto),
        new("genero", p => p.Genero, Genero, s => s),
    ];

    public async Task<List<PersonaGuardada>> GuardarAsync(IEnumerable<IPersonaRegistrable> personas, CancellationToken ct = default)
    {
        var resultados = new List<PersonaGuardada>();
        foreach (var p in personas)
        {
            PersonaGuardada r;
            try
            {
                r = await GuardarAsync(p, ct);
            }
            catch (NpgsqlException ex)
            {
                logger.LogError(ex, "Registro de personas: no se pudo guardar a {Nombre}.", p.NombreCompleto?.Valor);
                r = new PersonaGuardada(p.NombreCompleto?.Valor, p.PfId, "error", [ex.Message]);
            }

            if (r.Resultado is "creada" or "actualizada")
                logger.LogInformation("Registro de personas: {Nombre} (pf_id {PfId}) {Resultado}: {Detalle}",
                    r.Nombre, r.PfId, r.Resultado, string.Join(", ", r.Detalle));
            resultados.Add(r);
        }
        return resultados;
    }

    private async Task<PersonaGuardada> GuardarAsync(IPersonaRegistrable p, CancellationToken ct)
    {
        var nombre = p.NombreCompleto?.Valor;
        var maestro = ValoresPropios(p);
        var domicilio = Propio(p.Domicilio, Texto);
        var estadoCivil = Propio(p.EstadoCivil, Texto);

        if (p.PfId is null)
        {
            if (p.Candidatos is { Count: > 0 })
                return Omitida(p, "hay candidatos del registro sin confirmar");
            if (!maestro.TryGetValue("nombre_completo", out _) || !maestro.TryGetValue("dni", out var dni))
                return Omitida(p, "falta nombre completo o DNI válido");
            if (await registro.PfIdPorDniAsync(dni.Valor, ct) is not null)
                return Omitida(p, $"el DNI {dni.Valor} ya está en el registro; falta confirmar si es esa persona");

            var plan = new PlanPersona(null,
                maestro.ToDictionary(kv => kv.Key, kv => kv.Value.Valor),
                domicilio?.Valor, estadoCivil?.Valor);
            p.PfId = await registro.AplicarAsync(plan, ct);
            return new PersonaGuardada(nombre, p.PfId, "creada", Describir(plan));
        }

        var actual = await registro.ObtenerAsync(p.PfId.Value, ct);
        if (actual is null)
            return Omitida(p, $"pf_id {p.PfId} ya no está en el registro");

        var cambios = new Dictionary<string, string>();
        foreach (var campo in CamposMaestro)
        {
            if (!maestro.TryGetValue(campo.Columna, out var nuevo)) continue;
            var registrado = actual.Valor(campo.Columna);

            if (string.IsNullOrWhiteSpace(registrado))
                cambios[campo.Columna] = nuevo.Valor;
            else if (campo.Columna != "dni" && campo.Clave(registrado) != campo.Clave(nuevo.Valor) && nuevo.Confirmado)
                cambios[campo.Columna] = nuevo.Valor;
        }

        var cambioDomicilio = Historico(actual.Domicilio, domicilio, MismoDomicilio);
        var cambioEstadoCivil = Historico(actual.EstadoCivil, estadoCivil, (a, b) => ClaveEstadoCivil(a) == ClaveEstadoCivil(b));

        var planExistente = new PlanPersona(p.PfId, cambios, cambioDomicilio, cambioEstadoCivil);
        if (planExistente.Vacio)
            return new PersonaGuardada(nombre, p.PfId, "sin_cambios", []);

        await registro.AplicarAsync(planExistente, ct);
        return new PersonaGuardada(nombre, p.PfId, "actualizada", Describir(planExistente));
    }

    /// <summary>Valor a escribir en un histórico, o null si no corresponde.</summary>
    private static string? Historico(string? registrado, (string Valor, bool Confirmado)? nuevo, Func<string, string, bool> iguales)
    {
        if (nuevo is null) return null;
        if (string.IsNullOrWhiteSpace(registrado)) return nuevo.Value.Valor;
        if (iguales(registrado, nuevo.Value.Valor)) return null;
        return nuevo.Value.Confirmado ? nuevo.Value.Valor : null;
    }

    private static Dictionary<string, (string Valor, bool Confirmado)> ValoresPropios(IPersonaRegistrable p)
    {
        var valores = new Dictionary<string, (string, bool)>();
        foreach (var campo in CamposMaestro)
            if (Propio(campo.Dato(p), campo.Normalizar) is { } v)
                valores[campo.Columna] = v;
        return valores;
    }

    /// <summary>El valor normalizado si lo dio el usuario y es válido; si no, null.</summary>
    private static (string Valor, bool Confirmado)? Propio(Dato? dato, Func<string, string?> normalizar)
    {
        if (dato is null || !dato.TieneValor) return null;
        if (dato.Origen is not null && OrigenesNoPropios.Contains(dato.Origen)) return null;
        var valor = normalizar(dato.Valor!);
        return valor is null ? null : (valor, dato.Origen == OrigenDato.Confirmado);
    }

    private static PersonaGuardada Omitida(IPersonaRegistrable p, string motivo) =>
        new(p.NombreCompleto?.Valor, p.PfId, "omitida", [motivo]);

    private static List<string> Describir(PlanPersona plan)
    {
        var d = plan.Maestro.Keys.ToList();
        if (plan.Domicilio is not null) d.Add("domicilio");
        if (plan.EstadoCivil is not null) d.Add("estado_civil");
        return d;
    }

    // ---------- Normalización y comparación ----------

    private static string? Texto(string v)
    {
        var t = string.Join(' ', v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return t.Length > 0 ? t : null;
    }

    private static string Digitos(string v) => new(v.Where(char.IsDigit).ToArray());

    private static string? Dni(string v) => Digitos(v) is { Length: >= 7 and <= 8 } d ? d : null;

    /// <summary>Con guiones, como los que ya están en el registro (20-12345678-3).</summary>
    private static string? Cuil(string v) => Digitos(v) is { Length: 11 } d ? $"{d[..2]}-{d[2..10]}-{d[10]}" : null;

    private static string? FechaNacimiento(string v) =>
        DateOnly.TryParseExact(v.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)
        && f.Year >= 1900 && f <= DateOnly.FromDateTime(DateTime.Today)
            ? f.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;

    private static string? Genero(string v) => v.Trim().ToUpperInvariant() is "M" or "F" ? v.Trim().ToUpperInvariant() : null;

    /// <summary>Sin acentos, mayúsculas ni puntuación.</summary>
    private static string ClaveTexto(string v)
    {
        var sb = new StringBuilder();
        foreach (var c in v.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>"casada" ≈ "casado", "viuda" ≈ "viudo".</summary>
    private static string ClaveEstadoCivil(string v)
    {
        var k = ClaveTexto(v);
        return k.EndsWith('a') ? k[..^1] + "o" : k;
    }

    /// <summary>Tolerante: si uno contiene al otro cuenta como el mismo (ej. "Colón 2200" vs "Colón 2200, Tandil").</summary>
    private static bool MismoDomicilio(string registrado, string nuevo)
    {
        var a = ClaveTexto(registrado);
        var b = ClaveTexto(nuevo);
        return a == b || a.Contains(b) || b.Contains(a);
    }
}
