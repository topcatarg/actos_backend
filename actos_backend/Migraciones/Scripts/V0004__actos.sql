-- V0004 · Catálogo de actos y consultas que ofrece el asistente al iniciar.
-- activo:      si aparece en el menú.
-- habilitado:  si se puede elegir (false = se muestra como "Próximamente").

CREATE TABLE actos (
    id           SERIAL PRIMARY KEY,
    codigo       TEXT NOT NULL UNIQUE,
    nombre       TEXT NOT NULL,
    descripcion  TEXT NOT NULL DEFAULT '',
    tipo         TEXT NOT NULL CHECK (tipo IN ('acto', 'consulta')),
    habilitado   BOOLEAN NOT NULL DEFAULT FALSE,
    activo       BOOLEAN NOT NULL DEFAULT TRUE,
    orden        INTEGER NOT NULL DEFAULT 0
);

INSERT INTO actos (codigo, nombre, descripcion, tipo, habilitado, orden) VALUES
('poder',       'Poder',                'Preparar un poder (general, especial, bancario o judicial)', 'acto',     TRUE,  10),
('compraventa', 'Compraventa',          'Escritura de compraventa de inmueble',                         'acto',     FALSE, 20),
('consulta',    'Consultar un trámite', 'Buscar un trámite o una escritura existente',                  'consulta', FALSE, 90);
