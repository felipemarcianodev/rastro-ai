using Microsoft.Extensions.Logging;
using Rastro.Infrastructure.Processes;

namespace Rastro.Infrastructure.Analysis;

// O Node é requisito opcional do extrator Angular (ADR-005). Sem ele, a tela não oferece o Angular.
public interface INodeRuntime
{
    bool Available { get; }
}

// Confere "node --version" uma vez e guarda o resultado.
public sealed class NodeRuntime : INodeRuntime
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);

    private readonly Lazy<bool> _available;

    public NodeRuntime(ILogger<NodeRuntime> logger)
    {
        _available = new Lazy<bool>(() => Check(logger));
    }

    public bool Available => _available.Value;

    private static bool Check(ILogger logger)
    {
        try
        {
            var result = ProcessRunner.RunAsync(NodeCommand.FileName, ["--version"], null, CheckTimeout, CancellationToken.None)
                .GetAwaiter().GetResult();
            if (result.ExitCode != 0)
            {
                logger.LogWarning("O Node respondeu com código {ExitCode}; o extrator Angular fica desligado.", result.ExitCode);
                return false;
            }
            logger.LogInformation("Node {Version} encontrado; o extrator Angular está disponível.", result.Output);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or TimeoutException)
        {
            logger.LogInformation(ex, "Node não encontrado no PATH; o extrator Angular fica desligado.");
            return false;
        }
    }
}

internal static class NodeCommand
{
    public const string FileName = "node";
}
