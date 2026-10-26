using Rastro.Ai.Providers;
using Rastro.Ai.Settings;

namespace Rastro.Tests.Ai;

// Provedor falso (no lugar do Claude) para os testes de integração: a resposta é definida por teste.
public class FakeAiTextService : IAiTextService
{
    public static Func<AiRequest, AiResult> Responder { get; set; } = _ => new AiResult("""{"proposals":[]}""", 10, 5);

    public static AiRequest? LastRequest { get; private set; }

    public AiProvider Provider => AiProvider.Claude;

    public Task<AiResult> GenerateJsonAsync(AiRequest request, string apiKey, string model, CancellationToken ct)
    {
        LastRequest = request;
        return Task.FromResult(Responder(request));
    }
}
