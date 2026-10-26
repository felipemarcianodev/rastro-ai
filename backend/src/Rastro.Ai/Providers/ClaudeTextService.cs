using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Rastro.Ai.Settings;

namespace Rastro.Ai.Providers;

// Claude pela API de Messages, HTTP direto (padrão do meu-portfolio, decisão do dono).
// Saída estruturada por output_config.format (o tool_choice forçado é recusado nos modelos atuais),
// fallback do lado do servidor quando o modelo aceita, e refusal tratado antes de ler o conteúdo.
public class ClaudeTextService(IHttpClientFactory http, ILogger<ClaudeTextService> logger) : IAiTextService
{
    public const string HttpClientName = "ai-claude";
    private const string FallbackBeta = "server-side-fallback-2026-07-01";
    private const int MaxTokens = 16000;

    // Modelos que aceitam fallbacks: "default".
    private static readonly HashSet<string> FallbackModels = ["claude-opus-5-5", "claude-opus-5", "claude-fable-5-1", "claude-sonnet-5-5"];

    public AiProvider Provider => AiProvider.Claude;

    public async Task<AiResult> GenerateJsonAsync(AiRequest request, string apiKey, string model, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = MaxTokens,
            ["system"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = request.System,
                ["cache_control"] = new JsonObject { ["type"] = "ephemeral" }
            }),
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = request.User }),
            ["output_config"] = new JsonObject
            {
                ["effort"] = "medium",
                ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = request.Schema.DeepClone() }
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/messages") { Content = JsonContent.Create(body) };
        message.Headers.Add("x-api-key", apiKey);
        message.Headers.Add("anthropic-version", "2023-06-01");
        if (FallbackModels.Contains(model))
        {
            body["fallbacks"] = "default";
            message.Content = JsonContent.Create(body);
            message.Headers.Add("anthropic-beta", FallbackBeta);
        }

        var json = await SendAsync(message, ct);

        if ((string?)json["stop_reason"] == "refusal")
            throw new AiProviderException("O Claude recusou o pedido (classificador de segurança). Tente outro nó ou outro provedor.");
        if ((string?)json["stop_reason"] == "max_tokens")
            throw new AiProviderException("A resposta do Claude passou do limite de tamanho. Tente um nó com menos contexto.");

        var text = (json["content"] as JsonArray)?
            .OfType<JsonObject>()
            .Where(block => (string?)block["type"] == "text")
            .Select(block => (string?)block["text"])
            .LastOrDefault(t => !string.IsNullOrWhiteSpace(t));
        if (text is null) throw new AiProviderException("O Claude respondeu sem conteúdo.");

        return new AiResult(text, (int?)json["usage"]?["input_tokens"] ?? 0, (int?)json["usage"]?["output_tokens"] ?? 0);
    }

    private async Task<JsonObject> SendAsync(HttpRequestMessage message, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.CreateClient(HttpClientName).SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Falha de rede ou timeout ao chamar o Claude.");
            throw new AiProviderException("Não foi possível falar com a API do Claude (rede ou tempo esgotado).", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Claude respondeu {StatusCode}.", (int)response.StatusCode);
                throw new AiProviderException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Chave do Claude inválida ou sem permissão.",
                    HttpStatusCode.TooManyRequests => "Limite de uso do Claude atingido. Tente de novo em instantes.",
                    HttpStatusCode.NotFound => "Modelo do Claude não encontrado. Confira o nome do modelo na configuração.",
                    HttpStatusCode.BadRequest => "O Claude recusou o formato do pedido. Confira o modelo configurado.",
                    _ => $"O Claude falhou ({(int)response.StatusCode}). Tente de novo."
                });
            }

            return await response.Content.ReadFromJsonAsync<JsonObject>(ct)
                ?? throw new AiProviderException("O Claude respondeu vazio.");
        }
    }
}
