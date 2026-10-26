using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Rastro.Ai.Providers;

namespace Rastro.Tests.Ai;

// Cenários AI-HTTP-NNN: cada provedor monta o pedido certo e traduz a resposta (sem rede).
public class ProviderHttpTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Factory(HttpMessageHandler handler, string baseAddress) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri(baseAddress) };
    }

    private static readonly AiRequest Request = new("sistema", "pedido", "teste",
        (JsonObject)JsonNode.Parse("""{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}""")!);

    [Fact] // AI-HTTP-001
    public async Task Claude_MontaPedidoComSaidaEstruturadaFallbackECabecalhos_ELeOTextoEOsTokens()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"stop_reason":"end_turn","content":[{"type":"thinking","thinking":""},{"type":"text","text":"{\"ok\":true}"}],
             "usage":{"input_tokens":120,"output_tokens":30}}
            """);
        var service = new ClaudeTextService(new Factory(handler, "https://api.anthropic.com/"), NullLogger<ClaudeTextService>.Instance);

        var result = await service.GenerateJsonAsync(Request, "chave-secreta", "claude-opus-5-5", CancellationToken.None);

        Assert.Equal("""{"ok":true}""", result.Json);
        Assert.Equal((120, 30), (result.InputTokens, result.OutputTokens));
        Assert.Equal("chave-secreta", handler.Request!.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", handler.Request.Headers.GetValues("anthropic-version").Single());
        Assert.Equal("server-side-fallback-2026-07-01", handler.Request.Headers.GetValues("anthropic-beta").Single());
        var body = JsonNode.Parse(handler.RequestBody!)!;
        Assert.Equal("json_schema", (string?)body["output_config"]!["format"]!["type"]);
        Assert.Equal("default", (string?)body["fallbacks"]);
        Assert.Null(body["tool_choice"]);
        Assert.Null(body["thinking"]);
    }

    [Fact] // AI-HTTP-002
    public async Task Claude_ModeloSemFallback_NaoMandaOParametroNemOBeta()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"stop_reason":"end_turn","content":[{"type":"text","text":"{}"}],"usage":{}}""");
        var service = new ClaudeTextService(new Factory(handler, "https://api.anthropic.com/"), NullLogger<ClaudeTextService>.Instance);

        await service.GenerateJsonAsync(Request, "k", "claude-haiku-4-5", CancellationToken.None);

        Assert.False(handler.Request!.Headers.Contains("anthropic-beta"));
        Assert.Null(JsonNode.Parse(handler.RequestBody!)!["fallbacks"]);
    }

    [Theory] // AI-HTTP-003
    [InlineData(HttpStatusCode.OK, """{"stop_reason":"refusal","content":[]}""", "recusou")]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":{}}""", "Chave do Claude inválida")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":{}}""", "Limite de uso")]
    public async Task Claude_RecusaOuErro_ViraMensagemAmigavelSemVazarDetalhe(HttpStatusCode status, string body, string message)
    {
        var service = new ClaudeTextService(new Factory(new StubHandler(status, body), "https://api.anthropic.com/"), NullLogger<ClaudeTextService>.Instance);

        var ex = await Assert.ThrowsAsync<AiProviderException>(() => service.GenerateJsonAsync(Request, "k", "claude-opus-5-5", CancellationToken.None));

        Assert.Contains(message, ex.Message);
        Assert.DoesNotContain("\"error\"", ex.Message);
    }

    [Fact] // AI-HTTP-004
    public async Task OpenAi_UsaJsonSchemaEstritoEBearer_ETrataRecusa()
    {
        var ok = new StubHandler(HttpStatusCode.OK, """{"choices":[{"message":{"content":"{\"ok\":true}"}}],"usage":{"prompt_tokens":50,"completion_tokens":7}}""");
        var service = new OpenAiTextService(new Factory(ok, "https://api.openai.com/"), NullLogger<OpenAiTextService>.Instance);

        var result = await service.GenerateJsonAsync(Request, "sk-teste", "gpt-4o", CancellationToken.None);

        Assert.Equal((50, 7), (result.InputTokens, result.OutputTokens));
        Assert.Equal("Bearer sk-teste", ok.Request!.Headers.Authorization!.ToString());
        Assert.True((bool)JsonNode.Parse(ok.RequestBody!)!["response_format"]!["json_schema"]!["strict"]!);

        var refused = new OpenAiTextService(new Factory(new StubHandler(HttpStatusCode.OK, """{"choices":[{"message":{"refusal":"não posso"}}]}"""), "https://api.openai.com/"), NullLogger<OpenAiTextService>.Instance);
        await Assert.ThrowsAsync<AiProviderException>(() => refused.GenerateJsonAsync(Request, "k", "gpt-4o", CancellationToken.None));
    }

    [Fact] // AI-HTTP-005
    public async Task Gemini_ChaveNoCabecalhoNuncaNaUrl_ELeOTexto()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"{\"ok\":true}"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":40,"candidatesTokenCount":4}}""");
        var service = new GeminiTextService(new Factory(handler, "https://generativelanguage.googleapis.com/"), NullLogger<GeminiTextService>.Instance);

        var result = await service.GenerateJsonAsync(Request, "chave-gemini", "gemini-2.5-flash", CancellationToken.None);

        Assert.Equal((40, 4), (result.InputTokens, result.OutputTokens));
        Assert.Equal("chave-gemini", handler.Request!.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain("chave-gemini", handler.Request.RequestUri!.ToString());
        Assert.EndsWith("v1beta/models/gemini-2.5-flash:generateContent", handler.Request.RequestUri.AbsolutePath);
    }
}
