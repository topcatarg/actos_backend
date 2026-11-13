-- V0009 · Catálogo de facultades completo (el de n8n) y a qué acto pertenece cada cláusula.
--
-- Origen de los datos:
--   · códigos y nombres: REALES, de la tabla de mapeo del prompt de n8n (respetando mayúsculas: Locacion, Operaciones_bancarias...).
--   · categoria y aplicable_a: INFERIDOS (n8n no los trae en el flujo). Revisarlos contra la tabla de producción.
--   · texto_template: DE PRUEBA. El texto real está en producción; se usa recién al generar el documento.
-- Para reemplazar por los datos reales: exportar clausulas_template de producción con pg_dump --data-only.

-- Cada cláusula pertenece a un acto (hoy todas son de poder). Prepara el catálogo para compraventa y otros.
ALTER TABLE clausulas_template
    ADD COLUMN acto TEXT NOT NULL DEFAULT 'poder' REFERENCES actos (codigo) ON UPDATE CASCADE;
CREATE INDEX ix_clausulas_acto ON clausulas_template (acto, tipo, activo);

-- Facultades: se cargan todas con sus nombres reales; las 4 de prueba (V0002) se actualizan.
INSERT INTO clausulas_template (codigo, nombre, tipo, categoria, texto_template, aplicable_a, orden) VALUES
('admin_bienes',                     'Administrar bienes',                          'facultad', 'administracion',   '[Texto de prueba] administrar bienes',                            ARRAY['general','administrativo'], 10),
('vender_muebles',                   'Adquisicion y enajenacion de bienes',         'facultad', 'bienes',           '[Texto de prueba] adquirir y enajenar bienes',                    ARRAY['general','especial'],       20),
('otorgar_escrituras',               'Otorgar escrituras',                          'facultad', 'inmuebles',        '[Texto de prueba] otorgar y firmar escrituras',                   ARRAY['general','especial'],       30),
('Locacion',                         'Locacion',                                    'facultad', 'inmuebles',        '[Texto de prueba] dar y tomar en locación',                       ARRAY['general','especial'],       40),
('hipotecar',                        'Gravámenes',                                  'facultad', 'inmuebles',        '[Texto de prueba] constituir hipotecas y gravámenes',             ARRAY['general','especial'],       50),
('transferir_automotor',             'Transferir automotor',                        'facultad', 'automotor',        '[Texto de prueba] transferir automotores',                        ARRAY['general','especial'],       60),
('registro_embarcaciones',           'Registro de embarcaciones',                   'facultad', 'embarcaciones',    '[Texto de prueba] trámites ante el registro de embarcaciones',    ARRAY['especial'],                 70),
('operar_cuentas',                   'Depositos',                                   'facultad', 'bancario',         '[Texto de prueba] depositar y extraer fondos',                    ARRAY['general','bancario'],       80),
('Operaciones_bancarias',            'Operaciones bancarias',                       'facultad', 'bancario',         '[Texto de prueba] realizar operaciones bancarias',                ARRAY['general','bancario'],       90),
('cobrar_creditos',                  'Cobrar y percibir',                           'facultad', 'cobros_pagos',     '[Texto de prueba] cobrar y percibir',                             ARRAY['general','bancario','especial'], 100),
('pagar_deudas',                     'Pagar deudas',                                'facultad', 'cobros_pagos',     '[Texto de prueba] pagar deudas',                                  ARRAY['general','especial'],      110),
('gestiones_previsionales',          'Cobrar jubilaciones y pensiones',             'facultad', 'previsional',      '[Texto de prueba] cobrar jubilaciones y pensiones',               ARRAY['especial','administrativo'], 120),
('contestar_demandas',               'Intervencion en Juicios',                     'facultad', 'judicial',         '[Texto de prueba] intervenir en juicios',                         ARRAY['general','judicial'],      130),
('representar_organismos',           'Representar ante organismos',                 'facultad', 'administrativo',   '[Texto de prueba] representar ante organismos públicos',          ARRAY['general','administrativo','especial'], 140),
('Aceptacion_herencia',              'Aceptacion de herencia',                      'facultad', 'sucesiones',       '[Texto de prueba] aceptar herencias',                             ARRAY['especial','judicial'],     150),
('gestiones_sucesorias',             'Gestiones sucesorias',                        'facultad', 'sucesiones',       '[Texto de prueba] realizar gestiones sucesorias',                 ARRAY['especial','judicial'],     160),
('gestiones_sepelio',                'Gestiones de sepelio',                        'facultad', 'sepelio',          '[Texto de prueba] realizar gestiones de sepelio',                 ARRAY['especial'],                170),
('gestiones_agropecuarias',          'Gestiones agropecuarias y cereales',          'facultad', 'agropecuario',     '[Texto de prueba] realizar gestiones agropecuarias',              ARRAY['especial'],                180),
('comercio_exterior',                'Comercio exterior',                           'facultad', 'comercio_exterior','[Texto de prueba] operar en comercio exterior',                   ARRAY['especial'],                190),
('gestiones_exterior',               'Gestiones en el exterior',                    'facultad', 'exterior',         '[Texto de prueba] realizar gestiones en el exterior',             ARRAY['especial'],                200),
('constituir_sociedad',              'Sociedades y fideicomisos',                   'facultad', 'societario',       '[Texto de prueba] constituir sociedades y fideicomisos',          ARRAY['especial'],                210),
('Conferir_poderes_otros_juridicos', 'Conferir poderes y otros actos juridicos',    'facultad', 'poderes',          '[Texto de prueba] conferir poderes',                              ARRAY['general','especial'],      220),
('revocar_poderes',                  'Revocar poderes',                             'facultad', 'poderes',          '[Texto de prueba] revocar poderes',                               ARRAY['general','especial'],      230),
('sustituir_poder',                  'Sustituir poder',                             'facultad', 'poderes',          '[Texto de prueba] sustituir poderes',                             ARRAY['general','especial'],      240),
('Otros_actos_juridicos',            'Otros actos juridicos',                       'facultad', 'otros',            '[Texto de prueba] otros actos jurídicos',                         ARRAY['general','especial'],      250),
('Poder_especial',                   'Poder especial',                              'facultad', 'otros',            '[Texto de prueba] poder especial',                                ARRAY['especial'],                260)
ON CONFLICT (codigo) DO UPDATE SET
    nombre      = EXCLUDED.nombre,
    categoria   = EXCLUDED.categoria,
    aplicable_a = EXCLUDED.aplicable_a,
    orden       = EXCLUDED.orden;
