using System.Text;
using System.Text.RegularExpressions;

namespace Rastro.Core.Links;

// Onde o repositório mora, lido do remote do git. Valores explícitos: viajam como int no JSON.
public enum RepositoryHostKind
{
    Unknown = 0,
    AzureDevOps = 1,
    GitHub = 2,
    GitLab = 3,
    Bitbucket = 4
}

// Modelos de link de um repositório (Fase 56, ADR-010), com os marcadores {path} e {line} a preencher pela tela.
// CommitTemplate aponta para o commit do levantamento (estável); BranchTemplate, para a branch (alternativa).
// Sem remote reconhecido, os dois ficam nulos e só o editor vale.
public sealed record RepositoryLinks(
    string Repository,
    RepositoryHostKind Host,
    string? CommitTemplate,
    string? BranchTemplate,
    string? EditorTemplate);

// Remote reconhecido, já sem usuário, senha ou token: o que vai para a tela nunca leva credencial.
public sealed record RemoteRepository(RepositoryHostKind Host, string BaseUrl, IReadOnlyList<string> Parts);

// Monta os links para o host e para o editor sem I/O: quem lê o remote e a pasta é a infraestrutura.
public static class CodeLinks
{
    public const string PathToken = "{path}";
    public const string LineToken = "{line}";

    private const string AzureHost = "dev.azure.com";
    private const string AzureSshHost = "ssh.dev.azure.com";
    private const string AzureLegacySuffix = ".visualstudio.com";
    private const string GitHubHost = "github.com";
    private const string GitLabHost = "gitlab.com";
    private const string BitbucketHost = "bitbucket.org";
    private const string GitSuffix = ".git";
    private const string AzureGitSegment = "_git";
    private const string VsCodeTemplate = "vscode://file/{root}/{path}:{line}";
    private const string RootToken = "{root}";

    // git@host:caminho (SCP) e ssh://git@host[:porta]/caminho.
    private static readonly Regex ScpLike = new(@"^(?:[\w.-]+@)?(?<host>[\w.-]+):(?!//)(?<path>.+)$", RegexOptions.CultureInvariant);

    // Lê o remote (HTTPS, SSH ou SCP) e devolve o host e as partes do caminho. Nulo quando não reconhece.
    public static RemoteRepository? ParseRemote(string? remote)
    {
        if (string.IsNullOrWhiteSpace(remote)) return null;
        var text = remote.Trim();

        string host;
        string path;
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "ssh")
        {
            // A parte de usuário (e de token) some aqui: nunca entra no link.
            host = uri.Host.ToLowerInvariant();
            path = Uri.UnescapeDataString(uri.AbsolutePath);
        }
        else if (ScpLike.Match(text) is { Success: true } scp)
        {
            host = scp.Groups["host"].Value.ToLowerInvariant();
            path = scp.Groups["path"].Value;
        }
        else
        {
            return null;
        }

        if (path.EndsWith(GitSuffix, StringComparison.OrdinalIgnoreCase)) path = path[..^GitSuffix.Length];
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return host switch
        {
            GitHubHost when parts.Length == 2 => new(RepositoryHostKind.GitHub, $"https://{GitHubHost}", parts),
            GitLabHost when parts.Length >= 2 => new(RepositoryHostKind.GitLab, $"https://{GitLabHost}", parts),
            BitbucketHost when parts.Length == 2 => new(RepositoryHostKind.Bitbucket, $"https://{BitbucketHost}", parts),
            AzureHost => AzureHttps(parts),
            AzureSshHost => AzureSsh(parts),
            _ when host.EndsWith(AzureLegacySuffix, StringComparison.Ordinal) => AzureLegacy(host, parts),
            _ => null
        };
    }

    // https://dev.azure.com/{org}/{projeto}/_git/{repo}
    private static RemoteRepository? AzureHttps(string[] parts) =>
        parts is [var org, var project, AzureGitSegment, var repo]
            ? new(RepositoryHostKind.AzureDevOps, $"https://{AzureHost}", [org, project, repo])
            : null;

    // git@ssh.dev.azure.com:v3/{org}/{projeto}/{repo}
    private static RemoteRepository? AzureSsh(string[] parts) =>
        parts is ["v3", var org, var project, var repo]
            ? new(RepositoryHostKind.AzureDevOps, $"https://{AzureHost}", [org, project, repo])
            : null;

    // https://{org}.visualstudio.com/{projeto}/_git/{repo}, o endereço antigo do Azure DevOps.
    private static RemoteRepository? AzureLegacy(string host, string[] parts) =>
        parts is [var project, AzureGitSegment, var repo]
            ? new(RepositoryHostKind.AzureDevOps, $"https://{AzureHost}", [host[..^AzureLegacySuffix.Length], project, repo])
            : null;

    // Modelo de link para um commit ou uma branch, com {path} e {line}. Nulo sem remote reconhecido ou sem versão.
    public static string? HostTemplate(RemoteRepository? remote, string? commit, string? branch)
    {
        if (remote is null) return null;
        var byCommit = !string.IsNullOrWhiteSpace(commit);
        var version = byCommit ? commit!.Trim() : branch?.Trim();
        if (string.IsNullOrEmpty(version)) return null;
        var p = remote.Parts.Select(Uri.EscapeDataString).ToArray();
        var v = Uri.EscapeDataString(version);
        return remote.Host switch
        {
            RepositoryHostKind.GitHub => $"{remote.BaseUrl}/{p[0]}/{p[1]}/blob/{v}/{PathToken}#L{LineToken}",
            RepositoryHostKind.GitLab => $"{remote.BaseUrl}/{string.Join('/', p)}/-/blob/{v}/{PathToken}#L{LineToken}",
            RepositoryHostKind.Bitbucket => $"{remote.BaseUrl}/{p[0]}/{p[1]}/src/{v}/{PathToken}#lines-{LineToken}",
            RepositoryHostKind.AzureDevOps =>
                $"{remote.BaseUrl}/{p[0]}/{p[1]}/_git/{p[2]}?path=/{PathToken}&version={(byCommit ? "GC" : "GB")}{v}" +
                $"&line={LineToken}&lineEnd={LineToken}&lineStartColumn=1&lineEndColumn=200&lineStyle=plain&_a=contents",
            _ => null
        };
    }

    // Modelo do editor: o configurado (Rastro:Editor:UrlTemplate, com {path} absoluto e {line}) ou o do VS Code.
    // A raiz do repositório entra com "/", como o vscode:// espera também no Windows.
    public static string EditorTemplate(string repositoryRoot, string? configured)
    {
        // Cada segmento vai codificado (espaço, acento), menos a letra da unidade do Windows ("C:").
        var segments = repositoryRoot.Replace('\\', '/').TrimEnd('/').Split('/');
        var root = string.Join('/', segments.Select((s, i) => i == 0 && s.EndsWith(':') ? s : Uri.EscapeDataString(s)));
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim().Replace(PathToken, $"{root}/{PathToken}", StringComparison.Ordinal);
        // No Linux e no macOS a raiz começa com "/", que o modelo do VS Code já traz depois de "file".
        return VsCodeTemplate.Replace(RootToken, root.TrimStart('/'), StringComparison.Ordinal);
    }

    // Preenche o modelo: cada segmento do caminho vai codificado (espaço, acento) e a barra fica.
    public static string Fill(string template, string path, int line)
    {
        var encoded = string.Join('/', path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        var text = new StringBuilder(template);
        text.Replace(PathToken, encoded);
        text.Replace(LineToken, line.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return text.ToString();
    }
}
