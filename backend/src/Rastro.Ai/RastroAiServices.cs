using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Ai.Providers;
using Rastro.Ai.Rules;
using Rastro.Ai.Settings;
using Rastro.Infrastructure.Storage;

namespace Rastro.Ai;

// Registro do modo provedor (ADR-001). Só a API chama isto; o núcleo e o MCP não conhecem o Rastro.Ai.
public static class RastroAiServices
{
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromMinutes(3);

    public static IServiceCollection AddRastroAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AiDbContext>(options =>
            AiDbContext.Configure(options, RastroDatabase.ConnectionString(configuration)));

        // Chaves do DataProtection no banco: a chave de API cifrada continua legível depois de reiniciar.
        // No Windows, as próprias chaves ficam cifradas com o DPAPI do usuário: copiar o rastro.db para
        // outra máquina não abre a chave de API.
        var dataProtection = services.AddDataProtection().SetApplicationName("Rastro").PersistKeysToDbContext<AiDbContext>();
        if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();

        services.AddHttpClient(ClaudeTextService.HttpClientName, c => { c.BaseAddress = new Uri("https://api.anthropic.com/"); c.Timeout = ProviderTimeout; });
        services.AddHttpClient(OpenAiTextService.HttpClientName, c => { c.BaseAddress = new Uri("https://api.openai.com/"); c.Timeout = ProviderTimeout; });
        services.AddHttpClient(GeminiTextService.HttpClientName, c => { c.BaseAddress = new Uri("https://generativelanguage.googleapis.com/"); c.Timeout = ProviderTimeout; });

        services.AddScoped<IAiTextService, ClaudeTextService>();
        services.AddScoped<IAiTextService, OpenAiTextService>();
        services.AddScoped<IAiTextService, GeminiTextService>();
        services.AddScoped<AiSettingsService>();
        services.AddScoped<AiGateway>();
        services.AddScoped<HoleFiller>();
        services.AddScoped<RuleWriter>();
        services.AddScoped<Suggestions.RefactorAdvisor>();
        services.AddScoped<Suggestions.ContextAdvisor>();
        return services;
    }

    public static async Task MigrateRastroAiAsync(this IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AiDbContext>().Database.MigrateAsync();
    }
}
