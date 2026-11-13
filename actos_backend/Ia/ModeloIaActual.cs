using actos_backend.Datos;
using Microsoft.Extensions.Options;
using Npgsql;

namespace actos_backend.Ia;

/// <summary>
/// Qué modelo usar en cada llamada. Sale de la tabla ia_modelos (el que está en_uso);
/// si la tabla no existe todavía o no hay ninguno en uso, cae al de appsettings (Ia:Modelo).
/// Se cachea un rato para no ir a la base en cada llamada; al cambiar el modelo desde el front se invalida.
/// </summary>
public class ModeloIaActual(IServiceScopeFactory scopes, IOptions<IaOptions> opciones, ILogger<ModeloIaActual> logger)
{
    private static readonly TimeSpan DuracionCache = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private ModeloIa? _cache;
    private DateTime _vence;

    /// <summary>El de appsettings: se usa cuando la tabla no resuelve.</summary>
    public ModeloIa PorDefecto => new(opciones.Value.Modelo, NullSiVacio(opciones.Value.NivelRazonamiento));

    public async Task<ModeloIa> ObtenerAsync(CancellationToken ct = default)
    {
        if (_cache is not null && DateTime.UtcNow < _vence) return _cache;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cache is not null && DateTime.UtcNow < _vence) return _cache;

            _cache = await LeerDeLaBaseAsync(ct) ?? PorDefecto;
            _vence = DateTime.UtcNow + DuracionCache;
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Fuerza a releer la tabla en la próxima llamada.</summary>
    public void Invalidar() => _cache = null;

    /// <summary>Completa el nivel de razonamiento con el de la configuración si la fila no lo define.</summary>
    public ModeloIa DesdeFila(IaModeloFila fila) =>
        new(fila.Modelo, NullSiVacio(fila.NivelRazonamiento) ?? PorDefecto.NivelRazonamiento);

    private async Task<ModeloIa?> LeerDeLaBaseAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IIaModelosRepositorio>();
            var fila = await repo.ObtenerEnUsoAsync(ct);
            return fila is null ? null : DesdeFila(fila);
        }
        catch (NpgsqlException ex)
        {
            // Base sin migrar o caída: no frenamos la IA por eso, usamos el de appsettings.
            logger.LogWarning("No se pudo leer el modelo en uso ({Error}); se usa el de appsettings.", ex.Message);
            return null;
        }
    }

    private static string? NullSiVacio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
