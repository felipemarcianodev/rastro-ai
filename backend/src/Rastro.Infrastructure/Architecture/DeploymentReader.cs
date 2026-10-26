using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Rastro.Core.Architecture;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Rastro.Infrastructure.Architecture;

// Lê o docker-compose do repositório analisado para o C2 (Fase 13): serviço com "build" vira
// container de aplicação, com o projeto publicado pelo Dockerfile e as referências dele; serviço
// de banco (postgres, mysql, sql server, mongo) dá nome ao banco. Só lê arquivos dentro da raiz do
// repositório (caminho que sai dela é ignorado). Sem compose legível, devolve null e o C2 continua
// como antes (um container por projeto).
public partial class DeploymentReader(ILogger<DeploymentReader> logger)
{
    private static readonly string[] ComposeNames = ["docker-compose.yml", "docker-compose.yaml", "compose.yml", "compose.yaml"];
    private static readonly string[] ComposeFolders = ["", "docker", "deploy"];
    private static readonly string[] DatabaseImages = ["postgres", "mysql", "mariadb", "mssql", "sqlserver", "mongo"];

    [GeneratedRegex(@"[\w./\\-]+\.csproj", RegexOptions.IgnoreCase)]
    private static partial Regex CsprojInText();

    public DeploymentMap? Read(string repositoryRoot)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var compose = ComposeFolders.SelectMany(folder => ComposeNames.Select(name => Path.Combine(root, folder, name))).FirstOrDefault(File.Exists);
        if (compose is null) return null;

        try
        {
            var yaml = new YamlStream();
            using (var reader = new StreamReader(compose)) yaml.Load(reader);
            if (yaml.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode document
                || !document.Children.TryGetValue(new YamlScalarNode("services"), out var servicesNode)
                || servicesNode is not YamlMappingNode services)
                return null;

            var composeDir = Path.GetDirectoryName(compose)!;
            var applications = new List<DeployedService>();
            string? database = null;
            foreach (var (key, value) in services.Children)
            {
                if (key is not YamlScalarNode { Value: { } name } || value is not YamlMappingNode service) continue;
                var image = Scalar(service, "image") ?? "";
                if (database is null && DatabaseImages.Any(d => image.Contains(d, StringComparison.OrdinalIgnoreCase)))
                {
                    database = $"{name} ({image})";
                    continue;
                }
                if (Build(service, composeDir, root) is not { } dockerfile) continue;
                var projects = ProjectsOf(dockerfile, root);
                if (projects.Count > 0) applications.Add(new DeployedService(name, projects, ".NET"));
            }
            return applications.Count == 0 ? null : new DeploymentMap(applications, database);
        }
        catch (Exception ex) when (ex is YamlException or IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            logger.LogWarning(ex, "docker-compose do repositório não pôde ser lido; o C2 fica por projeto.");
            return null;
        }
    }

    // Dockerfile do serviço: "build: pasta" ou "build: { context, dockerfile }", dentro da raiz.
    private static string? Build(YamlMappingNode service, string composeDir, string root)
    {
        if (!service.Children.TryGetValue(new YamlScalarNode("build"), out var build)) return null;
        string context;
        var dockerfile = "Dockerfile";
        if (build is YamlScalarNode { Value: { } path }) context = path;
        else if (build is YamlMappingNode map)
        {
            context = Scalar(map, "context") ?? ".";
            dockerfile = Scalar(map, "dockerfile") ?? dockerfile;
        }
        else return null;

        var full = Path.GetFullPath(Path.Combine(composeDir, context, dockerfile));
        return Inside(full, root) && File.Exists(full) ? full : null;
    }

    // Projeto publicado: o .csproj ao lado do Dockerfile ou o citado nele; mais as referências.
    private static IReadOnlyCollection<string> ProjectsOf(string dockerfile, string root)
    {
        var folder = Path.GetDirectoryName(dockerfile)!;
        var entry = Directory.GetFiles(folder, "*.csproj").FirstOrDefault();
        if (entry is null)
        {
            var cited = CsprojInText().Matches(File.ReadAllText(dockerfile)).Select(m => m.Value.Replace('\\', '/')).LastOrDefault();
            if (cited is not null)
            {
                var name = Path.GetFileName(cited);
                entry = Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).FirstOrDefault(f => Inside(f, root));
            }
        }
        if (entry is null) return [];

        var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>([entry]);
        while (queue.Count > 0)
        {
            var csproj = queue.Dequeue();
            if (!projects.Add(Path.GetFileNameWithoutExtension(csproj))) continue;
            foreach (var reference in XDocument.Load(csproj).Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")?.Value;
                if (include is null) continue;
                var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(csproj)!, include.Replace('\\', Path.DirectorySeparatorChar)));
                if (Inside(path, root) && File.Exists(path)) queue.Enqueue(path);
            }
        }
        return projects;
    }

    private static string? Scalar(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode { Value: { } value } ? value : null;

    private static bool Inside(string path, string root) =>
        Path.GetFullPath(path).StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
