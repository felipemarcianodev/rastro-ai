using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Core.Business;
using Rastro.Mcp;
using Rastro.Tests.Infrastructure;

namespace Rastro.Tests.Api;

// Cenários API-BIZ-NNN e MCP-BIZ-NNN: a visão de negócio pela API e pelo agente (Fase 55), sobre as fixtures da
// visão de negócio extraídas de verdade (NegocioPedidos cancela e pede o estorno à NegocioPagamentos).
[Collection(ApiCollection.Name)]
public class BusinessEndpointTests(ApiFixture fixture) : IAsyncLifetime
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

    private async Task<Guid> SurveyAsync(IServiceScope scope)
    {
        var analysis = await CrossKit.AnalyzeAsync(scope, _pedidos, Entry, 15, _pagamentos);
        return (await CrossKit.SaveSurveyAsync(scope, _pedidos, Entry, 15, analysis, _pagamentos)).Id;
    }

    [Fact] // API-BIZ-001
    public async Task Business_LevantamentoComCostura_DevolvePassosChaveComTipoTextoEReferencia()
    {
        await using var provider = CrossKit.Services(fixture, new FixtureSolutionSource(), _pedidos, _pagamentos);
        await using var scope = provider.CreateAsyncScope();
        var id = await SurveyAsync(scope);

        var response = await Client.GetAsync($"/api/surveys/{id}/business");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        var flow = JsonSerializer.Deserialize<BusinessFlow>(raw, Json)!;

        Assert.Equal(id, flow.SurveyId);
        var start = flow.Steps[0];
        Assert.Equal(BusinessKind.Start, start.Kind);
        Assert.Equal(_pedidos.Name, start.Ref!.Repository);
        Assert.Contains(flow.Steps, s => s.Kind == BusinessKind.Decision);
        Assert.Contains(flow.Steps, s => s.Kind == BusinessKind.Rejection);
        var call = Assert.Single(flow.Steps, s => s.Kind == BusinessKind.CrossSystem);
        Assert.Equal("POST api/estornos", call.Label);
        var email = Assert.Single(flow.Steps, s => s.Kind == BusinessKind.Notification);
        Assert.Equal(_pagamentos.Name, email.Ref!.Repository);
        Assert.All(flow.Steps, s => Assert.Equal(LabelState.Proposed, s.LabelState));
        Assert.All(flow.Edges, e =>
        {
            Assert.Contains(flow.Steps, s => s.Id == e.From);
            Assert.Contains(flow.Steps, s => s.Id == e.To);
        });

        // Enums viajam como int no JSON (regra do repositório).
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal(JsonValueKind.Number, doc.RootElement.GetProperty("steps")[0].GetProperty("kind").ValueKind);
    }

    [Fact] // API-BIZ-002
    public async Task Business_LevantamentoQueNaoExiste_404()
    {
        var response = await Client.GetAsync($"/api/surveys/{Guid.NewGuid()}/business");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact] // MCP-BIZ-001
    public async Task GetBusinessView_LevantamentoComCostura_ListaPassosComApelidoTextoProposto()
    {
        await using var provider = CrossKit.Services(fixture, new FixtureSolutionSource(), _pedidos, _pagamentos);
        await using var scope = provider.CreateAsyncScope();
        var id = await SurveyAsync(scope);
        var tools = scope.ServiceProvider.GetRequiredService<RastroTools>();

        var text = await tools.GetBusinessView(id);

        Assert.Contains("passos-chave", text);
        Assert.Contains("b1 Start", text);
        Assert.Contains("\"POST api/estornos\"", text);
        Assert.Contains("\"Envia e-mail\"", text);
        Assert.Contains("(proposto)", text);
        Assert.Contains($"[{_pagamentos.Name} · ", text);
        Assert.Contains("Ligações:", text);
        Assert.Equal("Levantamento não encontrado ou ainda sem grafo. Use list_surveys.", await tools.GetBusinessView(Guid.NewGuid()));
    }
}
