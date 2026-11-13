using System.Text;
using System.Text.Json;
using actos_backend.Datos;
using actos_backend.Ia;

namespace actos_backend.Poderes;

/// <summary>
/// Texto libre → datos del poder. La IA solo interpreta; C# valida que cada dato tenga
/// respaldo en el texto y que los códigos existan en el catálogo. Lo que no pasa se descarta.
/// </summary>
public class PoderExtractor(IIaClient ia, ICatalogoRepositorio catalogo, ILogger<PoderExtractor> logger)
{
    private static readonly string[] TiposPoder = Normalizadores.TiposPoder;
    private static readonly string[] Roles = Normalizadores.Roles;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ResultadoExtraccion> ExtraerAsync(string texto, CancellationToken ct = default)
    {
        var clausulas = await catalogo.ObtenerClausulasActivasAsync(ct);

        // Temperatura 0: el mismo texto tiene que dar siempre la misma extracción.
        var respuesta = await ia.CompletarAsync(Instrucciones(clausulas), texto, PoderEsquema.Crear(), temperatura: 0, ct: ct);

        PoderExtraido poder;
        try
        {
            poder = JsonSerializer.Deserialize<PoderExtraido>(respuesta.Texto, Json)
                    ?? throw new JsonException("Respuesta vacía.");
        }
        catch (JsonException ex)
        {
            logger.LogWarning("La IA devolvió JSON inválido: {Texto}", respuesta.Texto);
            throw new IaException(null, "La IA no devolvió un JSON con la forma esperada.", ex.Message);
        }

        var descartes = new Validador(texto, clausulas).Validar(poder);

        return new ResultadoExtraccion(poder, descartes, respuesta.Modelo,
            respuesta.TokensEntrada, respuesta.TokensSalida, respuesta.DuracionMs);
    }

    private static string Instrucciones(IReadOnlyList<Clausula> clausulas)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
            Extraés datos de un pedido de poder notarial escrito en español rioplatense.

