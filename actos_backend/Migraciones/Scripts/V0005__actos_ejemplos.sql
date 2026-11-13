-- V0005 · Ejemplo de pedido por acto: se muestra en la ayuda y se le pasa al clasificador de intención.

ALTER TABLE actos ADD COLUMN ejemplo TEXT NOT NULL DEFAULT '';

UPDATE actos SET ejemplo = 'Poder especial de Juan Pérez a favor de María Gómez para vender el auto' WHERE codigo = 'poder';
UPDATE actos SET ejemplo = 'Compraventa del departamento de Rivadavia 1500, vende Roberto Sánchez'  WHERE codigo = 'compraventa';
UPDATE actos SET ejemplo = 'Traeme el último poder de Juan Pérez'                                     WHERE codigo = 'consulta';
