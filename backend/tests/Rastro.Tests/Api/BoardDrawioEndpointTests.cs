using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Api.Controllers;
using Rastro.Core.Business;
using Rastro.Infrastructure;
using Rastro.Infrastructure.Business;
using Rastro.Mcp;

namespace Rastro.Tests.Api;

// Cenários API-BDRW-NNN: anotações do quadro de fluxos no draw.io (Fase 68).
[Collection(ApiCollection.Name)]
public class BoardDrawioEndpointTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Notes = "<root><mxCell id=\"n1\" value=\"Nota\" vertex=\"1\" parent=\"1\"><mxGeometry x=\"1\" y=\"2\" width=\"80\" height=\"30\" as=\"geometry\"/></mxCell></root>";

    private HttpClient Client => fixture.Factory.CreateClient();

    private async Task<Guid> BoardAsync()
    {
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();
        var analyzed = await scope.ServiceProvider.GetRequiredService<RastroTools>()
            .AnalyzeFlow(ApiFixture.RepositoryName, ApiFixture.SolutionFile, "AlertJob.ExecuteAsync");
        var surveyId = Guid.Parse(analyzed.Split(' ')[1]);
        var created = await Client.PostAsJsonAsync("/api/boards", new { name = "Quadro de teste", surveyIds = new[] { surveyId } }, Json);
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<BoardDetail>(Json))!.Id;
    }

    private Task<HttpResponseMessage> PutAsync(Guid id, string? annotations) =>
        Client.PutAsJsonAsync($"/api/boards/{id}/drawio", new { annotations }, Json);

    private async Task<string?> ReadAsync(Guid id)
    {
        using var doc = JsonDocument.Parse(await Client.GetStringAsync($"/api/boards/{id}/drawio"));
        // A API omite campo nulo no JSON: sem "annotations" quer dizer nada guardado.
        return doc.RootElement.TryGetProperty("annotations", out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
    }

    [Fact] // API-BDRW-001
    public async Task Drawio_SalvaELeDepois_NuloQuandoNadaGuardado()
    {
        var id = await BoardAsync();
        Assert.Null(await ReadAsync(id));

        var saved = await PutAsync(id, Notes);

        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal(Notes, await ReadAsync(id));

        await PutAsync(id, null);
        Assert.Null(await ReadAsync(id));
    }

    public static TheoryData<string, string> Invalid() => new()
    {
        { "XML malformado", "<root><mxCell></root>" },
        { "raiz errada", "<mxGraphModel><root/></mxGraphModel>" },
        { "mxCell com rastroId", "<root><mxCell id=\"a\" rastroId=\"b1\"/></root>" },
        { "UserObject com rastroId", "<root><UserObject label=\"x\" rastroId=\"b1\"><mxCell vertex=\"1\" parent=\"1\"/></UserObject></root>" },
        { "DTD", "<!DOCTYPE root [<!ENTITY e \"x\">]><root><mxCell id=\"a\" value=\"&e;\"/></root>" },
        { "mais de 512 KB", "<root><mxCell id=\"a\" value=\"" + new string('x', 512 * 1024) + "\"/></root>" },
    };

    [Theory] // API-BDRW-002
    [MemberData(nameof(Invalid))]
    public async Task Drawio_AnotacaoInvalida_Recusa400ComCodigoENadaGrava(string cenario, string annotations)
    {
        var id = await BoardAsync();
        await PutAsync(id, Notes);

        var response = await PutAsync(id, annotations);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, cenario);
        Assert.Contains(ErrorCodes.DrawioLayoutInvalid, await response.Content.ReadAsStringAsync());
        Assert.Equal(Notes, await ReadAsync(id));
    }

    [Fact] // API-BDRW-003
    public async Task Drawio_QuadroInexistente_Devolve404NasDuasRotas()
    {
        var missing = Guid.NewGuid();

        var get = await Client.GetAsync($"/api/boards/{missing}/drawio");
        var put = await PutAsync(missing, Notes);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Contains(ErrorCodes.BoardNotFound, await put.Content.ReadAsStringAsync());
    }

    [Fact] // API-BDRW-004
    public async Task Drawio_ExcluirQuadro_ApagaJuntoESoDele()
    {
        var doomed = await BoardAsync();
        var kept = await BoardAsync();
        await PutAsync(doomed, Notes);
        await PutAsync(kept, Notes);

        var deleted = await Client.DeleteAsync($"/api/boards/{doomed}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/boards/{doomed}/drawio")).StatusCode);
        Assert.Equal(Notes, await ReadAsync(kept));
    }
}

// Cenário API-BDRW-005: a migration da Fase 68 mantém os quadros que já existiam.
public class BoardDrawioMigrationTests
{
    [Fact] // API-BDRW-005
    public async Task Migration_BancoComQuadros_MantemOsDadosEAnotacaoComecaNula()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rastro-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var options = new DbContextOptionsBuilder<RastroDbContext>();
            RastroDbContext.Configure(options, $"Data Source={Path.Combine(folder, "rastro.db")};Pooling=False");
            await using (var db = new RastroDbContext(options.Options))
            {
                await db.GetService<IMigrator>().MigrateAsync("20261127201532_DrawioLayout");
                var now = (long)new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter().ConvertToProvider(DateTimeOffset.UtcNow)!;
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO boards (id, name, glossary, created_at, updated_at) VALUES ('22222222-2222-2222-2222-222222222222', 'Quadro antigo', 'Termo: significado', {0}, {0})", now);
            }

            await using (var db = new RastroDbContext(options.Options))
            {
                await db.Database.MigrateAsync();
                var board = await db.Boards.SingleAsync();
                Assert.Equal(("Quadro antigo", "Termo: significado"), (board.Name, board.Glossary));
                Assert.Null(board.DrawioAnnotations);
            }
        }
        finally
        {
            try { Directory.Delete(folder, true); }
            catch (IOException) { /* arquivo temporário do teste: sobra de uma pasta temporária, não afeta o resultado. */ }
        }
    }
}
