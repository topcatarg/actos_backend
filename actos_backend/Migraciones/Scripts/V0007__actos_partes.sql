-- V0007 · Partes (pasos) que lleva cada acto. Arman la columna izquierda del front.
-- Solo el qué y el orden: los campos de cada parte los conoce el front por su código.

CREATE TABLE actos_partes (
    id           SERIAL PRIMARY KEY,
    acto_codigo  TEXT NOT NULL REFERENCES actos (codigo) ON UPDATE CASCADE,
    codigo       TEXT NOT NULL,      -- otorgantes, apoderados, facultades... (lo que interpreta el front)
    nombre       TEXT NOT NULL,      -- lo que se muestra
    orden        INTEGER NOT NULL DEFAULT 0,
    activo       BOOLEAN NOT NULL DEFAULT TRUE,
    UNIQUE (acto_codigo, codigo)
);

INSERT INTO actos_partes (acto_codigo, codigo, nombre, orden) VALUES
('poder', 'otorgantes',  'Otorgantes',     10),
('poder', 'apoderados',  'Apoderados',     20),
('poder', 'facultades',  'Facultades',     30),
('poder', 'condiciones', 'Condiciones',    40),
('poder', 'revision',    'Revisión final', 90);
