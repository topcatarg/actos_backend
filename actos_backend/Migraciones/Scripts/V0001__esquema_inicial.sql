-- V0001 · Esquema inicial (personas físicas y catálogo de cláusulas).
-- Subconjunto del esquema real (mismos nombres de tablas y columnas que usa n8n).

CREATE EXTENSION IF NOT EXISTS unaccent;

-- Personas físicas
CREATE TABLE pf_maestro (
    pf_id              SERIAL PRIMARY KEY,
    nombre_completo    TEXT NOT NULL,
    dni                TEXT,
    cuit_cuil          TEXT,
    fecha_nacimiento   DATE,
    nacionalidad       TEXT,
    padre              TEXT,
    madre              TEXT,
    padre_dni          TEXT,
    madre_dni          TEXT,
    genero             TEXT,                 -- 'M' / 'F'
    estado_validacion  TEXT,
    escribania_id      INTEGER NOT NULL DEFAULT 1,
    created_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at         TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX ux_pf_maestro_dni ON pf_maestro (dni) WHERE dni IS NOT NULL;

-- Domicilios (histórico: el vigente es el de desde_fecha más reciente)
CREATE TABLE pf_domicilios (
    id           SERIAL PRIMARY KEY,
    pf_id        INTEGER NOT NULL REFERENCES pf_maestro (pf_id) ON DELETE CASCADE,
    direccion    TEXT NOT NULL,
    ciudad       TEXT,
    provincia    TEXT,
    pais         TEXT DEFAULT 'Argentina',
    desde_fecha  DATE NOT NULL DEFAULT CURRENT_DATE
);

-- Estado civil (histórico, mismo criterio)
CREATE TABLE pf_estado_civil (
    id              SERIAL PRIMARY KEY,
    pf_id           INTEGER NOT NULL REFERENCES pf_maestro (pf_id) ON DELETE CASCADE,
    estado_civil    TEXT NOT NULL,
    conyuge_nombre  TEXT,
    desde_fecha     DATE NOT NULL DEFAULT CURRENT_DATE
);

-- Catálogo de facultades y condiciones
CREATE TABLE clausulas_template (
    id              SERIAL PRIMARY KEY,
    codigo          TEXT NOT NULL UNIQUE,
    nombre          TEXT NOT NULL,
    tipo            TEXT NOT NULL CHECK (tipo IN ('facultad', 'condicion')),
    categoria       TEXT NOT NULL,
    texto_template  TEXT NOT NULL,
    aplicable_a     TEXT[] NOT NULL DEFAULT '{}',   -- tipos de poder: general, especial, ...
    orden           INTEGER NOT NULL DEFAULT 0,
    activo          BOOLEAN NOT NULL DEFAULT TRUE
);
