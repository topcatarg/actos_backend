# Migraciones

Los scripts de `Scripts/` van embebidos en el .dll y los aplica `MigradorBaseDatos`.

## Endpoints

| Endpoint | Qué hace |
|---|---|
| `GET /api/db/version` | Versión actual, última disponible, pendientes y scripts modificados |
| `POST /api/db/migrar` | Base vacía → la crea entera. Base vieja → aplica solo lo que falta |
| `POST /api/db/regenerar?confirmar=true` | Borra el esquema `public` y migra desde cero. **Solo Development** |

## Reglas para agregar un cambio

1. Archivo nuevo `V000N__descripcion.sql` (N = última + 1; descripción en snake_case, sin espacios ni guiones).
2. Sin `BEGIN`/`COMMIT`: cada script ya corre en su propia transacción junto con el registro en `version_esquema`.
3. **No editar un script ya aplicado** en una base que importa: el migrador guarda el checksum y se niega a seguir
   si cambió. Mientras estamos en desarrollo, editar y regenerar está bien.
4. Si un script falla, se hace rollback de ese script y la base queda en la versión anterior; el error dice cuál falló.
