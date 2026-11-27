using Microsoft.Extensions.FileProviders;

namespace Rastro.Api.Editor;

// Draw.io dentro do Rastro (ADR-011): a aplicação do draw.io vem no pacote, na pasta "drawio" ao lado do executável
// (preparada por scripts/preparar-drawio.ps1, com versão e hash fixos), e a API a serve em /drawio, na mesma origem
// da tela. Sem a pasta (CI de PR, código-fonte sem o script), o draw.io não está disponível e a tela não o oferece.
// O código fica em Editor/, e não em Drawio/: no Windows, "Drawio" e a pasta de dados "drawio" são a mesma pasta, e
// o script, que apaga e recria a pasta de dados, apagaria o código junto.
public static class DrawioHosting
{
    public const string RequestPath = "/drawio";
    public const string FolderSetting = "Rastro:Drawio:Folder";
    private const string VersionFile = "VERSION";
    private const string IndexFile = "index.html";

    // Só a própria origem: o editor roda sem rede (modo isolado) e a página que o abre é a tela do Rastro.
    // O draw.io usa script e estilo embutidos e monta funções em tempo de execução, por isso "unsafe-inline" e
    // "unsafe-eval" valem só nas páginas dele; a tela do Rastro não recebe este cabeçalho.
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; font-src 'self' data:; connect-src 'self'; worker-src 'self' blob:; " +
        "frame-ancestors 'self'; base-uri 'self'; form-action 'none'";

    public static string Folder(IConfiguration configuration) =>
        configuration[FolderSetting] is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "drawio");

    public static bool IsAvailable(string folder) => File.Exists(Path.Combine(folder, IndexFile));

    public static string? Version(string folder)
    {
        var path = Path.Combine(folder, VersionFile);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    // Serve a pasta em /drawio com o cabeçalho de segurança próprio. O PhysicalFileProvider não sai da pasta (../).
    public static void UseDrawio(this WebApplication app)
    {
        var folder = Folder(app.Configuration);
        if (!IsAvailable(folder))
        {
            app.Logger.LogInformation("draw.io não está nesta instalação (pasta {Folder} sem index.html).", folder);
            return;
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.GetFullPath(folder)),
            RequestPath = RequestPath,
            OnPrepareResponse = context =>
            {
                var headers = context.Context.Response.Headers;
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
                headers.XContentTypeOptions = "nosniff";
            },
        });
    }
}
