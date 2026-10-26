using System.Text.Json.Nodes;
using Rastro.Ai.Settings;

namespace Rastro.Ai.Providers;

// Pedido com saída estruturada: o provedor devolve JSON que obedece ao schema.
public record AiRequest(string System, string User, string SchemaName, JsonObject Schema);

public record AiResult(string Json, int InputTokens, int OutputTokens);

// Falha do provedor com mensagem que pode ir para a tela (sem chave, sem corpo de resposta).
public class AiProviderException(string message, Exception? inner = null) : Exception(message, inner);

// Uma implementação por provedor (Claude, OpenAI, Gemini), todas com o mesmo contrato.
public interface IAiTextService
{
    AiProvider Provider { get; }

    Task<AiResult> GenerateJsonAsync(AiRequest request, string apiKey, string model, CancellationToken ct);
}
