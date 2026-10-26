using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Rastro.Ai.Settings;

namespace Rastro.Ai.Providers;

// Gemini pela generateContent, HTTP direto, com saída JSON e schema (responseJsonSchema).
// A chave vai no header x-goog-api-key, nunca na URL (URL pode ir parar em log de proxy).
public class GeminiTextService(IHttpClientFactory http, ILogger<GeminiTextService> logger) : IAiTextService
{
    public const string HttpClientName = "ai-gemini";

    public AiProvider Provider => AiProvider.Gemini;

    public async Task<AiResult> GenerateJsonAsync(AiRequest request, string apiKey, string model, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = request.System }) },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = request.User })
            }),
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseJsonSchema"] = request.Schema.DeepClone()
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, $"v1beta/models/{Uri.EscapeDataString(model)}:generateContent")
        {
            Content = JsonContent.Create(body)
        };
        message.Headers.Add("x-goog-api-key", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await http.CreateClient(HttpClientName).SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Falha de rede ou timeout ao chamar o Gemini.");
            throw new AiProviderException("Não foi possível falar com a API do Gemini (rede ou tempo esgotado).", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Gemini respondeu {StatusCode}.", (int)response.StatusCode);
                throw new AiProviderException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Chave do Gemini inválida ou sem permissão.",
                    HttpStatusCode.TooManyRequests => "Limite de uso do Gemini atingido. Tente de novo em instantes.",
                    HttpStatusCode.NotFound => "Modelo do Gemini não encontrado. Confira o nome do modelo na configuração.",
                    _ => $"O Gemini falhou ({(int)response.StatusCode}). Tente de novo."
                });
            }

            var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct)
                ?? throw new AiProviderException("O Gemini respondeu vazio.");
            var candidate = json["candidates"]?[0];
            if ((string?)candidate?["finishReason"] is "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST")
                throw new AiProviderException("O Gemini recusou o pedido. Tente outro nó ou outro provedor.");

            var text = (candidate?["content"]?["parts"] as JsonArray)?
                .Select(part => (string?)part?["text"])
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            if (text is null) throw new AiProviderException("O Gemini respondeu sem conteúdo.");

            return new AiResult(text, (int?)json["usageMetadata"]?["promptTokenCount"] ?? 0, (int?)json["usageMetadata"]?["candidatesTokenCount"] ?? 0);
        }
    }
}
