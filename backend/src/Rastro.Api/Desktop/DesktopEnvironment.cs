using System.Runtime.InteropServices;

namespace Rastro.Api.Desktop;

// Confere, antes de criar a janela, o que ela precisa em cada sistema (ADR-007). A conferência vem antes porque
// o Photino, sem o WebView2, tenta baixar e instalar o runtime sozinho, e no Linux sem tela o GTK encerra o processo.
public static class DesktopEnvironment
{
    public const string WebView2DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";
    public const string LinuxWebKitLibrary = "libwebkit2gtk-4.1.so.0";

    // Devolve o motivo de a janela não poder abrir aqui, ou null quando pode.
    public static string? WhyUnavailable(ILogger logger)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!Environment.UserInteractive)
                return "esta sessão não tem tela";
            return WebView2Version(logger) is null
                ? $"falta o WebView2 Runtime da Microsoft (baixe em {WebView2DownloadUrl})"
                : null;
        }

        if (OperatingSystem.IsLinux())
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
                && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
                return "esta sessão não tem tela (DISPLAY e WAYLAND_DISPLAY vazios)";
            if (!NativeLibrary.TryLoad(LinuxWebKitLibrary, out var webkit))
                return "falta a biblioteca libwebkit2gtk-4.1 (no Ubuntu e no Debian: sudo apt install libwebkit2gtk-4.1-0)";
            NativeLibrary.Free(webkit);
            return null;
        }

        // No macOS o WebKit é do sistema.
        return OperatingSystem.IsMacOS() ? null : "este sistema não tem a janela própria";
    }

    // Versão do WebView2 instalada, pela função oficial do carregador que viaja no pacote (WebView2Loader.dll).
    private static string? WebView2Version(ILogger logger)
    {
        try
        {
            var result = GetAvailableCoreWebView2BrowserVersionString(null, out var versionPointer);
            if (result != 0 || versionPointer == IntPtr.Zero)
                return null;
            var version = Marshal.PtrToStringUni(versionPointer);
            Marshal.FreeCoTaskMem(versionPointer);
            logger.LogDebug("WebView2 Runtime {Version} encontrado.", version);
            return version;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            logger.LogWarning(ex, "O carregador do WebView2 (WebView2Loader.dll) não foi achado ao lado do Rastro.");
            return null;
        }
    }

    [DllImport("WebView2Loader.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetAvailableCoreWebView2BrowserVersionString(string? browserExecutableFolder, out IntPtr versionInfo);
}
