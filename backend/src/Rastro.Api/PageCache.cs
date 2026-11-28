using Microsoft.AspNetCore.StaticFiles;

namespace Rastro.Api;

// Cache da página de entrada (a tela e o draw.io). Sem Cache-Control, o WebView2 da janela própria reaproveitava do
// disco o index.html guardado, que aponta para o main.js da versão anterior: depois de atualizar o pacote, a janela
// seguia na tela antiga. Com "no-cache" a página é conferida a cada abertura (pelo ETag, sem baixar de novo quando não
// mudou). Os arquivos com hash no nome mudam de nome a cada build e não precisam disso.
public static class PageCache
{
    public const string PageFile = "index.html";
    public const string NoCache = "no-cache";

    public static bool IsPage(string fileName) => string.Equals(fileName, PageFile, StringComparison.OrdinalIgnoreCase);

    public static void Apply(StaticFileResponseContext context)
    {
        if (IsPage(context.File.Name)) context.Context.Response.Headers.CacheControl = NoCache;
    }
}
