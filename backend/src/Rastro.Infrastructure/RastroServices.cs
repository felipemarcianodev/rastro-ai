using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rastro.Infrastructure.Analysis;
using Rastro.Infrastructure.Configuration;
using Rastro.Infrastructure.Processes;
using Rastro.Infrastructure.Proposals;
using Rastro.Infrastructure.Repositories;
using Rastro.Infrastructure.Storage;
using Rastro.Infrastructure.Validation;

namespace Rastro.Infrastructure;

// Registro comum à API e ao servidor MCP: os dois hospedeiros usam exatamente o mesmo núcleo.
// Quem hospeda registra o MSBuildLocator antes de abrir qualquer solution.
public static class RastroServices
{
    public static IServiceCollection AddRastroCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RastroOptions>(configuration.GetSection(RastroOptions.Section));
        services.AddDbContext<RastroDbContext>(options =>
            RastroDbContext.Configure(options, RastroDatabase.ConnectionString(configuration)));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<RepositoryCatalog>();
        services.AddSingleton(_ => new RepositoryConfigFile(RastroHome.ConfigPath));
        services.AddSingleton<IGitInfo, GitCli>();
        services.AddSingleton<ISolutionSource, MsBuildSolutionSource>();
        services.AddSingleton<ISolutionAnalyzer, SolutionAnalyzer>();
        services.AddScoped<RouteIndexStore>();
        services.AddScoped<CrossSystemAnalyzer>();
        services.AddScoped<ProposalService>();
        services.AddScoped<ChangeValidationService>();
        services.AddSingleton<Architecture.DeploymentReader>();
        services.AddSingleton<IValidationCommandRunner, LocalCommandRunner>();
        return services;
    }
}
