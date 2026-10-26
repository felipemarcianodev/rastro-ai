using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rastro.Core;
using Rastro.Infrastructure;
using Rastro.Infrastructure.Storage;

namespace Rastro.Mcp;

// Servidor MCP do Rastro (modo agente, ADR-001), via stdio: "rastro mcp". O stdout é o canal do
// protocolo, então todo log vai para o stderr. Quem chama já registrou o MSBuildLocator.
public static class McpHost
{
    public static async Task RunAsync(string[] args)
    {
        // Mesmo ambiente da API: o "dotnet run" do desenvolvimento traz ASPNETCORE_ENVIRONMENT=Development.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                              ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                              ?? Environments.Production
        });
        // Desenvolvimento: o mesmo user-secrets da API. No pacote, só a pasta do usuário (~/.rastro).
        if (builder.Environment.IsDevelopment())
            builder.Configuration.AddUserSecrets(typeof(McpHost).Assembly, optional: true);
        builder.Configuration.AddRastroHome();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

        builder.Services.AddRastroCore(builder.Configuration);
        builder.Services
            .AddMcpServer(o =>
            {
                o.ServerInfo = new() { Name = "rastro", Version = RastroVersion.Current };
                o.ServerInstructions = McpInstructions.Text;
            })
            .WithStdioServerTransport()
            .WithTools<RastroTools>();

        var host = builder.Build();

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();
            await db.Database.MigrateAsync();
            await RastroDatabase.UseWriteAheadLogAsync(db);
        }

        await host.RunAsync();
    }
}
