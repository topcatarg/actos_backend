-- V0003 · Trámites armados por chat. El poder completo se guarda como JSON.

CREATE TABLE chat_tramites (
    id               SERIAL PRIMARY KEY,
    codigo_tramite   TEXT NOT NULL UNIQUE,
    tipo_tramite     TEXT NOT NULL,                       -- poder
    estado           TEXT NOT NULL,                       -- en_curso | listo | confirmado
    datos            JSONB NOT NULL,                      -- el poder (PoderExtraido)
    siguiente        JSONB NOT NULL DEFAULT '[]'::jsonb,  -- lo último que se preguntó
    version          INTEGER NOT NULL DEFAULT 1,          -- control de concurrencia optimista
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Historial de la conversación, para trazabilidad.
CREATE TABLE chat_mensajes (
    id           SERIAL PRIMARY KEY,
    tramite_id   INTEGER NOT NULL REFERENCES chat_tramites (id) ON DELETE CASCADE,
    rol          TEXT NOT NULL CHECK (rol IN ('usuario', 'asistente')),
    texto        TEXT NOT NULL,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX ix_chat_mensajes_tramite ON chat_mensajes (tramite_id, id);
