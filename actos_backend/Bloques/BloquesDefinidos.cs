namespace actos_backend.Bloques;

// Bloques de datos. El front tiene la definición espejo en src/bloques/ (mismo código y mismas claves).
// Para agregar uno: una clase acá, su espejo en el front y la fila en actos_partes del acto que lo use.

/// <summary>Inmueble básico: lo mínimo para identificarlo (ej. un poder para vender).</summary>
public sealed class BloqueInmueble : Bloque
{
    public override string Codigo => "inmueble";
    public override string DelBloque => "del inmueble";

    public override IReadOnlyList<CampoBloque> Campos { get; } =
    [
        new("direccion", "Dirección", true, "Calle y número"),
        new("localidad", "Localidad", true, "Ciudad o localidad"),
        new("provincia", "Provincia", true, "Provincia"),
        new("tipo", "Tipo", false, "departamento, casa, lote, local, campo..."),
        new("matricula", "Matrícula", false, "Matrícula o inscripción registral"),
        new("nomenclatura", "Nomenclatura catastral", false, "Nomenclatura catastral"),
        new("superficie", "Superficie", false, "Superficie con su unidad, ej. 85 m2"),
    ];
}

/// <summary>
/// Inmueble con sus datos registrales completos, para escriturar (ej. una compraventa). Es otro bloque
/// (otro código) porque pide más datos que el básico; cada acto elige cuál usa en actos_partes.
/// </summary>
public sealed class BloqueInmuebleRegistral : Bloque
{
    public override string Codigo => "inmueble_registral";
    public override string DelBloque => "del inmueble";

    public override IReadOnlyList<CampoBloque> Campos { get; } =
    [
        new("direccion", "Dirección", true, "Calle y número"),
        new("localidad", "Localidad", true, "Ciudad o localidad"),
        new("provincia", "Provincia", true, "Provincia"),
        new("tipo", "Tipo", true, "departamento, casa, lote, local, campo..."),
        new("superficie", "Superficie", true, "Superficie con su unidad, ej. 85 m2"),
        new("matricula", "Matrícula", true, "Matrícula o inscripción registral"),
        new("partida", "Partida inmobiliaria", false, "Número de partida inmobiliaria"),
        new("nomenclatura", "Nomenclatura catastral", true, "Nomenclatura catastral"),
    ];
}

public sealed class BloqueVehiculo : Bloque
{
    public override string Codigo => "vehiculo";
    public override string DelBloque => "del vehículo";
    public override bool Multiple => true;

    public override IReadOnlyList<CampoBloque> Campos { get; } =
    [
        new("dominio", "Dominio", true, "Patente, ej. AB123CD o ABC123", Normalizadores.Dominio),
        new("marca", "Marca", true, "Marca"),
        new("modelo", "Modelo", true, "Modelo"),
        new("anio", "Año", true, "Año de fabricación, 4 dígitos", Normalizadores.Anio),
        new("motor", "N° de motor", false, "Número de motor"),
        new("chasis", "N° de chasis", false, "Número de chasis"),
    ];
}

public sealed class BloqueCuentaBancaria : Bloque
{
    public override string Codigo => "cuenta_bancaria";
    public override string DelBloque => "de la cuenta";
    public override bool Multiple => true;
    public override string? PreguntaAlcance => "¿Es para un banco o una cuenta específicos, o para operar cuentas en general?";

    public override IReadOnlyList<CampoBloque> Campos { get; } =
    [
        new("banco", "Banco", true, "Nombre del banco"),
        new("tipoCuenta", "Tipo de cuenta", false, "caja de ahorro, cuenta corriente..."),
        new("numeroCuenta", "N° de cuenta", false, "Número de cuenta"),
        new("cbu", "CBU", false, "22 dígitos", Normalizadores.Cbu),
    ];
}

public sealed class BloqueExpediente : Bloque
{
    public override string Codigo => "expediente";
    public override string DelBloque => "del expediente";
    public override string? PreguntaAlcance => "¿Es para un expediente o juicio específico, o es genérico?";

    public override IReadOnlyList<CampoBloque> Campos { get; } =
    [
        new("numero", "N° de expediente", true, "Número de expediente"),
        new("juzgado", "Juzgado", true, "Juzgado"),
        new("fuero", "Fuero", true, "civil, comercial, laboral, familia..."),
        new("caratula", "Carátula", false, "Carátula del expediente"),
        new("jurisdiccion", "Jurisdicción", false, "Jurisdicción"),
    ];
}
