using Rastro.Core.Graph;
using Rastro.Extractor;

namespace Rastro.Tests.Extractor;

// Cenários EXT-FLOW-NNN sobre a fixture Alerts (ver Fixtures/Alerts/AlertJob.cs).
public class FlowExtractorTests
{
    private const string Entry = "AlertJob.ExecuteAsync";

    private static async Task<FlowGraph> ExtractAsync(int maxDepth = 15)
    {
        var solution = FixtureWorkspace.Load("Alerts");
        await FixtureWorkspace.AssertCompilesAsync(solution);
        return await FlowExtractor.ExtractAsync(solution, Entry, new ExtractionOptions
        {
            MaxDepth = maxDepth,
            BaseDirectory = FixtureWorkspace.FixturesRoot
        });
    }

    private static FlowNode Node(FlowGraph g, string label, NodeKind? kind = null) =>
        Assert.Single(g.Nodes, n => n.Label == label && (kind is null || n.Kind == kind));

    private static IEnumerable<FlowEdge> From(FlowGraph g, FlowNode node) => g.Edges.Where(e => e.From == node.Id);

    private static bool HasEdge(FlowGraph g, FlowNode from, FlowNode to, string? label = null) =>
        g.Edges.Any(e => e.From == from.Id && e.To == to.Id && e.Label == label);

    [Fact] // EXT-FLOW-001
    public async Task Extrair_MetodoDeEntrada_ViraNoEntryComLocalizacao()
    {
        var g = await ExtractAsync();

        var entry = Assert.Single(g.Nodes, n => n.Id == g.EntryNodeId);
        Assert.Equal(NodeKind.Entry, entry.Kind);
        Assert.Equal("AlertJob.ExecuteAsync", entry.Label);
        Assert.Equal("Alerts/AlertJob.cs", entry.Location!.File);
        Assert.Equal(11, entry.Location.Line);
        Assert.Equal(Entry, g.EntryPoint);
        Assert.Equal(1, g.SchemaVersion);
    }

    [Fact] // EXT-FLOW-002
    public async Task Extrair_ChamadaDireta_SegueAteOMetodoChamadoInclusiveDentroDeLambda()
    {
        var g = await ExtractAsync();
        var entry = g.Nodes.Single(n => n.Id == g.EntryNodeId);
        var loadDue = Node(g, "AlertJob.LoadDue", NodeKind.Method);
        var isDue = Node(g, "AlertJob.IsDue", NodeKind.Method);

        Assert.True(HasEdge(g, entry, loadDue));
        Assert.True(HasEdge(g, loadDue, isDue));
        Assert.All(g.Edges, e => Assert.Equal(EdgeConfidence.Extracted, e.Confidence));
    }

    [Fact] // EXT-FLOW-003
    public async Task Extrair_BifurcacoesELacos_ViramNosComCondicaoERamos()
    {
        var g = await ExtractAsync();
        var loop = Node(g, "foreach (var alert in due)", NodeKind.Loop);
        var muted = Node(g, "if (alert.Muted)", NodeKind.Decision);
        var tryNode = Node(g, "try", NodeKind.Try);
        var switchNode = Node(g, "switch (due.Count)", NodeKind.Decision);

        Assert.Equal("alert.Muted", muted.Condition);
        Assert.Contains(From(g, loop), e => e.To == muted.Id && e.Label == "each");
        Assert.Contains(From(g, loop), e => e.To == tryNode.Id && e.Label == "each");
        Assert.Contains(From(g, switchNode), e => e.Label == "case 0");
        Assert.Contains(From(g, switchNode), e => e.Label == "default");
    }

    [Fact] // EXT-FLOW-004
    public async Task Extrair_InterfaceComImplementacaoUnica_SegueAteAImplementacaoEAteAChamadaExterna()
    {
        var g = await ExtractAsync();
        var tryNode = Node(g, "try", NodeKind.Try);
        var send = Node(g, "TelegramAlertSender.SendAsync", NodeKind.Method);
        var http = Node(g, "HttpClient.PostAsync", NodeKind.External);

        Assert.True(HasEdge(g, tryNode, send, "try"));
        Assert.True(HasEdge(g, send, http));
    }

