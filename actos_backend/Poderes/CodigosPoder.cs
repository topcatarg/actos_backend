namespace actos_backend.Poderes;

/// <summary>Valores propios del poder (los genéricos están en Comun/Normalizadores).</summary>
public static class CodigosPoder
{
    public static readonly string[] Roles = ["otorgante", "apoderado"];

    /// <summary>Tipos de poder de n8n. Es el "subtipo" del acto: activa pestañas (ver actos_partes.condicion).</summary>
    public static readonly string[] TiposPoder = ["general", "especial", "bancario", "judicial", "administrativo"];
}
