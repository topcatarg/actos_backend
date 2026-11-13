-- V0010 · Cada pestaña de un acto dice qué bloque muestra y cuándo aparece.
--   bloque:    referencia común con el front. Bloques de datos (inmueble, vehiculo, cuenta_bancaria,
--              expediente) o vistas (persona, facultades, condiciones, revision).
--   rol:       en las pestañas de personas, qué rol muestran (otorgante, vendedor...).
--   condicion: null = siempre; "subtipo:x", "categoria:x"; "&" = y, "|" = o (ver ActivadorPartes).

ALTER TABLE actos_partes ADD COLUMN bloque    TEXT;
ALTER TABLE actos_partes ADD COLUMN rol       TEXT;
ALTER TABLE actos_partes ADD COLUMN condicion TEXT;

-- Poder
UPDATE actos_partes SET bloque = 'persona', rol = 'otorgante' WHERE acto_codigo = 'poder' AND codigo = 'otorgantes';
UPDATE actos_partes SET bloque = 'persona', rol = 'apoderado' WHERE acto_codigo = 'poder' AND codigo = 'apoderados';
UPDATE actos_partes SET bloque = 'facultades'                 WHERE acto_codigo = 'poder' AND codigo = 'facultades';
UPDATE actos_partes SET bloque = 'condiciones', orden = 80    WHERE acto_codigo = 'poder' AND codigo = 'condiciones';
UPDATE actos_partes SET bloque = 'revision'                   WHERE acto_codigo = 'poder' AND codigo = 'revision';

INSERT INTO actos_partes (acto_codigo, codigo, nombre, orden, bloque, condicion) VALUES
('poder', 'inmueble',   'Inmueble',        40, 'inmueble',        'subtipo:especial&categoria:inmuebles'),
('poder', 'vehiculos',  'Vehículos',       50, 'vehiculo',        'subtipo:especial&categoria:automotor'),
('poder', 'cuentas',    'Cuenta bancaria', 60, 'cuenta_bancaria', 'subtipo:bancario'),
('poder', 'expediente', 'Expediente',      70, 'expediente',      'subtipo:judicial');

-- Compraventa (todavía no habilitada): mismos bloques, otras pestañas.
-- El subtipo de la compraventa sería qué se vende: inmueble o automotor.
INSERT INTO actos_partes (acto_codigo, codigo, nombre, orden, bloque, rol, condicion) VALUES
('compraventa', 'vendedores',  'Vendedores',     10, 'persona',     'vendedor',  NULL),
('compraventa', 'compradores', 'Compradores',    20, 'persona',     'comprador', NULL),
('compraventa', 'inmueble',    'Inmueble',       30, 'inmueble',    NULL,        'subtipo:inmueble'),
('compraventa', 'vehiculos',   'Vehículos',      40, 'vehiculo',    NULL,        'subtipo:automotor'),
('compraventa', 'condiciones', 'Condiciones',    80, 'condiciones', NULL,        NULL),
('compraventa', 'revision',    'Revisión final', 90, 'revision',    NULL,        NULL);

ALTER TABLE actos_partes ALTER COLUMN bloque SET NOT NULL;
