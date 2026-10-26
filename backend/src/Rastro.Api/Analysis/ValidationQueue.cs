using System.Threading.Channels;

namespace Rastro.Api.Analysis;

// Fila em memória das validações de mudança (Fase 9). Uma de cada vez: cada uma carrega a
// solution e roda build e testes do repositório.
public class ValidationQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(Guid validationId, CancellationToken ct = default) => _channel.Writer.WriteAsync(validationId, ct);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
