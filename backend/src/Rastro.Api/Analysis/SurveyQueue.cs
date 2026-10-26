using System.Threading.Channels;

namespace Rastro.Api.Analysis;

// Fila em memória dos levantamentos a analisar. Um de cada vez: a carga de uma solution
// grande usa muita memória e CPU.
public class SurveyQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(Guid surveyId, CancellationToken ct = default) => _channel.Writer.WriteAsync(surveyId, ct);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
