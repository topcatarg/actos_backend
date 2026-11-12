using actos_backend.Datos;
using actos_backend.Ia;
using actos_backend.Poderes;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// IA: la configuración se valida al arrancar; si falta la API key, la app no levanta y dice por qué.
builder.Services.AddOptions<IaOptions>()
    .Bind(builder.Configuration.GetSection(IaOptions.Seccion))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHttpClient<IIaClient, OpenAiCompatibleClient>();

// Postgres: un único NpgsqlDataSource para toda la app (maneja el pool de conexiones).
var connectionString = builder.Configuration.GetConnectionString("Actos")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:Actos en la configuración.");
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));

// Dapper: mapear columnas snake_case (aplicable_a) a propiedades PascalCase (AplicableA).
Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
builder.Services.AddScoped<ICatalogoRepositorio, CatalogoRepositorio>();

// Poderes
builder.Services.AddScoped<PoderExtractor>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
