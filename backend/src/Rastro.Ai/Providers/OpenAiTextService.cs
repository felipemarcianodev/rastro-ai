using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Rastro.Ai.Settings;

namespace Rastro.Ai.Providers;

// OpenAI pela Chat Completions, HTTP direto, com response_format json_schema estrito.
public class OpenAiTextService(IHttpClientFactory http, ILogger<OpenAiTextService> logger) : IAiTextService
{
    public const string HttpClientName = "ai-openai";

    public AiProvider Provider => AiProvider.OpenAi;

    public async Task<AiResult> GenerateJsonAsync(AiRequest request, string apiKey, string model, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = request.System },
                new JsonObject { ["role"] = "user", ["content"] = request.User }),
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = request.SchemaName, ["strict"] = true, ["schema"] = request.Schema.DeepClone() }
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions") { Content = JsonContent.Create(body) };
        message.Headers.Add("Authorization", $"Bearer {apiKey}");

        HttpResponseMessage response;
        try
        {
            response = await http.CreateClient(HttpClientName).SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Falha de rede ou timeout ao chamar a OpenAI.");
            throw new AiProviderException("Não foi possível falar com a API da OpenAI (rede ou tempo esgotado).", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("OpenAI respondeu {StatusCode}.", (int)response.StatusCode);
                throw new AiProviderException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Chave da OpenAI inválida ou sem permissão.",
                    HttpStatusCode.TooManyRequests => "Limite de uso da OpenAI atingido. Tente de novo em instantes.",
                    HttpStatusCode.NotFound => "Modelo da OpenAI não encontrado. Confira o nome do modelo na configuração.",
                    _ => $"A OpenAI falhou ({(int)response.StatusCode}). Tente de novo."
                });
            }

            var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct)
                ?? throw new AiProviderException("A OpenAI respondeu vazio.");
            var choice = json["choices"]?[0]?["message"];
            if (choice?["refusal"] is JsonValue refusal && !string.IsNullOrWhiteSpace((string?)refusal))
                throw new AiProviderException("A OpenAI recusou o pedido. Tente outro nó ou outro provedor.");

            var text = (string?)choice?["content"];
            if (string.IsNullOrWhiteSpace(text)) throw new AiProviderException("A OpenAI respondeu sem conteúdo.");

            return new AiResult(text, (int?)json["usage"]?["prompt_tokens"] ?? 0, (int?)json["usage"]?["completion_tokens"] ?? 0);
        }
    }
}
