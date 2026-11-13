-- V0006 · Modelos de IA elegibles desde el front y registro de pruebas de conexión.
-- activo: si aparece para elegir. en_uso: el que usa el backend (uno solo a la vez).
-- nivel_razonamiento: reasoning_effort para ese modelo; null = el de appsettings (Ia:NivelRazonamiento).

CREATE TABLE ia_modelos (
    id                  SERIAL PRIMARY KEY,
    modelo              TEXT NOT NULL UNIQUE,      -- id que se manda al proveedor
    nombre              TEXT NOT NULL,
    descripcion         TEXT NOT NULL DEFAULT '',
    nivel_razonamiento  TEXT NULL,
    activo              BOOLEAN NOT NULL DEFAULT TRUE,
    en_uso              BOOLEAN NOT NULL DEFAULT FALSE,
    orden               INTEGER NOT NULL DEFAULT 0
);

-- Como máximo un modelo en uso.
CREATE UNIQUE INDEX ux_ia_modelos_en_uso ON ia_modelos (en_uso) WHERE en_uso;

-- Modelos del free tier de Gemini (noviembre 2026). Verificá los ids en AI Studio si alguno da 404.
INSERT INTO ia_modelos (modelo, nombre, descripcion, en_uso, orden) VALUES
('gemini-3.5-flash-lite', 'Gemini 3.5 Flash-Lite', 'Rápido y liviano. El que veníamos usando.', TRUE,  10),
('gemini-3.8-flash',      'Gemini 3.8 Flash',      'El Flash más nuevo del free tier.',         FALSE, 20),
('gemini-3.6-flash',      'Gemini 3.6 Flash',      'Flash de la generación anterior.',          FALSE, 30),
('gemini-3.1-flash-lite', 'Gemini 3.1 Flash-Lite', 'Flash-Lite anterior, de respaldo.',         FALSE, 40);

CREATE TABLE ia_pruebas_conexion (
    id                SERIAL PRIMARY KEY,
    fecha             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    modelo            TEXT NOT NULL,
    ok                BOOLEAN NOT NULL,
    duracion_ms       BIGINT NOT NULL,
    status_proveedor  INTEGER NULL,
    respuesta         TEXT NULL,
    error             TEXT NULL,
    detalle           TEXT NULL
);

CREATE INDEX ix_ia_pruebas_fecha ON ia_pruebas_conexion (fecha DESC);
