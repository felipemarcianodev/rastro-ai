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
using Rastro.Mcp;

namespace Rastro.Tests.Api;

// Cenários API-DRWL-NNN: posições e anotações do draw.io por levantamento e origem (Fases 65 e 66).
[Collection(ApiCollection.Name)]
public class DrawioLayoutEndpointTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Notes = "<root><mxCell id=\"n1\" value=\"Nota\" vertex=\"1\" parent=\"1\"><mxGeometry x=\"1\" y=\"2\" width=\"80\" height=\"30\" as=\"geometry\"/></mxCell></root>";

    private HttpClient Client => fixture.Factory.CreateClient();

    private async Task<Guid> SurveyAsync()
    {
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();
        var analyzed = await scope.ServiceProvider.GetRequiredService<RastroTools>()
            .AnalyzeFlow(ApiFixture.RepositoryName, ApiFixture.SolutionFile, "AlertJob.ExecuteAsync");
        return Guid.Parse(analyzed.Split(' ')[1]);
    }

    private static DrawioLayoutDto Layout(IEnumerable<DrawioCellDto>? cells = null, string? annotations = null) =>
        new((cells ?? []).ToList(), annotations);

    private static DrawioCellDto Cell(string id, double x = 1, double y = 2, double w = 100, double h = 40) => new(id, x, y, w, h);

    private Task<HttpResponseMessage> PutAsync(Guid id, DrawioLayoutDto layout, int source = 2) =>
        Client.PutAsJsonAsync($"/api/surveys/{id}/drawio/{source}", layout, Json);

    private Task<DrawioLayoutDto?> ReadAsync(Guid id, int source = 2) =>
        Client.GetFromJsonAsync<DrawioLayoutDto>($"/api/surveys/{id}/drawio/{source}", Json);

    [Fact] // API-DRWL-001
    public async Task Drawio_SemSalvo_DevolveVazio()
    {
        var id = await SurveyAsync();

        var read = await ReadAsync(id);

        Assert.Empty(read!.Cells);
        Assert.Null(read.Annotations);
        Assert.Null(read.UpdatedAt);
    }

    [Fact] // API-DRWL-002
    public async Task Drawio_SalvaELeDepois_JsonEmCamelCase()
    {
        var id = await SurveyAsync();

        var saved = await PutAsync(id, Layout([Cell("b1", 10.5, -20, 120, 60)], Notes));
        var raw = await Client.GetStringAsync($"/api/surveys/{id}/drawio/2");
        var read = await ReadAsync(id);

        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        using var doc = JsonDocument.Parse(raw);
        var cell = doc.RootElement.GetProperty("cells")[0];
        Assert.Equal("b1", cell.GetProperty("rastroId").GetString());
        Assert.Equal(10.5, cell.GetProperty("x").GetDouble());
        Assert.Equal(60, cell.GetProperty("height").GetDouble());
        Assert.Equal(Notes, doc.RootElement.GetProperty("annotations").GetString());
        Assert.NotNull(read!.UpdatedAt);
    }

    [Fact] // API-DRWL-003
    public async Task Drawio_PutTrocaTudoEOrigensSaoIndependentes()
    {
        var id = await SurveyAsync();
        await PutAsync(id, Layout([Cell("b1"), Cell("b2")], Notes), 2);
        await PutAsync(id, Layout([Cell("g1")]), 0);

        await PutAsync(id, Layout([Cell("b3", 7, 8)]), 2);

        var business = await ReadAsync(id, 2);
        Assert.Equal(["b3"], business!.Cells.Select(c => c.RastroId));
        Assert.Null(business.Annotations);
        Assert.Equal(["g1"], (await ReadAsync(id, 0))!.Cells.Select(c => c.RastroId));
        Assert.Empty((await ReadAsync(id, 1))!.Cells);
    }

    [Fact] // API-DRWL-004
    public async Task Drawio_Reorganizar_ApagaSoAquelaOrigem()
    {
        var id = await SurveyAsync();
        await PutAsync(id, Layout([Cell("b1")], Notes), 2);
        await PutAsync(id, Layout([Cell("m1")]), 1);

        var deleted = await Client.DeleteAsync($"/api/surveys/{id}/drawio/2");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var read = await ReadAsync(id, 2);
        Assert.Empty(read!.Cells);
        Assert.Null(read.Annotations);
        Assert.Single((await ReadAsync(id, 1))!.Cells);
    }

    [Fact] // API-DRWL-005
    public async Task Drawio_LevantamentoInexistente_Devolve404NasTresRotas()
    {
        var missing = Guid.NewGuid();

        var get = await Client.GetAsync($"/api/surveys/{missing}/drawio/2");
        var put = await PutAsync(missing, Layout([Cell("b1")]));
        var delete = await Client.DeleteAsync($"/api/surveys/{missing}/drawio/2");

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    public static TheoryData<string, DrawioLayoutDto> Invalid()
    {
        var cells2001 = Enumerable.Range(0, 2001).Select(i => Cell($"c{i}")).ToList();
        return new TheoryData<string, DrawioLayoutDto>
        {
            { "2001 células", new(cells2001, null) },
            { "id com 129", Layout([Cell(new string('a', 129))]) },
            { "id vazio", Layout([Cell(" ")]) },
            { "x fora da faixa", Layout([Cell("a", x: 100001)]) },
            { "y fora da faixa", Layout([Cell("a", y: -100001)]) },
            { "largura zero", Layout([Cell("a", w: 0)]) },
            { "altura acima de 10000", Layout([Cell("a", h: 10001)]) },
            { "XML malformado", Layout(annotations: "<root><mxCell></root>") },
            { "raiz errada", Layout(annotations: "<mxGraphModel><root/></mxGraphModel>") },
            { "UserObject com rastroId", Layout(annotations: "<root><UserObject label=\"x\" rastroId=\"b1\"><mxCell vertex=\"1\" parent=\"1\"/></UserObject></root>") },
            { "mxCell com rastroId", Layout(annotations: "<root><mxCell id=\"a\" rastroId=\"b1\"/></root>") },
            { "DTD", Layout(annotations: "<!DOCTYPE root [<!ENTITY e \"x\">]><root><mxCell id=\"a\" value=\"&e;\"/></root>") },
            { "anotação com mais de 512 KB", Layout(annotations: "<root><mxCell id=\"a\" value=\"" + new string('x', 512 * 1024) + "\"/></root>") },
        };
    }

    [Theory] // API-DRWL-006
    [MemberData(nameof(Invalid))]
    public async Task Drawio_ForaDosLimites_Recusa400ComCodigoENadaGrava(string cenario, DrawioLayoutDto layout)
    {
        var id = await SurveyAsync();

        var response = await PutAsync(id, layout);
        var read = await ReadAsync(id);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, cenario);
        Assert.Contains(ErrorCodes.DrawioLayoutInvalid, await response.Content.ReadAsStringAsync());
        Assert.Empty(read!.Cells);
        Assert.Null(read.Annotations);
    }

    [Fact] // API-DRWL-007
    public async Task Drawio_OrigemForaDoEnum_Recusa400ComCodigo()
    {
        var id = await SurveyAsync();

        foreach (var source in new[] { "3", "-1", "abc" })
        {
            var get = await Client.GetAsync($"/api/surveys/{id}/drawio/{source}");
            var put = await Client.PutAsJsonAsync($"/api/surveys/{id}/drawio/{source}", Layout([Cell("a")]), Json);
            var delete = await Client.DeleteAsync($"/api/surveys/{id}/drawio/{source}");

            foreach (var response in new[] { get, put, delete })
            {
                Assert.True(response.StatusCode == HttpStatusCode.BadRequest, source);
                Assert.Contains(ErrorCodes.DrawioLayoutInvalid, await response.Content.ReadAsStringAsync());
            }
        }
    }

    [Fact] // API-DRWL-008
    public void Limites_NaoFinito_Recusa()
    {
        // NaN e infinito não têm representação em JSON, mas o validador também os recusa.
        Assert.False(DrawioLayoutLimits.IsValid(Layout([Cell("a", x: double.NaN)])));
        Assert.False(DrawioLayoutLimits.IsValid(Layout([Cell("a", w: double.PositiveInfinity)])));
        Assert.True(DrawioLayoutLimits.IsValid(Layout([Cell("a", -100000, 100000, 10000, 10000)], Notes)));
    }

    [Fact] // API-DRWL-009
    public async Task Drawio_NosLimites_Aceita()
    {
        var id = await SurveyAsync();
        var cells = Enumerable.Range(0, 2000).Select(i => Cell($"c{i}")).ToList();

        var response = await PutAsync(id, new DrawioLayoutDto(cells, Notes));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(2000, (await ReadAsync(id))!.Cells.Count);
    }

    [Fact] // API-DRWL-010
    public async Task Drawio_ExcluirLevantamento_ApagaSoOsDele()
    {
        var doomed = await SurveyAsync();
        var kept = await SurveyAsync();
        await PutAsync(doomed, Layout([Cell("b1")], Notes));
        await PutAsync(kept, Layout([Cell("b1")]));

        var deleted = await Client.DeleteAsync($"/api/surveys/{doomed}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();
        Assert.False(await db.DrawioLayouts.AnyAsync(l => l.SurveyId == doomed));
        Assert.True(await db.DrawioLayouts.AnyAsync(l => l.SurveyId == kept));
    }
}

