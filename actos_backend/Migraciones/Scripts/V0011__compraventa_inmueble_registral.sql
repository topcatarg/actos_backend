-- V0011 · La compraventa usa el inmueble con datos registrales completos (bloque inmueble_registral);
-- el poder sigue con el inmueble básico. Mismo tipo de dato, distinta exigencia según para qué es.

UPDATE actos_partes SET bloque = 'inmueble_registral'
WHERE acto_codigo = 'compraventa' AND codigo = 'inmueble';
