using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rastro.Api.Desktop;

// Tipos das mensagens entre a tela e a janela própria (ADR-007). Viajam como int, como todo enum do Rastro.
public enum DesktopMessageType
{
    // Tela -> .NET: abrir a caixa "Escolher pasta".
    PickFolder = 0,
    // .NET -> tela: a pasta escolhida (ou null, se o usuário cancelou).
    FolderPicked = 1,
    // Tela -> .NET: abrir um link no navegador ou no editor (só o que o ExternalLinkPolicy aceita).
    OpenLink = 2,
    // .NET -> tela: se o link abriu (recusado pela política ou o sistema não abriu: false), para a tela avisar.
    LinkOpened = 3
}

public sealed record DesktopRequest(DesktopMessageType Type, string RequestId, string? Url = null);

// Ponte de mensagens da janela própria. Formato fixo, conferido aqui: a página pede "escolher pasta" (e só recebe
// o caminho escolhido pelo usuário na caixa do sistema) ou "abrir link" (só host reconhecido ou editor
// configurado). Nenhuma mensagem executa comando nem lê disco.
public static partial class DesktopBridge
{
    // Cabe a URL do Azure DevOps com caminho e linha (ExternalLinkPolicy.MaxLength) mais o envelope.
    public const int MaxMessageLength = 4200;
    public const int MaxPathLength = 4096;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex RequestIdPattern();

    // Mensagens válidas: {"type":0,"requestId":"pasta-1"} e {"type":2,"requestId":"link-1","url":"..."}, sem outro
    // campo. Qualquer outra coisa é recusada (null).
    public static DesktopRequest? Parse(string? message)
    {
        if (string.IsNullOrEmpty(message) || message.Length > MaxMessageLength)
            return null;
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.Number || !type.TryGetInt32(out var kind))
                return null;
            if (!root.TryGetProperty("requestId", out var id) || id.ValueKind != JsonValueKind.String)
                return null;
            var requestId = id.GetString()!;
            if (!RequestIdPattern().IsMatch(requestId))
                return null;
            var count = root.EnumerateObject().Count();
            if (kind == (int)DesktopMessageType.OpenLink)
            {
                // {"type":2,"requestId":"link-1","url":"..."}: a resposta diz se abriu.
                if (count != 3 || !root.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String)
                    return null;
                var text = url.GetString()!;
                return text.Length is > 0 and <= ExternalLinkPolicy.MaxLength
                    ? new DesktopRequest(DesktopMessageType.OpenLink, requestId, text)
                    : null;
            }
            if (kind != (int)DesktopMessageType.PickFolder || count != 2)
                return null;
            return new DesktopRequest(DesktopMessageType.PickFolder, requestId);
        }
        catch (JsonException)
        {
            // Texto que não é JSON: recusado como qualquer outro formato errado; quem chama registra a recusa.
            return null;
        }
    }

    // A resposta só leva caminho absoluto; o resto vira null (como cancelar).
    public static string FolderPickedMessage(string requestId, string? path)
    {
        var safe = path is { Length: > 0 and <= MaxPathLength } && Path.IsPathFullyQualified(path) ? path : null;
        return JsonSerializer.Serialize(new { type = (int)DesktopMessageType.FolderPicked, requestId, path = safe }, Json);
    }

    // A resposta do "abrir link" só diz se abriu; nunca devolve a URL nem o motivo.
    public static string LinkOpenedMessage(string requestId, bool opened) =>
        JsonSerializer.Serialize(new { type = (int)DesktopMessageType.LinkOpened, requestId, opened }, Json);

    // Trata uma mensagem da página: devolve a resposta, ou null quando a mensagem é recusada pelo formato.
    // openLink abre um link já aprovado e diz se o sistema abriu; editorTemplate é o Rastro:Editor:UrlTemplate (ou
    // nulo). Sem openLink, "abrir link" é recusado, e a tela recebe que não abriu.
    public static string? Handle(string? message, Func<string?> pickFolder, ILogger logger, Func<string, bool>? openLink = null, string? editorTemplate = null)
    {
        var request = Parse(message);
        if (request is null)
        {
            logger.LogWarning("Mensagem da janela recusada: formato fora do combinado ({Length} caracteres).", message?.Length ?? 0);
            return null;
        }
        if (request.Type == DesktopMessageType.OpenLink)
        {
            if (openLink is null || !ExternalLinkPolicy.IsAllowed(request.Url, editorTemplate))
            {
                var (scheme, host) = ExternalLinkPolicy.Describe(request.Url);
                logger.LogWarning("Link recusado pela janela: esquema {Scheme}, host {Host}.", scheme, host);
                return LinkOpenedMessage(request.RequestId, false);
            }
            return LinkOpenedMessage(request.RequestId, openLink(request.Url!));
        }
        return FolderPickedMessage(request.RequestId, pickFolder());
    }
}
