using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Connections;
using Microsoft.Build.Locator;
using Microsoft.EntityFrameworkCore;
using Rastro.Ai;
using Rastro.Api.Analysis;
using Rastro.Api.Cli;
using Rastro.Core;
using Rastro.Infrastructure;
using Rastro.Infrastructure.Database.Live;
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
int? packagePort = string.IsNullOrWhiteSpace(builder.Configuration["urls"])
    ? builder.Configuration.GetValue("porta", RastroCommandLine.DefaultPort)
    : null;
if (packagePort is not null)
    builder.WebHost.UseUrls($"http://127.0.0.1:{packagePort}");

builder.Services.AddRastroCore(builder.Configuration);
// Modo provedor da IA (ADR-001): só a API registra; o núcleo e o MCP não conhecem o Rastro.Ai.
builder.Services.AddRastroAi(builder.Configuration);
// Banco vivo (Fase 40b, ADR-006): só leitura, cadastrado pela tela, com a string cifrada.
builder.Services.AddRastroLiveDatabase();
builder.Services.AddSingleton<SurveyQueue>();
builder.Services.AddSingleton<RastroMetrics>();
builder.Services.AddHostedService<SurveyWorker>();
builder.Services.AddSingleton<ValidationQueue>();
builder.Services.AddHostedService<ValidationWorker>();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddOpenApi();

var app = builder.Build();

// Porta ocupada: nem migra o banco, que pode ser o do Rastro já aberto.
if (standalone && packagePort is int port)
{
    var check = await PortProbe.CheckAsync(port, app.Logger);
    if (check.State != PortState.Free)
        return PortProbe.Report(check, port, Console.Out, Console.Error);
}

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

try
{
    await app.RunAsync();
}
catch (IOException ex) when (ex.InnerException is AddressInUseException)
{
    // A porta foi tomada depois da conferência, ou o endereço veio da configuração (--urls), que não passa por ela.
    // O detalhe vai para o stderr porque o app.Logger não serve aqui: o RunAsync já descartou o host, e escrever nele estoura ObjectDisposedException.
    Console.Error.WriteLine("O endereço do Rastro já está em uso. Se for outro Rastro, use a tela dele; se não, escolha outra porta: rastro --porta N");
    Console.Error.WriteLine($"Detalhe: {ex.Message}");
    return 1;
}
return 0;

// Exposto para os testes de integração (WebApplicationFactory).
public partial class Program;