            REGLAS:
            - Extraé SOLO lo que el texto dice explícitamente. Nunca deduzcas, completes ni inventes.
            - Si un dato no está en el texto, devolvé valor null y fragmento null.
            - "fragmento" es una copia TEXTUAL y exacta del pedazo del texto que respalda el valor
              (copiá letra por letra, sin corregir ni resumir). Usá el pedazo más corto que alcance.
            - "valor" va normalizado: DNI y CUIL solo dígitos; fechas AAAA-MM-DD; códigos en minúscula.
            - Partes: una entrada por persona. Otorgante es quien da el poder; apoderado es quien lo recibe.
              El rol es la única excepción a "no deduzcas": si el rol se entiende por el contexto, SIEMPRE
              completá su valor (otorgante o apoderado). Lo que puede quedar en null es solo el fragmento,
              cuando no hay palabras concretas que lo digan. Dejá el valor del rol en null únicamente si
              es imposible saber qué papel tiene la persona.
            - nombreCompleto: TODOS los nombres y apellidos de la persona tal como aparecen en el texto
              ("Juan Carlos Pérez", nunca solo "Juan" si el apellido está escrito). Si el texto dice solo el
              nombre de pila, devolvé solo eso: no inventes ni completes el apellido.
            - nombres y apellido: separalos solo si es claro cuál es cuál ("Juan Carlos Pérez" → nombres
              "Juan Carlos", apellido "Pérez"). Si hay duda ("Juan Carlos", "María José", un solo nombre),
              dejá el apellido en null: se va a preguntar aparte. Nunca adivines el apellido.
            - tipoPoder: "general" o "especial" (especial = para un acto o bien determinado).
            - Facultades y condiciones: usá únicamente los códigos de las listas de abajo, y solo si el texto
              las pide. Si el texto no menciona ninguna facultad, devolvé la lista vacía. No agregues defaults.
            - inmueble: solo si el texto menciona un inmueble concreto; si no, todos sus campos en null.
            """);

        sb.AppendLine("FACULTADES (código: descripción):");
        foreach (var c in clausulas.Where(c => c.Tipo == "facultad"))
            sb.AppendLine($"- {c.Codigo}: {c.Nombre}");

        foreach (var categoria in new[] { "sustitucion", "vigencia", "revocabilidad" })
        {
            sb.AppendLine($"CONDICIONES DE {categoria.ToUpperInvariant()} (código: descripción):");
            foreach (var c in clausulas.Where(c => c.Tipo == "condicion" && c.Categoria == categoria))
                sb.AppendLine($"- {c.Codigo}: {c.Nombre}");
        }

        return sb.ToString();
    }

    /// <summary>Recorre lo extraído, ubica cada fragmento en el texto y descarta lo que no tiene respaldo.</summary>
    private class Validador(string texto, IReadOnlyList<Clausula> clausulas)
    {
        private readonly List<Descarte> _descartes = [];

        public List<Descarte> Validar(PoderExtraido p)
        {
            // Por si el proveedor no respetó el schema y mandó null en vez de lista/objeto.
            p.Partes ??= [];
            p.Facultades ??= [];
            p.Inmueble ??= new();

            Dato(p.TipoPoder, "tipoPoder", Codigo(TiposPoder));

            for (var i = 0; i < p.Partes.Count; i++)
            {
                var parte = p.Partes[i];
                var c = $"partes[{i}]";
                // El rol es una interpretación: si no hay frase literal, se conserva como "inferido" para confirmar.
                Dato(parte.Rol, $"{c}.rol", Codigo(Roles), fragmentoOpcional: true);
                parte.Nombres ??= new();
                parte.Apellido ??= new();
                Dato(parte.NombreCompleto, $"{c}.nombreCompleto");
                Dato(parte.Nombres, $"{c}.nombres");
                Dato(parte.Apellido, $"{c}.apellido");
                Dato(parte.Dni, $"{c}.dni", SoloDigitos);
                Dato(parte.Cuil, $"{c}.cuil", SoloDigitos);
                Dato(parte.EstadoCivil, $"{c}.estadoCivil");
                Dato(parte.Nacionalidad, $"{c}.nacionalidad");
                Dato(parte.FechaNacimiento, $"{c}.fechaNacimiento", Fecha);
                Dato(parte.Domicilio, $"{c}.domicilio");
                Dato(parte.Padre, $"{c}.padre");
                Dato(parte.Madre, $"{c}.madre");

                // Citar el nombre de la persona no respalda su rol.
                if (parte.Rol.Origen == OrigenDato.Texto && parte.NombreCompleto.TieneValor &&
                    parte.NombreCompleto.Valor!.Contains(parte.Rol.Fragmento!.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    parte.Rol.Origen = OrigenDato.Inferido;
                    parte.Rol.Fragmento = null;
                    parte.Rol.Inicio = null;
                }
            }
            CompletarRolesPorDescarte(p);

            // Una parte sin nombre ni DNI no sirve para nada.
            p.Partes.RemoveAll(x => !x.NombreCompleto.TieneValor && !x.Dni.TieneValor);

            var facultades = clausulas.Where(c => c.Tipo == "facultad").Select(c => c.Codigo).ToArray();
            for (var i = 0; i < p.Facultades.Count; i++)
                Dato(p.Facultades[i], $"facultades[{i}]", Codigo(facultades));
            p.Facultades.RemoveAll(f => !f.TieneValor);

            Dato(p.Sustitucion, "sustitucion", Codigo(Condiciones("sustitucion")));
            Dato(p.Vigencia, "vigencia", Codigo(Condiciones("vigencia")));
            Dato(p.FechaVigencia, "fechaVigencia", Fecha);
            Dato(p.Revocabilidad, "revocabilidad", Codigo(Condiciones("revocabilidad")));

            Dato(p.Inmueble.Direccion, "inmueble.direccion");
            Dato(p.Inmueble.Localidad, "inmueble.localidad");
            Dato(p.Inmueble.Provincia, "inmueble.provincia");
            Dato(p.Inmueble.Matricula, "inmueble.matricula");
            Dato(p.Inmueble.Nomenclatura, "inmueble.nomenclatura");
            Dato(p.Inmueble.Tipo, "inmueble.tipo");

            return _descartes;
        }

        /// <summary>
        /// Si todas las partes con rol tienen el mismo (ej. un otorgante) y otras quedaron sin rol,
        /// a esas les corresponde el otro rol. Queda como inferido, para confirmar.
        /// </summary>
        private static void CompletarRolesPorDescarte(PoderExtraido p)
        {
            var conRol = p.Partes.Where(x => x.Rol.TieneValor).Select(x => x.Rol.Valor).Distinct().ToList();
            if (conRol.Count != 1) return;

            var otro = conRol[0] == "otorgante" ? "apoderado" : "otorgante";
            foreach (var parte in p.Partes.Where(x => !x.Rol.TieneValor))
            {
                parte.Rol.Valor = otro;
                parte.Rol.Fragmento = null;
                parte.Rol.Inicio = null;
                parte.Rol.Origen = OrigenDato.Inferido;
            }
        }

        private string[] Condiciones(string categoria) =>
            clausulas.Where(c => c.Tipo == "condicion" && c.Categoria == categoria).Select(c => c.Codigo).ToArray();

        /// <param name="normalizar">Devuelve el valor normalizado, o null si es inválido.</param>
        /// <param name="fragmentoOpcional">Si el fragmento no está, conservar el valor como inferido en vez de descartarlo.</param>
        private void Dato(Dato? d, string campo, Func<string, string?>? normalizar = null, bool fragmentoOpcional = false)
        {
            if (d is null) return;

            if (!d.TieneValor)
            {
                d.Valor = null; d.Fragmento = null; d.Inicio = null; d.Origen = null;
                return;
            }

            var valor = d.Valor!.Trim();
            var inicio = d.Fragmento is null ? null : BuscadorFragmentos.Buscar(texto, d.Fragmento);
            if (inicio is null && !fragmentoOpcional)
            {
                Descartar(d, campo, "El fragmento no aparece en el texto.");
                return;
            }

            if (normalizar is not null)
            {
                var normalizado = normalizar(valor);
                if (normalizado is null)
                {
                    Descartar(d, campo, $"Valor inválido: '{valor}'.");
                    return;
                }
                valor = normalizado;
            }

            d.Valor = valor;
            d.Inicio = inicio;
            if (inicio is null) d.Fragmento = null;
            d.Origen = inicio is null ? OrigenDato.Inferido : OrigenDato.Texto;
        }

        private void Descartar(Dato d, string campo, string motivo)
        {
            _descartes.Add(new Descarte(campo, d.Valor, d.Fragmento, motivo));
            d.Valor = null; d.Fragmento = null; d.Inicio = null; d.Origen = null;
        }

        private static Func<string, string?> Codigo(IEnumerable<string> validos) => Normalizadores.Codigo(validos);
        private static string? SoloDigitos(string v) => Normalizadores.SoloDigitos(v);
        private static string? Fecha(string v) => Normalizadores.Fecha(v);
    }
}
