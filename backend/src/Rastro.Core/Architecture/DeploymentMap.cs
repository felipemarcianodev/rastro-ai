namespace Rastro.Core.Architecture;

// O que roda separado de verdade, lido do docker-compose do repositório (Fase 13). Cada serviço
// de aplicação diz quais projetos (assemblies) vão dentro dele; o banco vem do serviço de banco.
public record DeployedService(string Name, IReadOnlyCollection<string> Projects, string Technology);

public record DeploymentMap(IReadOnlyList<DeployedService> Applications, string? DatabaseService)
{
    // Serviço onde o projeto roda; o primeiro que o declara (API antes de worker, na ordem do compose).
    public DeployedService? ServiceOf(string project) =>
        Applications.FirstOrDefault(s => s.Projects.Contains(project, StringComparer.OrdinalIgnoreCase));
}
