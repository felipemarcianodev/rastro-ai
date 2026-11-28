using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Rastro.Api;

namespace Rastro.Tests.Api;

// Cenários API-CACHE-NNN: a página da tela embutida não pode ficar guardada no WebView2 da janela, senão a janela segue
// na tela da versão anterior depois de o pacote ser atualizado. A tela embutida só existe no pacote (o wwwroot vem do
// build do Angular), então aqui se confere a regra que a tela e o draw.io usam ao servir cada arquivo.
public class ScreenCacheTests : IDisposable
{
    private readonly string _screen = Path.Combine(Path.GetTempPath(), "rastro-tela-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_screen)) Directory.Delete(_screen, recursive: true);
    }

    private string CacheControlFor(string fileName)
    {
        Directory.CreateDirectory(_screen);
        File.WriteAllText(Path.Combine(_screen, fileName), "teste");
        var context = new DefaultHttpContext();
        using var files = new PhysicalFileProvider(_screen);
        PageCache.Apply(new StaticFileResponseContext(context, files.GetFileInfo(fileName)));
        return context.Response.Headers.CacheControl.ToString();
    }

    [Fact] // API-CACHE-002
    public void Apply_PaginaDaTela_RecebeNoCache_eOScriptComHashNao()
    {
        Assert.Equal(PageCache.NoCache, CacheControlFor("index.html"));
        Assert.Equal(PageCache.NoCache, CacheControlFor("INDEX.HTML"));
        Assert.Equal(string.Empty, CacheControlFor("main-3CN6DQVW.js"));
        Assert.Equal(string.Empty, CacheControlFor("styles-ABC.css"));
    }
}
