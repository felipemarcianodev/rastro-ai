using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Core.Business;
using Rastro.Infrastructure;
using Rastro.Infrastructure.Business;
using Rastro.Infrastructure.Processes;
using Rastro.Mcp;
using Rastro.Tests.Infrastructure;

namespace Rastro.Tests.Api;

// Cenários API-BTX-NNN e MCP-BTX-NNN: texto e tipo gravados dos passos da visão de negócio (Fase 59), pela API e
// pelo agente. O levantamento é o das fixtures da visão de negócio (NegocioPedidos cancela e pede o estorno à
// NegocioPagamentos): tem decisão, recusa, passo de gravação, chamada entre sistemas e aviso.
[Collection(ApiCollection.Name)]
public class BusinessTextEndpointTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Entry = "PedidosController.Cancelar";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private CrossRepo _pedidos = null!;
    private CrossRepo _pagamentos = null!;

    public async Task InitializeAsync()
    {
        _pedidos = await CrossRepo.CreateAsync("pedidos", "NegocioPedidos");
        _pagamentos = await CrossRepo.CreateAsync("pagamentos", "NegocioPagamentos");
    }

    public async Task DisposeAsync()
    {
        await _pedidos.DisposeAsync();
        await _pagamentos.DisposeAsync();
    }

    private HttpClient Client => fixture.Factory.CreateClient();

    // Serviços com os repositórios temporários no cadastro (a API de teste não os tem): o código do passo se lê.
    private ServiceProvider Services() => CrossKit.Services(fixture, new FixtureSolutionSource(), _pedidos, _pagamentos);

    private async Task<Guid> SurveyAsync()
    {
        await using var provider = Services();
        await using var scope = provider.CreateAsyncScope();
        var analysis = await CrossKit.AnalyzeAsync(scope, _pedidos, Entry, 15, _pagamentos);
        return (await CrossKit.SaveSurveyAsync(scope, _pedidos, Entry, 15, analysis, _pagamentos)).Id;
    }

    private async Task<BusinessFlow> FlowAsync(Guid id) =>
        (await Client.GetFromJsonAsync<BusinessFlow>($"/api/surveys/{id}/business", Json))!;

    private static BusinessStep Decision(BusinessFlow flow) => flow.Steps.First(s => s.Kind == BusinessKind.Decision);

    private Task<HttpResponseMessage> PutLabelAsync(Guid id, string stepId, string? text) =>
        Client.PutAsJsonAsync($"/api/surveys/{id}/business/steps/{stepId}/label", new { text });

    private Task<HttpResponseMessage> PutKindAsync(Guid id, string stepId, int? kind) =>
        Client.PutAsJsonAsync($"/api/surveys/{id}/business/steps/{stepId}/kind", new { kind });

    private async Task<T> ToolAsync<T>(Func<RastroTools, Task<T>> call)
    {
        await using var provider = Services();
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<RastroTools>());
    }

    [Fact] // API-BTX-001
    public async Task PutLabel_TextoDoUsuario_FicaConfirmadoNaVisao()
    {
        var id = await SurveyAsync();
        var step = Decision(await FlowAsync(id));

        var response = await PutLabelAsync(id, step.Id, "  O alerta está silenciado?  ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<BusinessStep>(Json))!;
        Assert.Equal("O alerta está silenciado?", saved.Label);
        Assert.Equal(LabelOrigin.User, saved.LabelOrigin);
        Assert.Equal(LabelState.Confirmed, saved.LabelState);
        Assert.False(saved.CodeChanged);
        var again = (await FlowAsync(id)).Steps.Single(s => s.Id == step.Id);
        Assert.Equal("O alerta está silenciado?", again.Label);
        Assert.Equal(LabelState.Confirmed, again.LabelState);
        using var doc = JsonDocument.Parse(await Client.GetStringAsync($"/api/surveys/{id}/business"));
        var raw = doc.RootElement.GetProperty("steps").EnumerateArray().First(e => e.GetProperty("id").GetString() == step.Id);
        Assert.Equal(JsonValueKind.Number, raw.GetProperty("labelOrigin").ValueKind);
        Assert.False(raw.GetProperty("codeChanged").GetBoolean());
        // Sem proposta, o campo vem nulo ou nem vem (a API omite nulos).
        Assert.True(!raw.TryGetProperty("kindProposal", out var proposal) || proposal.ValueKind == JsonValueKind.Null);
    }

    [Fact] // API-BTX-002
    public async Task PutLabel_PeloIdDoNoCodificadoNaUrl_Resolve()
    {
        var id = await SurveyAsync();
        var step = Decision(await FlowAsync(id));

        var response = await PutLabelAsync(id, Uri.EscapeDataString(step.NodeId), "Pelo nó?");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Pelo nó?", (await FlowAsync(id)).Steps.Single(s => s.Id == step.Id).Label);
    }

    [Fact] // API-BTX-003
    public async Task PutLabel_TextoInvalidoOuPassoAusente_ErroComCodigo()
    {
        var id = await SurveyAsync();
        var step = Decision(await FlowAsync(id));

        var longo = await PutLabelAsync(id, step.Id, new string('x', 121));
        var quebra = await PutLabelAsync(id, step.Id, "linha 1\nlinha 2");
        var semPasso = await PutLabelAsync(id, "0000000000000000", "Texto");
        var semNo = await PutLabelAsync(id, "no-que-nao-existe", "Texto");
        var semLevantamento = await PutLabelAsync(Guid.NewGuid(), step.Id, "Texto");

        Assert.Equal(HttpStatusCode.BadRequest, longo.StatusCode);
        Assert.Contains("BUSINESS_LABEL_INVALID", await longo.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, quebra.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, semPasso.StatusCode);
        Assert.Contains("BUSINESS_STEP_NOT_FOUND", await semPasso.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, semNo.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, semLevantamento.StatusCode);
        Assert.Equal(LabelState.Proposed, (await FlowAsync(id)).Steps.Single(s => s.Id == step.Id).LabelState);
    }

    [Fact] // API-BTX-004
    public async Task PutLabel_TextoVazio_VoltaAoTextoDaRegra()
    {
        var id = await SurveyAsync();
        var step = Decision(await FlowAsync(id));
        await PutLabelAsync(id, step.Id, "Meu texto?");

        var response = await PutLabelAsync(id, step.Id, "  ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var back = (await response.Content.ReadFromJsonAsync<BusinessStep>(Json))!;
        Assert.Equal(step.Label, back.Label);
        Assert.Equal(LabelOrigin.Rule, back.LabelOrigin);
        Assert.Equal(LabelState.Proposed, back.LabelState);
    }

    [Fact] // API-BTX-005
    public async Task ConfirmLabel_PropostaDaRegra_GravaOTextoDaRegraConfirmado()
    {
        var id = await SurveyAsync();
        var step = Decision(await FlowAsync(id));

        var response = await Client.PostAsync($"/api/surveys/{id}/business/steps/{step.Id}/label/confirm", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var confirmed = (await response.Content.ReadFromJsonAsync<BusinessStep>(Json))!;
        Assert.Equal(step.Label, confirmed.Label);
        Assert.Equal(LabelOrigin.Rule, confirmed.LabelOrigin);
        Assert.Equal(LabelState.Confirmed, confirmed.LabelState);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Client.PostAsync($"/api/surveys/{id}/business/steps/nao-existe/label/confirm", null)).StatusCode);
    }

    [Fact] // API-BTX-012
    public async Task VisaoDeNegocio_SegundaLeituraVemDoCache_ETipoGravadoOuIdiomaTrocamAChave()
    {
        var id = await SurveyAsync();
        await using var provider = Services();
        await using var scope = provider.CreateAsyncScope();
        var view = scope.ServiceProvider.GetRequiredService<BusinessViewService>();
        var annotations = scope.ServiceProvider.GetRequiredService<BusinessAnnotationService>();
        var pt = Rastro.Core.Localization.UiLanguage.Portuguese;

        var first = (await view.GetAsync(id, pt, CancellationToken.None, applyLabels: false))!;
        var again = (await view.GetAsync(id, pt, CancellationToken.None, applyLabels: false))!;
        Assert.Same(first, again);

        var english = (await view.GetAsync(id, Rastro.Core.Localization.UiLanguage.English, CancellationToken.None, applyLabels: false))!;
        Assert.NotSame(first, english);

        // O tipo gravado muda a derivação: a leitura seguinte não pode vir da entrada antiga.
        var decision = Decision(first);
        var changed = await annotations.SetKindAsync(id, decision.Id, (int)BusinessKind.Step, pt, CancellationToken.None);
        Assert.True(changed.Ok);
        var after = (await view.GetAsync(id, pt, CancellationToken.None, applyLabels: false))!;
        Assert.NotSame(first, after);
        Assert.Equal(BusinessKind.Step, after.Steps.Single(s => s.NodeId == decision.NodeId).Kind);
    }

    [Fact] // API-BTX-006
    public async Task CodigoMudou_HashGravadoDiferente_MarcaCodeChangedEConfirmarRenovaOHash()
    {
        var id = await SurveyAsync();
        await using var provider = Services();
        await using var scope = provider.CreateAsyncScope();
        var view = scope.ServiceProvider.GetRequiredService<BusinessViewService>();
        var annotations = scope.ServiceProvider.GetRequiredService<BusinessAnnotationService>();
        var step = Decision((await view.GetAsync(id, Rastro.Core.Localization.UiLanguage.Portuguese, CancellationToken.None))!);

        var saved = await annotations.SetLabelAsync(id, step.Id, "Texto confirmado?", Rastro.Core.Localization.UiLanguage.Portuguese, CancellationToken.None);
        Assert.True(saved.Ok);
        Assert.False(saved.Step!.CodeChanged);

        var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();
        var row = await db.BusinessLabels.SingleAsync(l => l.SurveyId == id && l.NodeId == step.NodeId);
        Assert.Equal(64, row.CodeHash.Length);
        row.Set("hash-de-um-codigo-antigo", row.Text, row.Origin, row.State, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        var stale = (await view.GetAsync(id, Rastro.Core.Localization.UiLanguage.Portuguese, CancellationToken.None))!.Steps.Single(s => s.Id == step.Id);
        Assert.True(stale.CodeChanged);
        Assert.Equal("Texto confirmado?", stale.Label);

        var reconfirmed = await annotations.ConfirmLabelAsync(id, step.Id, Rastro.Core.Localization.UiLanguage.Portuguese, CancellationToken.None);
        Assert.False(reconfirmed.Step!.CodeChanged);
        Assert.Equal("Texto confirmado?", reconfirmed.Step.Label);
        Assert.Equal(LabelState.Confirmed, reconfirmed.Step.LabelState);
    }

    [Fact] // API-BTX-007
    public async Task PutKind_NoQueNaoEraPasso_ViraPassoChaveEDepoisVoltaAoDetectado()
    {
        var id = await SurveyAsync();
        var flow = await FlowAsync(id);
        var detail = (await Client.GetFromJsonAsync<Rastro.Api.Controllers.SurveyDetail>($"/api/surveys/{id}", Json))!;
        var node = detail.Graph!.Nodes.First(n => n.Kind == Rastro.Core.Graph.NodeKind.Method && flow.Steps.All(s => s.NodeId != n.Id));

        var shown = await PutKindAsync(id, Uri.EscapeDataString(node.Id), (int)BusinessKind.Step);

        Assert.Equal(HttpStatusCode.OK, shown.StatusCode);
        var withNode = (await shown.Content.ReadFromJsonAsync<BusinessFlow>(Json))!;
        var step = Assert.Single(withNode.Steps, s => s.NodeId == node.Id);
        Assert.Equal(BusinessKind.Step, step.Kind);
        Assert.Contains(withNode.Edges, e => e.To == step.Id);

        var back = (await (await PutKindAsync(id, step.Id, null)).Content.ReadFromJsonAsync<BusinessFlow>(Json))!;
        Assert.DoesNotContain(back.Steps, s => s.NodeId == node.Id);
    }

    [Fact] // API-BTX-008
    public async Task PutKind_TrocaOTipoDeUmPassoEValidaOValor()
    {
        var id = await SurveyAsync();
        var decision = Decision(await FlowAsync(id));

        var changed = (await (await PutKindAsync(id, decision.Id, (int)BusinessKind.Notification)).Content.ReadFromJsonAsync<BusinessFlow>(Json))!;
        var invalid = await PutKindAsync(id, decision.Id, 99);
        var absent = await PutKindAsync(id, "0000000000000000", (int)BusinessKind.Step);

        Assert.Equal(BusinessKind.Notification, changed.Steps.Single(s => s.Id == decision.Id).Kind);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("BUSINESS_KIND_INVALID", await invalid.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Contains("BUSINESS_STEP_NOT_FOUND", await absent.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await PutKindAsync(Guid.NewGuid(), decision.Id, 1)).StatusCode);
    }

    [Fact] // API-BTX-009
    public async Task PropostaDeTipoDoAgente_NaoMudaODesenhoAteConfirmar_EPodeSerDescartada()
    {
        var id = await SurveyAsync();
        var decision = Decision(await FlowAsync(id));

        var said = await ToolAsync(t => t.ProposeBusinessKind(id, decision.Id, "Notification", "O ramo envia aviso."));

        Assert.Contains("proposto", said);
        var proposed = (await FlowAsync(id)).Steps.Single(s => s.Id == decision.Id);
        Assert.Equal(BusinessKind.Decision, proposed.Kind);
        Assert.Equal(new KindProposal(BusinessKind.Notification, "O ramo envia aviso."), proposed.KindProposal);

        var confirmed = (await (await Client.PostAsync($"/api/surveys/{id}/business/steps/{decision.Id}/kind/confirm", null))
            .Content.ReadFromJsonAsync<BusinessFlow>(Json))!;
        var step = confirmed.Steps.Single(s => s.Id == decision.Id);
        Assert.Equal(BusinessKind.Notification, step.Kind);
        Assert.Null(step.KindProposal);

        // Sem proposta pendente, confirmar e descartar recusam.
        var noConfirm = await Client.PostAsync($"/api/surveys/{id}/business/steps/{decision.Id}/kind/confirm", null);
        var noDismiss = await Client.PostAsync($"/api/surveys/{id}/business/steps/{decision.Id}/kind/dismiss", null);
        Assert.Equal(HttpStatusCode.NotFound, noConfirm.StatusCode);
        Assert.Contains("BUSINESS_PROPOSAL_NOT_FOUND", await noConfirm.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, noDismiss.StatusCode);
    }

    [Fact] // API-BTX-010
    public async Task PropostaDeTipoDescartada_VoltaAoDesenhoSemProposta()
    {
        var id = await SurveyAsync();
        var decision = Decision(await FlowAsync(id));
        await ToolAsync(t => t.ProposeBusinessKind(id, decision.Id, "Step", "Grava algo."));

        var dismissed = (await (await Client.PostAsync($"/api/surveys/{id}/business/steps/{decision.Id}/kind/dismiss", null))
            .Content.ReadFromJsonAsync<BusinessFlow>(Json))!;

        var step = dismissed.Steps.Single(s => s.Id == decision.Id);
        Assert.Equal(BusinessKind.Decision, step.Kind);
        Assert.Null(step.KindProposal);
    }

    [Fact] // API-BTX-011
    public async Task ExcluirLevantamento_LevaOTextoEOTipoGravadosJunto()
    {
        var id = await SurveyAsync();
        var decision = Decision(await FlowAsync(id));
        await PutLabelAsync(id, decision.Id, "Vai embora?");
        await PutKindAsync(id, decision.Id, (int)BusinessKind.Step);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/surveys/{id}")).StatusCode);

        await using var provider = fixture.McpServices();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RastroDbContext>();
        Assert.False(await db.BusinessLabels.AnyAsync(l => l.SurveyId == id));
        Assert.False(await db.BusinessKindOverrides.AnyAsync(k => k.SurveyId == id));
    }

    // ---------- agente (MCP) ----------

    [Fact] // MCP-BTX-001
    public async Task ProposeBusinessLabel_ApelidoDoPasso_GravaPropostaDeAgenteQueAparece()
    {
        var id = await SurveyAsync();
        var flow = await FlowAsync(id);
        var index = flow.Steps.ToList().FindIndex(s => s.Kind == BusinessKind.Decision);

        var said = await ToolAsync(t => t.ProposeBusinessLabel(id, $"b{index + 1}", "O alerta foi silenciado?"));

        Assert.Contains("O alerta foi silenciado?", said);
        var step = (await FlowAsync(id)).Steps[index];
        Assert.Equal("O alerta foi silenciado?", step.Label);
        Assert.Equal(LabelOrigin.Agent, step.LabelOrigin);
        Assert.Equal(LabelState.Proposed, step.LabelState);
        var view = await ToolAsync(t => t.GetBusinessView(id));
        Assert.Contains("\"O alerta foi silenciado?\"", view);
        Assert.Contains("(proposto)", view);
    }

    [Fact] // MCP-BTX-002
    public async Task ProposeBusinessLabel_TextoQueOUsuarioJaConfirmou_NaoMuda()
    {
        var id = await SurveyAsync();
        var step = Decision(await FlowAsync(id));
        await PutLabelAsync(id, step.Id, "Texto do usuário?");

        var said = await ToolAsync(t => t.ProposeBusinessLabel(id, step.Id, "Texto do agente?"));

        Assert.Contains("já confirmou", said);
        var current = (await FlowAsync(id)).Steps.Single(s => s.Id == step.Id);
        Assert.Equal("Texto do usuário?", current.Label);
        Assert.Equal(LabelState.Confirmed, current.LabelState);
    }

    [Fact] // MCP-BTX-003
    public async Task ProposeBusinessTools_EntradaInvalida_RespondeComMensagemSemGravar()
    {
        var id = await SurveyAsync();
        var step = Decision(await FlowAsync(id));

        Assert.Contains("Texto inválido", await ToolAsync(t => t.ProposeBusinessLabel(id, step.Id, new string('x', 121))));
        Assert.Contains("Texto inválido", await ToolAsync(t => t.ProposeBusinessLabel(id, step.Id, "  ")));
        Assert.Contains("Passo não encontrado", await ToolAsync(t => t.ProposeBusinessLabel(id, "b999", "Texto")));
        Assert.Contains("Tipo inválido", await ToolAsync(t => t.ProposeBusinessKind(id, step.Id, "Inexistente", "Motivo.")));
        Assert.Contains("Motivo inválido", await ToolAsync(t => t.ProposeBusinessKind(id, step.Id, "Step", " ")));
        Assert.Contains("Levantamento não encontrado", await ToolAsync(t => t.ProposeBusinessLabel(Guid.NewGuid(), "b1", "Texto")));
        var current = (await FlowAsync(id)).Steps.Single(s => s.Id == step.Id);
        Assert.Equal(step.Label, current.Label);
        Assert.Null(current.KindProposal);
    }

    [Fact] // MCP-BTX-004
    public async Task ProposeBusinessKind_TipoQueOUsuarioJaConfirmou_NaoMuda()
    {
        var id = await SurveyAsync();
        var step = Decision(await FlowAsync(id));
        await PutKindAsync(id, step.Id, (int)BusinessKind.Notification);

        var said = await ToolAsync(t => t.ProposeBusinessKind(id, step.Id, "Rejection", "Parece recusa."));

        Assert.Contains("já confirmou", said);
        var current = (await FlowAsync(id)).Steps.Single(s => s.Id == step.Id);
        Assert.Equal(BusinessKind.Notification, current.Kind);
        Assert.Null(current.KindProposal);
    }

    [Fact] // MCP-BTX-005
    public void Ferramentas_DeNegocio_NaoTemComoConfirmarNemDescartar()
    {
        var names = typeof(RastroTools).GetMethods()
            .Select(m => m.Name)
            .Where(n => n.Contains("Business", StringComparison.Ordinal))
            .ToList();

        Assert.Contains("ProposeBusinessLabel", names);
        Assert.Contains("ProposeBusinessKind", names);
        Assert.DoesNotContain(names, n => n.Contains("Confirm", StringComparison.Ordinal) || n.Contains("Dismiss", StringComparison.Ordinal)
                                          || n.Contains("Accept", StringComparison.Ordinal));
        Assert.Contains("you cannot confirm or dismiss your own proposal", Rastro.Mcp.McpInstructions.Text, StringComparison.Ordinal);
    }
}
