using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastro.Core.Graph;
using Rastro.Core.Surveys;
using Rastro.Infrastructure;
using Rastro.Mcp;
using Rastro.Tests.Api;
using Rastro.Tests.Extractor;

namespace Rastro.Tests.Mcp;

// Cenários MCP-TOOL-NNN: o modo agente de ponta a ponta (ferramentas, propostas, banco real).
[Collection(ApiCollection.Name)]
public class RastroToolsTests(ApiFixture fixture)
{
    private const string Services = "Alerts/Services.cs";

    private sealed class Session : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        public Session(ApiFixture fixture)
        {
            _provider = fixture.McpServices();
            _scope = _provider.CreateAsyncScope();
        }

        public RastroTools Tools => _scope.ServiceProvider.GetRequiredService<RastroTools>();
        public RastroDbContext Db => _scope.ServiceProvider.GetRequiredService<RastroDbContext>();

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
        }
    }

    private static async Task<Guid> AnalyzeAsync(Session session)
    {
        var result = await session.Tools.AnalyzeFlow(ApiFixture.RepositoryName, ApiFixture.SolutionFile, "AlertJob.ExecuteAsync");
        Assert.StartsWith("Levantamento ", result);
        return Guid.Parse(result.Split(' ')[1]);
    }

    private static async Task<(FlowGraph Graph, string Handle)> HoleAsync(Session session, Guid id, string label)
    {
        var survey = await session.Db.Surveys.AsNoTracking().SingleAsync(s => s.Id == id);
        var graph = survey.ReadGraph()!;
        var index = graph.Nodes.ToList().FindIndex(n => n.Kind == NodeKind.Unresolved && n.Label == label);
        Assert.True(index >= 0);
        return (graph, $"n{index + 1}");
    }

    private static int EmailNotifyLine => FixtureLines.LineOf(Services, "public class EmailNotifier", "public void Notify");

    [Fact] // MCP-TOOL-001
    public async Task AnalyzeFlow_ListUnresolved_GetFlow_MostramOFluxoEOsBuracosComApelidos()
    {
        await using var session = new Session(fixture);
        var id = await AnalyzeAsync(session);

        var holes = await session.Tools.ListUnresolved(id);
        var flow = await session.Tools.GetFlow(id);

        Assert.Contains("chamada `notifier.Notify`", holes);
        Assert.Contains("mais de uma implementação", holes);
        Assert.StartsWith("Fluxo de AlertJob.ExecuteAsync", flow);
        Assert.Contains("n1 entrada: AlertJob.ExecuteAsync", flow);
    }

    [Fact] // MCP-TOOL-002
    public async Task ProposeEdge_ComProvaValida_EntraComoInferidaPeloAgenteEOBuracoFecha()
    {
        await using var session = new Session(fixture);
        var id = await AnalyzeAsync(session);
        var (before, hole) = await HoleAsync(session, id, "notifier.Notify");
        var openBefore = (await session.Db.Surveys.AsNoTracking().SingleAsync(s => s.Id == id)).UnresolvedCount;

        var result = await session.Tools.ProposeEdge(id, hole, "EmailNotifier.Notify", Services, EmailNotifyLine, "Produção usa e-mail.");

        Assert.StartsWith("Aceita.", result);
        var survey = await session.Db.Surveys.AsNoTracking().SingleAsync(s => s.Id == id);
        var graph = survey.ReadGraph()!;
        var holeId = before.Nodes[int.Parse(hole[1..]) - 1].Id;
        var edge = Assert.Single(graph.Edges, e => e.From == holeId);
        Assert.Equal(EdgeConfidence.Inferred, edge.Confidence);
        Assert.Equal(ProposalOrigin.Agent, edge.Origin);
        Assert.Equal(openBefore - 1, survey.UnresolvedCount);

        var proposal = await session.Db.SurveyProposals.AsNoTracking().SingleAsync(p => p.SurveyId == id);
        Assert.Equal(ProposalStatus.Accepted, proposal.Status);
        Assert.Equal(edge.To, proposal.TargetNodeId);
    }

    [Theory] // MCP-TOOL-003
    [InlineData("EmailNotifier.Notify", "Alerts/AlertJob.cs", "não confere")]
    [InlineData("MetodoInventado.Fazer", Services, "não confere")]
    public async Task ProposeEdge_ComProvaErrada_ERecusadaRegistradaEOGrafoNaoMuda(string target, string file, string message)
    {
        await using var session = new Session(fixture);
        var id = await AnalyzeAsync(session);
        var (before, hole) = await HoleAsync(session, id, "notifier.Notify");

        var result = await session.Tools.ProposeEdge(id, hole, target, file, EmailNotifyLine, "chute");

        Assert.StartsWith("Recusada.", result);
        Assert.Contains(message, result);
        var after = (await session.Db.Surveys.AsNoTracking().SingleAsync(s => s.Id == id)).ReadGraph()!;
        Assert.Equal(before.Edges.Count, after.Edges.Count);
        var proposal = await session.Db.SurveyProposals.AsNoTracking().SingleAsync(p => p.SurveyId == id);
        Assert.Equal(ProposalStatus.Rejected, proposal.Status);
        Assert.NotNull(proposal.RejectionReason);
    }

    [Fact] // MCP-TOOL-004
    public async Task ProposeEdge_EmNoQueNaoEBuraco_OuMesmoDestinoDuasVezes_ERecusada_MasOutroDestinoEntra()
    {
        await using var session = new Session(fixture);
        var id = await AnalyzeAsync(session);
        var (_, hole) = await HoleAsync(session, id, "notifier.Notify");

        var onEntry = await session.Tools.ProposeEdge(id, "n1", "EmailNotifier.Notify", Services, EmailNotifyLine, "x");
        var first = await session.Tools.ProposeEdge(id, hole, "EmailNotifier.Notify", Services, EmailNotifyLine, "x");
        var second = await session.Tools.ProposeEdge(id, hole, "EmailNotifier.Notify", Services, EmailNotifyLine, "x");
        var smsLine = FixtureLines.LineOf(Services, "public class SmsNotifier", "public void Notify");
        var other = await session.Tools.ProposeEdge(id, hole, "SmsNotifier.Notify", Services, smsLine, "x", when: "cliente sem e-mail");

        Assert.Contains("Só nós não resolvidos", onEntry);
        Assert.StartsWith("Aceita.", first);
        Assert.Contains("já foi proposto", second);
        Assert.StartsWith("Aceita.", other);
        var graph = (await session.Db.Surveys.AsNoTracking().SingleAsync(s => s.Id == id)).ReadGraph()!;
        var holeId = graph.Nodes[int.Parse(hole[1..]) - 1].Id;
        Assert.Equal(2, graph.Edges.Count(e => e.From == holeId));
        Assert.Contains(graph.Edges, e => e.From == holeId && e.Label == "cliente sem e-mail");
    }

    [Fact] // MCP-TOOL-005
    public async Task ProposeEdge_DepoisDeUmCommitNovo_ERecusadaPedindoNovoLevantamento()
    {
        await using var session = new Session(fixture);
        var id = await AnalyzeAsync(session);
        var (_, hole) = await HoleAsync(session, id, "notifier.Notify");
        await fixture.NewCommitAsync();

        var result = await session.Tools.ProposeEdge(id, hole, "EmailNotifier.Notify", Services, EmailNotifyLine, "x");

        Assert.Contains("O código mudou", result);
    }

    [Fact] // MCP-TOOL-006
    public async Task DescribeNode_GravaARegraMarcadaComoDoAgente()
    {
        await using var session = new Session(fixture);
        var id = await AnalyzeAsync(session);

        var saved = await session.Tools.DescribeNode(id, "n1", "Envia os alertas devidos e avisa o mediator no fim.");
        var tooShort = await session.Tools.DescribeNode(id, "n1", "curta");
        var unknown = await session.Tools.DescribeNode(id, "n9999", "Regra para um nó que não existe.");

        Assert.StartsWith("Gravada.", saved);
        Assert.StartsWith("Recusada.", tooShort);
        Assert.Contains("não encontrado", unknown);
        var entry = (await session.Db.Surveys.AsNoTracking().SingleAsync(s => s.Id == id)).ReadGraph()!.Nodes[0];
        Assert.Equal(ProposalOrigin.Agent, entry.Rule!.Origin);
        Assert.Contains("regra: Envia os alertas", await session.Tools.GetFlow(id));
    }

    [Fact] // MCP-TOOL-007
    public async Task SearchMethodsEGetNodeCode_DaoOQueOAgentePrecisaParaAProva()
    {
        await using var session = new Session(fixture);
        var id = await AnalyzeAsync(session);
        var (_, hole) = await HoleAsync(session, id, "notifier.Notify");

        var found = await session.Tools.SearchMethods(id, "Notifier.Notify");
        var code = await session.Tools.GetNodeCode(id, hole, 2);

        Assert.Contains($"Fixture.Alerts.EmailNotifier.Notify ({Services}:{EmailNotifyLine})", found);
        Assert.Contains("notifier.Notify();", code);
        Assert.Contains(">", code);
    }

    [Fact] // MCP-TOOL-008
    public void FindNode_AceitaApelidoOuIdEIgnoraForaDaFaixa()
    {
        var graph = new FlowGraph(1, "A.B", "e", [new FlowNode("e", NodeKind.Entry, "A.B"), new FlowNode("x", NodeKind.Method, "A.C")], []);

        Assert.Equal("x", RastroTools.FindNode(graph, "n2")!.Id);
        Assert.Equal("x", RastroTools.FindNode(graph, "x")!.Id);
        Assert.Null(RastroTools.FindNode(graph, "n3"));
        Assert.Null(RastroTools.FindNode(graph, "n0"));
    }
}
