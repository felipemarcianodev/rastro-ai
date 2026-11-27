using System.Net;
using System.Net.Http.Json;
using Rastro.Api.Editor;

namespace Rastro.Tests.Api;

// Cenários API-DRW-NNN: o draw.io dentro do Rastro (ADR-011), servido em /drawio quando a pasta está na instalação.
[Collection(ApiCollection.Name)]
public class DrawioEndpointTests(ApiFixture fixture) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rastro-drawio-" + Guid.NewGuid().ToString("N"));

    private record Status(bool Available, string? Version);

    private HttpClient ClientWithFolder(string folder) =>
        fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting(DrawioHosting.FolderSetting, folder)).CreateClient();

    private void CreateFolder()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "js"));
        File.WriteAllText(Path.Combine(_folder, "index.html"), "<!doctype html><title>draw.io de teste</title>");
        File.WriteAllText(Path.Combine(_folder, "js", "app.min.js"), "// teste");
        File.WriteAllText(Path.Combine(_folder, "VERSION"), "32.6.10");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "rastro-drawio-fora.txt"), "fora da pasta");
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact] // API-DRW-001
    public async Task Status_SemAPastaDoDrawio_DizQueNaoEstaDisponivel()
    {
        var client = ClientWithFolder(_folder);

        var status = await client.GetFromJsonAsync<Status>("/api/drawio");

        Assert.False(status!.Available);
        Assert.Null(status.Version);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/drawio/index.html")).StatusCode);
    }

    [Fact] // API-DRW-002
    public async Task Status_ComAPasta_DizAVersaoEServeOsArquivosComOCabecalhoDeSeguranca()
    {
        CreateFolder();
        var client = ClientWithFolder(_folder);

        var status = await client.GetFromJsonAsync<Status>("/api/drawio");
        var page = await client.GetAsync("/drawio/index.html");
        var script = await client.GetAsync("/drawio/js/app.min.js");

        Assert.True(status!.Available);
        Assert.Equal("32.6.10", status.Version);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("draw.io de teste", await page.Content.ReadAsStringAsync());
        Assert.Equal(DrawioHosting.ContentSecurityPolicy, page.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("connect-src 'self'", DrawioHosting.ContentSecurityPolicy);
        Assert.Equal("nosniff", page.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
    }

    [Fact] // API-DRW-003
    public async Task Arquivo_ForaDaPastaDoDrawio_NaoEServido()
    {
        CreateFolder();
        var client = ClientWithFolder(_folder);

        var response = await client.GetAsync("/drawio/..%2Frastro-drawio-fora.txt");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("fora da pasta", await response.Content.ReadAsStringAsync());
    }
}
