namespace Rastro.Api.Desktop;

// Quais links a janela própria aceita abrir fora dela (Fase 57, ADR-010). Validador puro, sem I/O: é a proteção
// de verdade, porque abrir URL pelo sistema operacional entrega o endereço ao programa que o sistema registrou.
// Passa só: https com host exato do Azure DevOps, GitHub, GitLab ou Bitbucket (sem usuário, senha ou porta);
// vscode://file/; ou o esquema do modelo do editor configurado em Rastro:Editor:UrlTemplate.
public static class ExternalLinkPolicy
{
    public const int MaxLength = 4096;

    private const string HttpsScheme = "https";
    private const string VsCodePrefix = "vscode://file/";

    private static readonly HashSet<string> AllowedHosts = new(StringComparer.Ordinal)
    {
        "dev.azure.com", "github.com", "gitlab.com", "bitbucket.org"
    };

    // Esquemas que nunca valem como "editor", mesmo que alguém os ponha no modelo (a configuração também é entrada).
    private static readonly HashSet<string> ForbiddenEditorSchemes = new(StringComparer.Ordinal)
    {
        "http", "https", "file", "javascript", "data", "vbscript", "blob", "about", "ftp", "ssh", "telnet", "mailto",
        "search", "search-ms", "ldap", "smb", "ws", "wss", "cmd", "powershell", "shell"
    };

    private const string ForbiddenEditorSchemePrefix = "ms-";

    // Esquema e host para o log (nunca a URL inteira, que leva caminho do disco). Texto que não é URL vira "?".
    public static (string Scheme, string Host) Describe(string? url)
    {
        if (string.IsNullOrEmpty(url)) return ("?", "?");
        var colon = url.IndexOf(':');
        var scheme = colon is > 0 and <= 32 ? url[..colon].ToLowerInvariant() : "?";
        if (scheme == "?" || scheme.Any(c => !IsSchemeChar(c))) return ("?", "?");
        var host = "?";
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            host = uri.Host.ToLowerInvariant();
        return (scheme, host);
    }

    public static bool IsAllowed(string? url, string? editorTemplate)
    {
        if (string.IsNullOrEmpty(url) || url.Length > MaxLength) return false;
        // Controle, espaço e barra invertida abrem brecha para leituras diferentes da mesma URL.
        if (url.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\')) return false;

        if (url.StartsWith(VsCodePrefix, StringComparison.OrdinalIgnoreCase)) return true;
        if (url.StartsWith(HttpsScheme + "://", StringComparison.OrdinalIgnoreCase)) return IsAllowedHttps(url);

        var configured = SchemeOf(editorTemplate);
        var scheme = SchemeOf(url);
        return configured is not null && scheme == configured && IsEditorScheme(configured);
    }

    private static bool IsAllowedHttps(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != HttpsScheme) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
        // A autoridade crua tem de ser só o host: sem usuário@, sem :porta (nem a padrão), sem ponto no fim.
        var rest = url[(HttpsScheme.Length + 3)..];
        var end = rest.IndexOfAny(['/', '?', '#']);
        var authority = end < 0 ? rest : rest[..end];
        var host = uri.Host.ToLowerInvariant();
        return string.Equals(authority, host, StringComparison.OrdinalIgnoreCase) && AllowedHosts.Contains(host);
    }

    private static bool IsEditorScheme(string scheme) =>
        scheme.Length > 0
        && !ForbiddenEditorSchemes.Contains(scheme)
        && !scheme.StartsWith(ForbiddenEditorSchemePrefix, StringComparison.Ordinal);

    // Esquema em minúsculas (RFC 3986: letra, depois letra, dígito, "+", "-" ou "."), ou nulo se o texto não começa com um.
    private static string? SchemeOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = text.Trim();
        var colon = value.IndexOf(':');
        if (colon < 1 || colon > 32 || !char.IsAsciiLetter(value[0])) return null;
        var scheme = value[..colon];
        return scheme.All(IsSchemeChar) ? scheme.ToLowerInvariant() : null;
    }

    private static bool IsSchemeChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.';
}
