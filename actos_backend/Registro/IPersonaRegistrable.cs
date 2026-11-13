using actos_backend.Comun;
using actos_backend.Datos;

namespace actos_backend.Registro;

/// <summary>
/// Lo que necesita el guardado para llevar una persona física al registro (pf_maestro y sus históricos).
/// Lo implementa la parte de cada acto (otorgante/apoderado del poder, vendedor/comprador de la
/// compraventa...): el guardado no sabe de qué acto viene.
/// </summary>
public interface IPersonaRegistrable
{
    Dato NombreCompleto { get; }
    Dato Nombres { get; }
    Dato Apellido { get; }
    Dato Dni { get; }
    Dato Cuil { get; }
    Dato Genero { get; }
    Dato EstadoCivil { get; }
    Dato Nacionalidad { get; }
    Dato FechaNacimiento { get; }
    Dato Domicilio { get; }
    Dato Padre { get; }
    Dato Madre { get; }

    /// <summary>Id en pf_maestro. El guardado lo completa cuando crea a la persona.</summary>
    int? PfId { get; set; }

    /// <summary>Posibles coincidencias sin confirmar: mientras haya, no se guarda nada.</summary>
    List<CandidatoPersona> Candidatos { get; }
}