    [Fact] // EXT-FLOW-016
    public async Task Extrair_ChamadaExterna_LigacaoTrazArquivoELinhaDaChamada()
    {
        var g = await ExtractAsync();
        var send = Node(g, "TelegramAlertSender.SendAsync", NodeKind.Method);
        var http = Node(g, "HttpClient.PostAsync", NodeKind.External);

        var edge = g.Edges.Single(e => e.From == send.Id && e.To == http.Id);

        Assert.Null(http.Location);
        Assert.NotNull(edge.Location);
        Assert.EndsWith(".cs", edge.Location!.File);
        Assert.True(edge.Location.Line > send.Location!.Line);
    }

    [Fact] // EXT-FLOW-005
    public async Task Extrair_InterfaceComDuasImplementacoes_ORegistroDeDiEscolhe()
    {
        var g = await ExtractAsync();
        var tryNode = Node(g, "try", NodeKind.Try);
        var plain = Node(g, "PlainFormatter.Format", NodeKind.Method);

        Assert.True(HasEdge(g, tryNode, plain, "try"));
        Assert.DoesNotContain(g.Nodes, n => n.Label == "HtmlFormatter.Format");
        // A ordem respeita a execução: formatar antes de enviar.
        var tryEdges = From(g, tryNode).Where(e => e.Label == "try").OrderBy(e => e.Order).Select(e => e.To).ToList();
        Assert.True(tryEdges.IndexOf(plain.Id) < tryEdges.IndexOf(Node(g, "TelegramAlertSender.SendAsync").Id));
    }

    [Fact] // EXT-FLOW-006
    public async Task Extrair_ChamadasQueOCompiladorNaoResolve_ViramNaoResolvidasComMotivo()
    {
        var g = await ExtractAsync();
        var unresolved = g.Nodes.Where(n => n.Kind == NodeKind.Unresolved).ToList();

        var notifier = Assert.Single(unresolved, n => n.Reason == UnresolvedReason.MultipleImplementations);
        Assert.Equal("notifier.Notify", notifier.Label);
        Assert.Contains("EmailNotifier", notifier.Detail);
        Assert.Contains("SmsNotifier", notifier.Detail);

        Assert.Single(unresolved, n => n.Reason == UnresolvedReason.NoImplementation && n.Label == "orphan.Run");
        Assert.Single(unresolved, n => n.Reason == UnresolvedReason.Delegate && n.Label == "filter");

        var mediator = Assert.Single(unresolved, n => n.Reason == UnresolvedReason.MessageDispatch);
        Assert.Equal("Fixture.Alerts.AlertsProcessed", mediator.Detail);
        Assert.All(unresolved, n => Assert.NotNull(n.Location));
    }

    [Fact] // EXT-FLOW-007
    public async Task Extrair_ThrowEReturnAntecipado_ViramNos()
    {
        var g = await ExtractAsync();
        var tryNode = Node(g, "try", NodeKind.Try);
        var thrown = Node(g, "throw new AlertFailedException", NodeKind.Throw);
        var filterIf = Node(g, "if (filter(due.Count))", NodeKind.Decision);

        Assert.True(HasEdge(g, tryNode, thrown, "catch (InvalidOperationException)"));
        Assert.Contains(From(g, filterIf), e => e.Label == "then" && g.Nodes.Single(n => n.Id == e.To).Kind == NodeKind.Return);
    }

    [Fact] // EXT-FLOW-008
    public async Task Extrair_Recursao_NaoDuplicaNoENaoEntraEmLaco()
    {
        var g = await ExtractAsync();
        var recurse = Node(g, "AlertJob.Recurse", NodeKind.Method);
        var inner = Assert.Single(g.Edges, e => e.From != g.EntryNodeId && e.To == recurse.Id);

        Assert.StartsWith("if:", inner.From);
    }

    [Fact] // EXT-FLOW-009
    public async Task Extrair_ChamadasDeLinqELog_NaoViramRuido()
    {
        var g = await ExtractAsync();

        Assert.DoesNotContain(g.Nodes, n => n.Label.Contains("Where") || n.Label.Contains("ToList") || n.Label.Contains("Trim"));
    }

