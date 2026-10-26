using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.Build.Locator;
using Microsoft.EntityFrameworkCore;
using Rastro.Ai;
using Rastro.Api.Analysis;
using Rastro.Api.Cli;
using Rastro.Core;
using Rastro.Infrastructure;
using Rastro.Infrastructure.Storage;

// Acentos no terminal (no Windows o padrão é a página de código do console).
Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

// O MSBuild do SDK instalado precisa ser registrado antes de qualquer análise carregar uma solution.
if (!MSBuildLocator.IsRegistered && MSBuildLocator.CanRegister)
    MSBuildLocator.RegisterDefaults();

// "rastro mcp", "rastro repo"... (ADR-002). Sem comando, sobe a tela.
if (RastroCommandLine.IsCommand(args))
    return await RastroCommandLine.RunAsync(args);

// Rodando como programa (o "rastro" instalado ou o dotnet run), e não dentro do WebApplicationFactory dos testes.
var standalone = Assembly.GetEntryAssembly() == typeof(Program).Assembly;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // O "rastro" roda de qualquer pasta: a tela embutida (wwwroot) e o appsettings ficam ao lado do executável.
    ContentRootPath = standalone ? AppContext.BaseDirectory : null
});
builder.Configuration.AddRastroHome();

// Sem endereço configurado (o pacote), só nesta máquina: a porta padrão ou a do --porta.
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
    builder.WebHost.UseUrls($"http://127.0.0.1:{builder.Configuration.GetValue("porta", RastroCommandLine.DefaultPort)}");

builder.Services.AddRastroCore(builder.Configuration);
// Modo provedor da IA (ADR-001): só a API registra; o núcleo e o MCP não conhecem o Rastro.Ai.
builder.Services.AddRastroAi(builder.Configuration);
builder.Services.AddSingleton<SurveyQueue>();
builder.Services.AddSingleton<RastroMetrics>();
builder.Services.AddHostedService<SurveyWorker>();
builder.Services.AddSingleton<ValidationQueue>();
builder.Services.AddHostedService<ValidationWorker>();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Configuration.GetValue("Rastro:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();
    await db.Database.MigrateAsync();
    await RastroDatabase.UseWriteAheadLogAsync(db);
    await app.Services.MigrateRastroAiAsync();
}

// Host filtering (AllowedHosts) já entra sozinho no WebApplication; o Origin é verificado aqui (ADR-003).
app.UseMiddleware<Rastro.Api.OriginGuardMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();

// Tela embutida no pacote (o build do Angular vai para o wwwroot). No desenvolvimento não há wwwroot:
// a tela vem do ng serve, com proxy para a API.
if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? string.Empty, "index.html")))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallback("api/{**rest}", () => Results.NotFound());
    app.MapFallbackToFile("index.html");
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        Console.WriteLine($"Rastro {RastroVersion.Current} aberto em {string.Join(", ", app.Urls)}");
        Console.WriteLine($"Dados em {RastroHome.Folder}. Ctrl+C para sair.");
    });
}

await app.RunAsync();
return 0;

// Exposto para os testes de integração (WebApplicationFactory).
public partial class Program;
