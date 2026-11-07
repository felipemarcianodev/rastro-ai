using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Rastro.Api.Controllers;

namespace Rastro.Api.Cli;

public enum PortState
{
    Free = 0,
    RastroOpen = 1,
    OtherProgram = 2
}

public sealed record PortCheck(PortState State, string? Version = null);

// Confere a porta antes de subir a tela: abrir o "rastro" com ele já aberto não é erro, e o segundo
// processo nem chega a migrar o banco que o primeiro está usando.
public static class PortProbe
{
    public static readonly TimeSpan DefaultHealthTimeout = TimeSpan.FromSeconds(2);

    // healthTimeout só muda nos testes: numa máquina carregada, a primeira resposta de um servidor recém-aberto
    // passa de 2 segundos e o Rastro de verdade seria tomado por outro programa.
    public static async Task<PortCheck> CheckAsync(int port, ILogger logger, CancellationToken ct = default, TimeSpan? healthTimeout = null)
    {
        if (IsFree(port))
            return new PortCheck(PortState.Free);

        var version = await RastroVersionAtAsync(port, logger, healthTimeout ?? DefaultHealthTimeout, ct);
        return version is null ? new PortCheck(PortState.OtherProgram) : new PortCheck(PortState.RastroOpen, version);
    }

    // Devolve o código de saída: Rastro já aberto é o que o usuário queria (0); outro programa na porta é erro (1).
    public static int Report(PortCheck check, int port, TextWriter output, TextWriter error)
    {
        if (check.State == PortState.RastroOpen)
        {
            output.WriteLine($"O Rastro {check.Version} já está aberto em http://127.0.0.1:{port}. Abra esse endereço no navegador.");
            return 0;
        }

        error.WriteLine($"A porta {port} já está em uso por outro programa. Escolha outra: rastro --porta N");
        return 1;
    }

    // Tenta ocupar a porta do mesmo jeito que o Kestrel. Só "endereço em uso" conta como ocupada: qualquer
    // outra recusa (porta reservada, sem permissão) fica para o Kestrel relatar, porque não há quem consultar nela.
    private static bool IsFree(int port)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        if (!OperatingSystem.IsWindows())
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            return true;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            return false;
        }
    }

    private static async Task<string?> RastroVersionAtAsync(int port, ILogger logger, TimeSpan timeout, CancellationToken ct)
    {
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = timeout };
        try
        {
            var body = await http.GetStringAsync($"http://127.0.0.1:{port}/api/health", ct);
            var health = JsonSerializer.Deserialize<HealthController.HealthResponse>(body, JsonSerializerOptions.Web);
            return health is { Status: "ok", Version.Length: > 0, SchemaVersion: > 0 } ? health.Version : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogDebug(ex, "A porta {Port} está ocupada e quem responde não é um Rastro.", port);
            return null;
        }
    }
}