// Cenário API-DRWL-011: a migration do draw.io guarda o que já havia (posição da visão de negócio da Fase 58).
public class DrawioMigrationTests
{
    [Fact] // API-DRWL-011
    public async Task Migration_BancoComPosicaoDaFase58_MantemOsDadosECriaATabela()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rastro-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var options = new DbContextOptionsBuilder<RastroDbContext>();
            RastroDbContext.Configure(options, $"Data Source={Path.Combine(folder, "rastro.db")};Pooling=False");
            await using (var db = new RastroDbContext(options.Options))
            {
                await db.GetService<IMigrator>().MigrateAsync("20261126134928_QuadroDeFluxos");
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO business_layouts (scope, scope_id, lane_survey_id, step_id, x, y, updated_at) VALUES (0, '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000000', 'b1', 12.5, -3, 1)");
            }

            await using (var db = new RastroDbContext(options.Options))
            {
                await db.Database.MigrateAsync();
                var step = await db.BusinessStepLayouts.Select(s => new { s.StepId, s.X, s.Y }).SingleAsync();
                Assert.Equal(("b1", 12.5, -3d), (step.StepId, step.X, step.Y));
                Assert.Empty(await db.DrawioLayouts.ToListAsync());
            }
        }
        finally
        {
            try { Directory.Delete(folder, true); }
            catch (IOException) { /* arquivo temporário do teste: sobra de uma pasta temporária, não afeta o resultado. */ }
        }
    }
}
