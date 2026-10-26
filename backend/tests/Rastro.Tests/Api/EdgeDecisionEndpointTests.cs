using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Api.Controllers;
using Rastro.Core.Graph;
using Rastro.Core.Surveys;
using Rastro.Infrastructure;
using Rastro.Mcp;
using Rastro.Tests.Extractor;

namespace Rastro.Tests.Api;

// Cenários API-DEC-NNN: o usuário confirma ou descarta, na tela, a aresta que o agente propôs.
[Collection(ApiCollection.Name)]
public class EdgeDecisionEndpointTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Levantamento com a aresta notifier.Notify -> EmailNotifier.Notify proposta pelo agente.
    private async Task<(Guid Id, FlowEdge Edge)> ProposedAsync()
    {
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<RastroTools>();
        var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();

        var analyzed = await tools.AnalyzeFlow(ApiFixture.RepositoryName, ApiFixture.SolutionFile, "AlertJob.ExecuteAsync");
        var id = Guid.Parse(analyzed.Split(' ')[1]);
        var graph = (await db.Surveys.AsNoTracking().SingleAsync(s => s.Id == id)).ReadGraph()!;
        var hole = graph.Nodes.Single(n => n.Label == "notifier.Notify");
        var line = FixtureLines.LineOf("Alerts/Services.cs", "public class EmailNotifier", "public void Notify");
        var result = await tools.ProposeEdge(id, hole.Id, "EmailNotifier.Notify", "Alerts/Services.cs", line, "teste");
        Assert.StartsWith("Aceita.", result);

        var edge = (await db.Surveys.AsNoTracking().SingleAsync(s => s.Id == id)).ReadGraph()!.Edges.Single(e => e.From == hole.Id);
        return (id, edge);
    }

    private async Task<HttpResponseMessage> DecideAsync(Guid id, string from, string to, bool confirm) =>
        await fixture.Factory.CreateClient().PostAsJsonAsync($"/api/surveys/{id}/edges/decision", new { from, to, confirm });

    [Fact] // API-DEC-001
    public async Task Confirmar_ArestaInferida_FicaConfirmadaENoHistorico()
    {
        var (id, edge) = await ProposedAsync();

        var response = await DecideAsync(id, edge.From, edge.To, confirm: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = (await response.Content.ReadFromJsonAsync<SurveyDetail>(Json))!;
        Assert.Equal(EdgeConfidence.Confirmed, detail.Graph!.Edges.Single(e => e.From == edge.From).Confidence);

        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();
        var proposal = await scope.ServiceProvider.GetRequiredService<RastroDbContext>().SurveyProposals.AsNoTracking().SingleAsync(p => p.SurveyId == id);
        Assert.Equal(ProposalStatus.Confirmed, proposal.Status);
        Assert.NotNull(proposal.DecidedAt);
    }

    [Fact] // API-DEC-002
    public async Task Descartar_ArestaInferida_RemoveORamoEReabreOBuraco()
    {
        var (id, edge) = await ProposedAsync();

        var response = await DecideAsync(id, edge.From, edge.To, confirm: false);

        var detail = (await response.Content.ReadFromJsonAsync<SurveyDetail>(Json))!;
        Assert.DoesNotContain(detail.Graph!.Edges, e => e.From == edge.From);
        Assert.DoesNotContain(detail.Graph.Nodes, n => n.Id == edge.To);
        Assert.Contains(detail.Graph.Nodes, n => n.Id == edge.From && n.Kind == NodeKind.Unresolved);
    }

    [Fact] // API-DEC-003
    public async Task Decidir_ArestaExtraidaOuInexistente_Retorna400()
    {
        var (id, _) = await ProposedAsync();
        var detail = await fixture.Factory.CreateClient().GetFromJsonAsync<SurveyDetail>($"/api/surveys/{id}", Json);
        var extracted = detail!.Graph!.Edges.First(e => e.Confidence == EdgeConfidence.Extracted);

        var onExtracted = await DecideAsync(id, extracted.From, extracted.To, confirm: false);
        var missing = await DecideAsync(id, "a", "b", confirm: true);
        var unknownSurvey = await DecideAsync(Guid.NewGuid(), "a", "b", confirm: true);

        Assert.Equal(HttpStatusCode.BadRequest, onExtracted.StatusCode);
        Assert.Equal(ErrorCodes.EdgeNotProposed, (await onExtracted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownSurvey.StatusCode);
    }
}