    [Fact] // EXT-FLOW-010
    public async Task Extrair_LimiteDeProfundidade_MarcaOCaminhoComoNaoResolvido()
    {
        var g = await ExtractAsync(maxDepth: 1);

        Assert.Contains(g.Nodes, n => n.Reason == UnresolvedReason.DepthLimit);
        Assert.DoesNotContain(g.Nodes, n => n.Label == "HttpClient.PostAsync");
    }

    [Fact] // EXT-FLOW-011
    public async Task Extrair_DuasVezes_GeraOMesmoJson()
    {
        var first = FlowGraphJson.Serialize(await ExtractAsync());
        var second = FlowGraphJson.Serialize(await ExtractAsync());

        Assert.Equal(first, second);
    }

    [Fact] // EXT-FLOW-012
    public async Task Json_EnumsViajamComoIntEIdaEVoltaPreservaOGrafo()
    {
        var g = await ExtractAsync();
        var json = FlowGraphJson.Serialize(g);

        Assert.Contains("\"kind\": 0", json);
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.DoesNotContain("\"Extracted\"", json);

        var back = FlowGraphJson.Deserialize(json);
        Assert.Equal(json, FlowGraphJson.Serialize(back));
    }

    [Fact] // EXT-FLOW-014
    public async Task Extrair_FakeEmProjetoDeTeste_NaoCompeteComAImplementacaoDeProducao()
    {
        var g = await ExtractAsync();
        var entry = g.Nodes.Single(n => n.Id == g.EntryNodeId);
        var clock = Node(g, "SystemClock.Now", NodeKind.Method);

        Assert.True(HasEdge(g, entry, clock));
        Assert.DoesNotContain(g.Nodes, n => n.Label.Contains("FakeClock") || (n.Detail ?? string.Empty).Contains("FakeClock"));
    }

    [Fact] // EXT-FLOW-015
    public async Task Extrair_BreakEContinueDeLaco_ViramDesvio_MasBreakDeSwitchNao()
    {
        var g = await ExtractAsync();
        var muted = Node(g, "if (alert.Muted)", NodeKind.Decision);
        var negative = Node(g, "if (alert.Id < 0)", NodeKind.Decision);
        var jumps = g.Nodes.Where(n => n.Kind == NodeKind.Jump).ToList();

        Assert.Contains(From(g, muted), e => e.Label == "then" && jumps.Any(j => j.Id == e.To && j.Label == "continue"));
        Assert.Contains(From(g, negative), e => e.Label == "then" && jumps.Any(j => j.Id == e.To && j.Label == "break"));
        Assert.Equal(2, jumps.Count);
    }

    [Theory] // EXT-FLOW-013
    [InlineData("Inexistente.Metodo")]
    [InlineData("SemPonto")]
    [InlineData("AlertJob.NaoExiste")]
    public async Task Extrair_EntradaInvalida_LancaErroClaro(string entry)
    {
        var solution = FixtureWorkspace.Load("Alerts");

        await Assert.ThrowsAsync<FlowEntryNotFoundException>(() => FlowExtractor.ExtractAsync(solution, entry));
    }

    [Fact] // EXT-FLOW-017
    public async Task Extrair_EntradaSemAClasse_SugereOMetodoComNomeCompleto()
    {
        var solution = FixtureWorkspace.Load("Alerts");

        // Esqueceu a classe: "Namespace.Metodo" no lugar de "Namespace.Tipo.Metodo".
        var error = await Assert.ThrowsAsync<FlowEntryNotFoundException>(
            () => FlowExtractor.ExtractAsync(solution, "Fixture.Alerts.ExecuteAsync"));

        Assert.Contains("Com o nome ExecuteAsync existe: Fixture.Alerts.AlertJob.ExecuteAsync", error.Message);
    }

    [Fact] // EXT-FLOW-018
    public async Task Extrair_MetodoQueNaoExiste_DizQueNenhumTemEsseNome()
    {
        var solution = FixtureWorkspace.Load("Alerts");

        var error = await Assert.ThrowsAsync<FlowEntryNotFoundException>(
            () => FlowExtractor.ExtractAsync(solution, "AlertJob.NaoExiste"));

        Assert.Contains("nenhum método chamado NaoExiste", error.Message);
    }
}
