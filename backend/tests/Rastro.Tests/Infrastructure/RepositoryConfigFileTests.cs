using System.Text.Json.Nodes;
using Rastro.Infrastructure.Repositories;

namespace Rastro.Tests.Infrastructure;

public class RepositoryConfigFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rastro-config-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact] // INFRA-CFG-001
    public void Add_DoisRepositorios_GravaOConfigInteiroSemDeixarArquivoTemporario()
    {
        var home = Path.Combine(_folder, "home");
        var repoA = Directory.CreateDirectory(Path.Combine(_folder, "a")).FullName;
        var repoB = Directory.CreateDirectory(Path.Combine(_folder, "b")).FullName;
        var configPath = Path.Combine(home, "config.json");
        var config = new RepositoryConfigFile(configPath);

        config.Add("a", repoA);
        config.Add("b", repoB);

        // A gravação passa por um arquivo ao lado e troca de uma vez: no fim só fica o config.json, válido e completo.
        Assert.Equal(["config.json"], Directory.GetFiles(home).Select(Path.GetFileName));
        Assert.NotNull(JsonNode.Parse(File.ReadAllText(configPath)));
        Assert.Equal(["a", "b"], new RepositoryConfigFile(configPath).List().Select(r => r.Name).Order());
    }
}
