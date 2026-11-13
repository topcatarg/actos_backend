using actos_backend.Datos;
using actos_backend.Ia;
using actos_backend.Migraciones;
using actos_backend.Asistente;
using actos_backend.Poderes;
using actos_backend.Tramites;
using Npgsql;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Log: consola + archivo diario en logs/ (configuración en la sección "Serilog" de appsettings).
builder.Host.UseSerilog((context, services, log) => log
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// IA: la configuración se valida al arrancar; si falta la API key, la app no levanta y dice por qué.
builder.Services.AddOptions<IaOptions>()
    .Bind(builder.Configuration.GetSection(IaOptions.Seccion))
    .ValidateDataAnnotations()
    .ValidateOnStart();
// Modelo en uso: sale de la tabla ia_modelos (se elige desde el front); si no, el de appsettings.
builder.Services.AddScoped<IIaModelosRepositorio, IaModelosRepositorio>();
builder.Services.AddSingleton<ModeloIaActual>();
// Conexión al proveedor: prueba IPv6/IPv4 con límite corto por intento (ver ConectorHttp).
builder.Services.AddSingleton<ConectorHttp>();
builder.Services.AddHttpClient<IIaClient, OpenAiCompatibleClient>()
    .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
    {
        ConnectCallback = sp.GetRequiredService<ConectorHttp>().ConectarAsync,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    });

// Postgres: un único NpgsqlDataSource para toda la app (maneja el pool de conexiones).
var connectionString = builder.Configuration.GetConnectionString("Actos")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:Actos en la configuración.");
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));

// Dapper: mapear columnas snake_case (aplicable_a) a propiedades PascalCase (AplicableA).
Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
builder.Services.AddScoped<ICatalogoRepositorio, CatalogoRepositorio>();
builder.Services.AddScoped<IPersonasRepositorio, PersonasRepositorio>();
builder.Services.AddScoped<ITramitesRepositorio, TramitesRepositorio>();
builder.Services.AddScoped<IActosRepositorio, ActosRepositorio>();
builder.Services.AddScoped<IRegistroPersonasRepositorio, RegistroPersonasRepositorio>();
builder.Services.AddScoped<actos_backend.Registro.GuardadoPersonas>();

// Migraciones del esquema (scripts embebidos en Migraciones/Scripts). Se corren a pedido: POST /api/db/migrar
builder.Services.AddScoped<MigradorBaseDatos>();

// Poderes
builder.Services.AddScoped<PoderExtractor>();
builder.Services.AddScoped<PoderCompletador>();
builder.Services.AddScoped<RedactorPreguntas>();
builder.Services.AddScoped<InterpreteRespuestas>();
builder.Services.AddScoped<ConversacionPoder>();

// Trámites y asistente (entrada en texto libre)
builder.Services.AddScoped<TramitesServicio>();
builder.Services.AddScoped<ClasificadorIntencion>();

// CORS solo para desarrollo: el front (Vite) corre en otro puerto.
// Con el proxy de Vite no haría falta, pero así funciona también llamando directo al back.
const string CorsDesarrollo = "FrontDesarrollo";
builder.Services.AddCors(o => o.AddPolicy(CorsDesarrollo, p => p
    .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// Una línea por pedido HTTP: método, ruta, status y duración.
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors(CorsDesarrollo);
}
else
{
    // En desarrollo NO: si el back redirige a https://localhost:7164, el pedido que vino por el
    // proxy de Vite termina en otro origen y el navegador lo corta por CORS.
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
