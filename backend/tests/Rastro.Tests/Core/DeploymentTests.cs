using Microsoft.Extensions.Logging.Abstractions;
using Rastro.Core;
using Rastro.Core.Architecture;
using Rastro.Core.Graph;
using Rastro.Core.Prompts;
using Rastro.Infrastructure.Architecture;

namespace Rastro.Tests.Core;

// Cenários C4-DEPLOY-NNN: C2 a partir do docker-compose do repositório (Fase 13).
public class DeploymentTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("rastro-deploy-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private void Repository()
    {
        Write("docker/docker-compose.yml", """
            services:
              db:
                image: postgres:16-alpine
              api:
                build:
                  context: ../backend
                  dockerfile: src/Shop.Api/Dockerfile
              worker:
                build:
                  context: ../backend
                  dockerfile: worker.Dockerfile
              fora:
                build:
                  context: ../../fora-do-repositorio
            """);
        Write("backend/src/Shop.Api/Dockerfile", "FROM mcr.microsoft.com/dotnet/aspnet:10.0");
        Write("backend/src/Shop.Api/Shop.Api.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup><ProjectReference Include="..\Shop.Domain\Shop.Domain.csproj" /></ItemGroup>
            </Project>
            """);
        Write("backend/src/Shop.Domain/Shop.Domain.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Write("backend/worker.Dockerfile", "RUN dotnet publish src/Shop.Worker/Shop.Worker.csproj -c Release");
        Write("backend/src/Shop.Worker/Shop.Worker.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Worker">
              <ItemGroup><ProjectReference Include="../Shop.Domain/Shop.Domain.csproj" /></ItemGroup>
            </Project>
            """);
    }

    [Fact] // C4-DEPLOY-001
    public void Read_ComposeComBuildEBanco_ServicosComOsProjetosEOBanco()
    {
        Repository();

        var map = new DeploymentReader(NullLogger<DeploymentReader>.Instance).Read(_root)!;

        Assert.Equal(["api", "worker"], map.Applications.Select(a => a.Name));
        Assert.Equal(["Shop.Api", "Shop.Domain"], map.Applications[0].Projects.Order());
        Assert.Equal(["Shop.Domain", "Shop.Worker"], map.Applications[1].Projects.Order());
        Assert.Equal("db (postgres:16-alpine)", map.DatabaseService);
        Assert.Equal("api", map.ServiceOf("Shop.Domain")!.Name);
    }

    [Fact] // C4-DEPLOY-002
    public void Read_SemCompose_DevolveNull()
    {
        Assert.Null(new DeploymentReader(NullLogger<DeploymentReader>.Instance).Read(_root));
    }

    [Fact] // C4-DEPLOY-003
    public void Build_ComMapa_ContainerEhOServicoEOBancoTemONomeDoCompose()
    {
        var graph = new FlowGraph(RastroSchema.Version, "OrderController.Post", "m:post",
            [
                new FlowNode("m:post", NodeKind.Entry, "OrderController.Post", Project: "Shop.Api"),
                new FlowNode("m:save", NodeKind.Method, "OrderRepository.Save", Project: "Shop.Domain"),
            ],
            [new FlowEdge("m:post", "m:save", 1, EdgeConfidence.Extracted)]);
        var map = new DeploymentMap([new DeployedService("api", ["Shop.Api", "Shop.Domain"], ".NET")], "db (postgres:16-alpine)");

        var dsl = C4WorkspaceBuilder.Build(new PromptContext("shop", "Shop.slnx", "OrderController.Post", null), graph, [], map);

        Assert.Contains("cont1 = container \"api\" \"Serviço do docker-compose com Shop.Api, Shop.Domain.\"", dsl);
        Assert.DoesNotContain("container \"Shop.Domain\"", dsl);
        Assert.Contains("db = container \"db (postgres:16-alpine)\"", dsl);
    }
}
