-- V0002 · Datos de prueba INVENTADOS. Ninguna persona es real.

-- Facultades (códigos reales del sistema; textos inventados)
INSERT INTO clausulas_template (codigo, nombre, tipo, categoria, texto_template, aplicable_a, orden) VALUES
('otorgar_escrituras', 'Otorgar escrituras', 'facultad', 'inmuebles',
 'otorgar y firmar las escrituras públicas y demás instrumentos públicos o privados que fueren necesarios',
 ARRAY['general','especial'], 10),
('vender_muebles', 'Vender bienes muebles', 'facultad', 'muebles',
 'vender bienes muebles, percibir su precio y otorgar los recibos correspondientes',
 ARRAY['general'], 20),
('operar_cuentas', 'Operar cuentas bancarias', 'facultad', 'bancario',
 'operar en cuentas bancarias, depositar y extraer fondos, librar y endosar cheques',
 ARRAY['general','bancario'], 30),
('transferir_automotor', 'Transferir automotores', 'facultad', 'automotor',
 'transferir automotores, firmar los formularios registrales y realizar todo trámite ante el registro correspondiente',
 ARRAY['general','especial'], 40);

-- Condiciones (códigos reales del sistema; textos inventados)
INSERT INTO clausulas_template (codigo, nombre, tipo, categoria, texto_template, orden) VALUES
('sustitucion_sin',          'Sin facultad de sustituir',        'condicion', 'sustitucion',   'El presente poder no podrá ser sustituido.', 1),
('sustitucion_con',          'Con facultad de sustituir',        'condicion', 'sustitucion',   'El apoderado podrá sustituir el presente poder en todo o en parte.', 2),
('sustitucion_autorizacion', 'Sustitución con autorización',     'condicion', 'sustitucion',   'El apoderado podrá sustituir el presente poder con autorización expresa del otorgante.', 3),
('vigencia_indefinida',      'Vigencia indefinida',              'condicion', 'vigencia',      'El presente poder tendrá vigencia por tiempo indeterminado.', 1),
('vigencia_fecha',           'Vigencia hasta una fecha',         'condicion', 'vigencia',      'El presente poder tendrá vigencia hasta el {{fecha_vigencia}}.', 2),
('revocable',                'Revocable',                        'condicion', 'revocabilidad', 'El presente poder es revocable a voluntad del otorgante.', 1),
('irrevocable',              'Irrevocable',                      'condicion', 'revocabilidad', 'El presente poder se otorga con carácter irrevocable.', 2);

-- Personas físicas inventadas
INSERT INTO pf_maestro (nombre_completo, dni, cuit_cuil, fecha_nacimiento, nacionalidad, padre, madre, genero, estado_validacion) VALUES
('Juan Carlos Pérez',      '20123456', '20-20123456-3', '1968-04-12', 'argentina', 'Carlos Pérez',   'Ana Rodríguez', 'M', 'PRUEBA'),
('María Laura Gómez',      '27654321', '27-27654321-4', '1979-09-03', 'argentina', 'Luis Gómez',     'Elena Díaz',    'F', 'PRUEBA'),
('Roberto Daniel Sánchez', '18456789', '20-18456789-1', '1962-01-25', 'argentina', NULL,             NULL,            'M', 'PRUEBA'),
('Lucía Fernández',        '33222111', '27-33222111-8', '1988-11-30', 'uruguaya',  'Jorge Fernández', 'Marta Silva',  'F', 'PRUEBA');

INSERT INTO pf_domicilios (pf_id, direccion, ciudad, provincia, desde_fecha)
SELECT pf_id, d.direccion, d.ciudad, d.provincia, d.desde
FROM pf_maestro
JOIN (VALUES
    ('20123456', 'Calle Falsa 123',           'Mar del Plata',      'Buenos Aires', DATE '2010-01-01'),
    ('20123456', 'Av. Siempreviva 742',       'Mar del Plata',      'Buenos Aires', DATE '2021-06-01'),  -- vigente
    ('27654321', 'Rivadavia 1500, piso 3 B',  'Ciudad de Buenos Aires', 'CABA',     DATE '2018-03-15'),
    ('18456789', 'Colón 2200',                'Tandil',             'Buenos Aires', DATE '2005-08-20')
) AS d(dni, direccion, ciudad, provincia, desde) ON d.dni = pf_maestro.dni;

INSERT INTO pf_estado_civil (pf_id, estado_civil, conyuge_nombre, desde_fecha)
SELECT pf_id, e.estado, e.conyuge, e.desde
FROM pf_maestro
JOIN (VALUES
    ('20123456', 'casado',     'Silvia Martínez', DATE '1995-05-20'),
    ('27654321', 'soltera',    NULL,              DATE '1979-09-03'),
    ('18456789', 'divorciado', NULL,              DATE '2015-02-10')
) AS e(dni, estado, conyuge, desde) ON e.dni = pf_maestro.dni;
-- Lucía Fernández queda sin domicilio ni estado civil a propósito, para probar faltantes.
