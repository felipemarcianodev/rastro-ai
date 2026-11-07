using System.Runtime.ExceptionServices;
using Photino.NET;
using Rastro.Infrastructure.Storage;

namespace Rastro.Api.Desktop;

// Janela própria do "rastro" (ADR-007): a API sobe em segundo plano, a janela roda na thread principal (numa
// thread STA no Windows, como o WebView2 exige) e aponta para o mesmo endereço local do navegador.
// Fechar a janela encerra o Rastro; Ctrl+C no console fecha a janela. Se a janela não abre, a API continua.
public static class DesktopWindow
{
    private const string Title = "Rastro AI";
    private const int Width = 1440;
    private const int Height = 900;
    private const int MinWidth = 800;
    private const int MinHeight = 560;
    // Cache e armazenamento do WebView2 ficam na pasta de dados do Rastro, e não ao lado do executável.
    public const string DataFolderName = "janela";

    public static int Run(WebApplication app, ILogger logger)
    {
        app.Start();
        var url = app.Urls.First();

        var unavailable = DesktopEnvironment.WhyUnavailable(logger);
        if (unavailable is not null)
        {
            logger.LogWarning("A janela própria não abre nesta máquina: {Reason}.", unavailable);
            return KeepConsole(app, url, unavailable);
        }

        try
        {
            Console.WriteLine("Janela do Rastro aberta. Fechar a janela encerra o Rastro.");
            OnUiThread(() => Show(app, url, logger));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A janela própria do Rastro não abriu em {Url}; a API continua no ar.", url);
            return KeepConsole(app, url, "a janela falhou ao abrir (detalhe no log)");
        }

        app.StopAsync().GetAwaiter().GetResult();
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return 0;
    }

    // Sem janela, o Rastro fica como no --sem-janela: o console e o endereço para o navegador.
    private static int KeepConsole(WebApplication app, string url, string reason)
    {
        Console.Error.WriteLine($"A janela própria não abriu: {reason}.");
        Console.Error.WriteLine($"O Rastro continua no ar: abra {url} no navegador. Ctrl+C para sair.");
        app.WaitForShutdown();
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return 0;
    }

    private static void OnUiThread(Action show)
    {
        if (!OperatingSystem.IsWindows())
        {
            // macOS e Linux: o Program não tem await antes daqui, então esta é a thread principal do processo.
            show();
            return;
        }

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                show();
            }
            catch (Exception ex)
            {
                // Levada para a thread principal, que registra no log e mantém a API.
                failure = ex;
            }
        })
        { Name = "Janela do Rastro" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Throw(failure);
    }

    private static void Show(WebApplication app, string url, ILogger logger)
    {
        var state = new WindowState();
        // Sem o log do Photino no console: cada chamada da configuração vira uma linha.
        var window = new PhotinoWindow()
            .SetLogVerbosity(0)
            .SetTitle(Title)
            .SetUseOsDefaultSize(false)
            .SetSize(Width, Height)
            .SetMinSize(MinWidth, MinHeight)
            .Center()
            .SetDevToolsEnabled(false)
            .SetTemporaryFilesPath(Path.Combine(RastroHome.Folder, DataFolderName));

        var icon = Path.Combine(app.Environment.WebRootPath ?? string.Empty, "favicon.ico");
        if (OperatingSystem.IsWindows() && File.Exists(icon))
            window.SetIconFile(icon);

        window.RegisterWindowCreatedHandler((_, _) => state.Created())
            .RegisterWebMessageReceivedHandler((_, message) => OnMessage(window, message, logger))
            .Load(new Uri(url));

        // Ctrl+C no console: a API para e a janela fecha junto.
        using var stopping = app.Lifetime.ApplicationStopping.Register(() =>
        {
            if (state.TryClose())
                window.Invoke(window.Close);
        });

        window.WaitForClose();
        state.Closed();
    }

    // A caixa de escolher pasta sai do evento do WebView2 (que não aceita laço modal dentro dele) e volta para a
    // thread da janela pelo Invoke; a resposta vai para a página pelo mesmo caminho.
    private static void OnMessage(PhotinoWindow window, string message, ILogger logger)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var response = DesktopBridge.Handle(message, () => PickFolder(window), logger);
                if (response is not null)
                    window.Invoke(() => window.SendWebMessage(response));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "A janela do Rastro não conseguiu responder à página.");
            }
        });
    }

    private static string? PickFolder(PhotinoWindow window)
    {
        string[]? chosen = null;
        window.Invoke(() => chosen = window.ShowOpenFolder("Escolher pasta"));
        return chosen is [var first, ..] ? first : null;
    }

    // Criada, aberta e fechada: o Invoke só vale entre a criação e o fim do WaitForClose.
    private sealed class WindowState
    {
        private readonly Lock gate = new();
        private bool created;
        private bool closed;

        public void Created()
        {
            lock (gate) created = true;
        }

        public void Closed()
        {
            lock (gate) closed = true;
        }

        public bool TryClose()
        {
            lock (gate)
            {
                if (!created || closed)
                    return false;
                closed = true;
                return true;
            }
        }
    }
}
