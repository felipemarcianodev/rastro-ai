using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Api.Controllers;
using Rastro.Core.Graph;
using Rastro.Core.Validation;
using Rastro.Mcp;

namespace Rastro.Tests.Api;

// Cenários API-VAL-NNN e MCP-TOOL-011: validação da mudança, aprovação e documentação.
// O código da fixture não muda entre as análises: um passo alterado continua no fluxo (aprovada)
// e um passo novo não aparece (reprovada).
[Collection(ApiCollection.Name)]
public class ValidationEndpointTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient Client => fixture.Factory.CreateClient();

    private async Task<Guid> SurveyAsync(Func<FlowGraph, FlowGraph>? draft)
    {
        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();
        var analyzed = await scope.ServiceProvider.GetRequiredService<RastroTools>()
            .AnalyzeFlow(ApiFixture.RepositoryName, ApiFixture.SolutionFile, "AlertJob.ExecuteAsync");
        var id = Guid.Parse(analyzed.Split(' ')[1]);
        if (draft is not null)
        {
            var graph = (await Client.GetFromJsonAsync<SurveyDetail>($"/api/surveys/{id}", Json))!.Graph!;
            Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"/api/surveys/{id}/draft", draft(graph), Json)).StatusCode);
        }
        return id;
    }

    private static FlowGraph ModifySend(FlowGraph graph) => graph with
    {
        Nodes = graph.Nodes.Select(n => n.Label == "TelegramAlertSender.SendAsync" ? n with { Change = ChangeKind.Modified, Intent = "Enviar só em dia útil." } : n).ToList()
    };

    private static FlowGraph AddAudit(FlowGraph graph) => graph with
    {
        Nodes = [.. graph.Nodes, new FlowNode("new:audit", NodeKind.Method, "AlertAudit.Registrar", Change: ChangeKind.Added, Intent: "Registrar cada alerta enviado.")],
        Edges = [.. graph.Edges, new FlowEdge(graph.EntryNodeId, "new:audit", 99, EdgeConfidence.Extracted, Change: ChangeKind.Added)]
    };

    private async Task<ValidationView> RunAsync(Guid surveyId)
    {
        var response = await Client.PostAsync($"/api/surveys/{surveyId}/validations", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ValidationView>(Json))!;

        for (var i = 0; i < 150; i++)
        {
            var view = (await Client.GetFromJsonAsync<ValidationView>($"/api/validations/{created.Id}", Json))!;
            if (view.Status is ValidationStatus.Completed or ValidationStatus.Failed) return view;
            await Task.Delay(200);
        }
        throw new TimeoutException("A validação não terminou.");
    }

    [Fact] // API-VAL-001
    public async Task Validar_PassoAlteradoContinuaNoFluxo_AprovadaComLevantamentoNovo()
    {
        var id = await SurveyAsync(ModifySend);

        var view = await RunAsync(id);

        Assert.Equal(ValidationStatus.Completed, view.Status);
        Assert.Equal(ValidationVerdict.Approved, view.Result!.Verdict);
        Assert.Equal(100, view.Result.Score);
        Assert.Equal(CheckStatus.Skipped, view.Result.Build.Status);
        Assert.Empty(view.Result.Unexpected);
        var newSurvey = await Client.GetFromJsonAsync<SurveyDetail>($"/api/surveys/{view.NewSurveyId}", Json);
        Assert.Equal("AlertJob.ExecuteAsync", newSurvey!.Survey.EntryPoint);
        Assert.Equal(await fixture.HeadAsync(), newSurvey.Survey.CommitHash);
    }

    [Fact] // API-VAL-002
    public async Task Aprovar_GeraADocumentacaoEmMarkdown()
    {
        var id = await SurveyAsync(ModifySend);
        var view = await RunAsync(id);

        var approved = await Client.PostAsync($"/api/validations/{view.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.True((await approved.Content.ReadFromJsonAsync<ValidationView>(Json))!.HasDocument);

        var document = await Client.GetAsync($"/api/validations/{view.Id}/document");
        Assert.Equal("text/markdown", document.Content.Headers.ContentType?.MediaType);
        var text = await document.Content.ReadAsStringAsync();
        Assert.Contains("# Fluxo `AlertJob.ExecuteAsync`", text);
        Assert.Contains("Regra mudou em `TelegramAlertSender.SendAsync`: Enviar só em dia útil.", text);
        Assert.Contains("```mermaid", text);
        // O histórico do levantamento lista a validação.
        var list = await Client.GetFromJsonAsync<List<ValidationView>>($"/api/surveys/{id}/validations", Json);
        Assert.Equal(view.Id, Assert.Single(list!).Id);
    }

    [Fact] // API-VAL-003
    public async Task Validar_PassoNovoAusente_Reprovada_ENaoPodeSerAprovada()
    {
        var id = await SurveyAsync(AddAudit);

        var view = await RunAsync(id);

        Assert.Equal(ValidationVerdict.Rejected, view.Result!.Verdict);
        Assert.Equal(0, view.Result.Score);
        var approve = await Client.PostAsync($"/api/validations/{view.Id}/approve", null);
        Assert.Equal(HttpStatusCode.BadRequest, approve.StatusCode);
        Assert.Contains("VALIDATION_NOT_APPROVABLE", await approve.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/validations/{view.Id}/document")).StatusCode);
    }

    [Fact] // API-VAL-004
    public async Task Validar_SemRascunho_Retorna400DraftRequired()
    {
        var id = await SurveyAsync(draft: null);

        var response = await Client.PostAsync($"/api/surveys/{id}/validations", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("DRAFT_REQUIRED", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PostAsync($"/api/surveys/{Guid.NewGuid()}/validations", null)).StatusCode);
    }

    [Fact] // MCP-TOOL-011
    public async Task CheckChange_PeloMcp_RodaBuildETestesConfigurados()
    {
        var id = await SurveyAsync(ModifySend);
        await using var provider = fixture.McpServices(new Dictionary<string, string?>
        {
            // Comandos de verdade e baratos: o build passa e os testes falham.
            ["Rastro:Repositories:0:BuildCommand"] = "git --version",
            ["Rastro:Repositories:0:TestCommand"] = "git comando-que-nao-existe"
        });
        await using var scope = provider.CreateAsyncScope();

        var text = await scope.ServiceProvider.GetRequiredService<RastroTools>().CheckChange(id);

        Assert.Contains("Validação reprovada", text);
        Assert.Contains("Build: passou", text);
        Assert.Contains("Testes: falhou", text);
        Assert.Contains("[ok] TelegramAlertSender.SendAsync continua no fluxo", text);
        Assert.Contains("A aprovação (que gera a documentação) é do usuário", text);
    }
}
