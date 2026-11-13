using System.Text;
using System.Text.Json.Nodes;

namespace actos_backend.Bloques;

/// <summary>Lo que necesita la IA de los bloques: esquema de extracción, reglas y campos para respuestas.</summary>
public static class EsquemaBloques
{
    /// <summary>"bloques": { "vehiculo": [ { dominio, marca, ... } ], "inmueble": [ {...} ] }</summary>
    public static JsonObject Esquema(IReadOnlyList<Bloque> bloques) =>
        EsquemaJson.Objeto(bloques.Select(b => (b.Codigo, EsquemaJson.Lista(
            EsquemaJson.Objeto(b.Campos.Select(c => (c.Clave, EsquemaJson.Dato(c.DescripcionIa))).ToArray()))))
            .ToArray());

    /// <summary>"objetos": [ { valor: "inmueble", fragmento: "un inmueble" } ]</summary>
    public static JsonObject EsquemaObjetos(IReadOnlyList<Bloque> bloques) =>
        EsquemaJson.Lista(EsquemaJson.Dato(
            $"Objeto del acto mencionado en el texto. Valor: {string.Join(", ", bloques.Select(b => b.Codigo))}"));

    public static string Instrucciones(IReadOnlyList<Bloque> bloques)
    {
        if (bloques.Count == 0) return "";
        var codigos = string.Join(", ", bloques.Select(b => b.Codigo));
        var sb = new StringBuilder();
        sb.AppendLine("- objetos: sobre qué objetos trata el acto, aunque el texto no dé ningún dato de ellos");
        sb.AppendLine("  (ej. \"un inmueble\", \"el departamento\", \"el auto\", \"la cuenta del banco\", \"el juicio\").");
        sb.AppendLine($"  Una entrada por tipo de objeto: valor = uno de estos códigos ({codigos}); fragmento = dónde se menciona.");
        sb.AppendLine("  Si no menciona ningún objeto, dejá la lista vacía.");
        sb.AppendLine("- bloques: datos de los objetos del acto. Completá un bloque SOLO si el texto menciona ese objeto");
        sb.AppendLine("  concreto (una dirección, una patente, un banco, un expediente); si no, dejá su lista vacía.");
        foreach (var b in bloques)
            sb.AppendLine($"  · {b.Codigo}{(b.Multiple ? " (puede haber varios)" : " (uno solo)")}: " +
                          string.Join(", ", b.Campos.Select(c => c.Clave)));
        return sb.ToString();
    }

    /// <summary>Valida lo extraído con la misma función que usa el acto para sus otros datos.</summary>
    public static void Validar(DatosActo datos, Action<Dato, string, Func<string, string?>?> validarDato)
    {
        datos.Bloques ??= [];
        foreach (var (codigo, lista) in datos.Bloques)
        {
            var bloque = CatalogoBloques.Obtener(codigo);
            if (bloque is null || lista is null) continue;
            for (var i = 0; i < lista.Count; i++)
                foreach (var (clave, dato) in lista[i])
                    if (bloque.Campo(clave) is { } campo)
                        validarDato(dato, $"bloques.{codigo}[{i}].{clave}", campo.Normalizar);
        }
        datos.Limpiar();

        // Objetos: código de bloque conocido y fragmento que aparezca en el texto; uno por tipo.
        datos.Objetos ??= [];
        for (var i = 0; i < datos.Objetos.Count; i++)
            if (datos.Objetos[i] is { } objeto)
                validarDato(objeto, $"objetos[{i}]", CodigoDeBloque);
        datos.Objetos = datos.Objetos
            .Where(o => o is { TieneValor: true })
            .DistinctBy(o => o.Valor)
            .ToList();
    }

    private static string? CodigoDeBloque(string valor)
    {
        var codigo = valor.Trim().ToLowerInvariant();
        return CatalogoBloques.Obtener(codigo) is null ? null : codigo;
    }

    /// <summary>Líneas de CAMPOS para interpretar respuestas: solo bloques activos y no descartados.</summary>
    public static string CamposParaRespuestas(DatosActo datos)
    {
        var sb = new StringBuilder();
        foreach (var b in CatalogoBloques.De(datos.BloquesActivos).Where(b => !datos.BloquesDescartados.Contains(b.Codigo)))
        {
            var n = datos.Instancias(b.Codigo).Count;
            var campos = string.Join(", ", b.Campos.Select(c => "." + c.Clave));
            sb.AppendLine($"- bloques.{b.Codigo}[0]: {campos}" +
                          (b.Multiple ? $" (para agregar otro, usá el índice libre bloques.{b.Codigo}[{Math.Max(n, 1)}])" : ""));
            if (b.PreguntaAlcance is not null)
                sb.AppendLine($"- bloques.{b.Codigo}.alcance: \"confirmar\" si es para algo específico, \"rechazar\" si es en general / no aplica");
        }
        return sb.ToString();
    }
}
