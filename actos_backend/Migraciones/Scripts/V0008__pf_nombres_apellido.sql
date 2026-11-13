-- V0008 · Nombre y apellido por separado en el registro de personas.
-- nombre_completo se mantiene (lo usan n8n y los documentos); nombres y apellido se suman.
-- En los registros viejos quedan en null: el asistente los pregunta y se completan al guardar.

ALTER TABLE pf_maestro ADD COLUMN nombres  TEXT;
ALTER TABLE pf_maestro ADD COLUMN apellido TEXT;

-- Personas de prueba (V0002)
UPDATE pf_maestro SET nombres = 'Juan Carlos',    apellido = 'Pérez'     WHERE dni = '20123456';
UPDATE pf_maestro SET nombres = 'María Laura',    apellido = 'Gómez'     WHERE dni = '27654321';
UPDATE pf_maestro SET nombres = 'Roberto Daniel', apellido = 'Sánchez'   WHERE dni = '18456789';
UPDATE pf_maestro SET nombres = 'Lucía',          apellido = 'Fernández' WHERE dni = '33222111';
