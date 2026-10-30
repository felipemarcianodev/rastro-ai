using Rastro.Core;
using Rastro.Core.Graph;
using Rastro.Core.Http;
using Rastro.Core.Surveys;

namespace Rastro.Tests.Core;

// Cenários CORE-HTTP-NNN: o modelo do fluxo entre sistemas (Fase 25): ids com sistema, JSON e entidades.
public class CrossSystemModelTests
{
    private static FlowGraph Sample() => new(
        RastroSchema.Version, "A.Run", "m:a",
        [
            new FlowNode("m:a", NodeKind.Entry, "A.Run", Project: "Api"),
            new FlowNode("h:x.cs:3:9", NodeKind.Unresolved, "POST api/estornos", Reason: UnresolvedReason.NoMatchingEndpoint)
        ],
        [new FlowEdge("m:a", "h:x.cs:3:9", 1, EdgeConfidence.Extracted, Http: new HttpCall("POST", "api/estornos"))],
        [new Finding(FindingKind.LongMethod, FindingSeverity.Warning, "m:a", "longo")]);

    [Fact] // CORE-HTTP-010
    public void InSystem_PrefixaTodoIdEMarcaOSistema()
    {
        var graph = SystemIds.InSystem(Sample(), "pagamentos");

        Assert.Equal("pagamentos::m:a", graph.EntryNodeId);
        Assert.All(graph.Nodes, n =>
        {
            Assert.Equal("pagamentos", n.System);
            Assert.StartsWith("pagamentos::", n.Id, StringComparison.Ordinal);
        });
        var edge = Assert.Single(graph.Edges);
        Assert.Equal("pagamentos::m:a", edge.From);
        Assert.Equal("pagamentos::h:x.cs:3:9", edge.To);
        Assert.Equal("POST", edge.Http!.Method);
        Assert.Equal("pagamentos::m:a", Assert.Single(graph.Findings!).NodeId);
    }

    [Fact] // CORE-HTTP-011
    public void InSystem_GrafoOriginal_NaoMuda()
    {
        var original = Sample();

        SystemIds.InSystem(original, "pagamentos");

        Assert.All(original.Nodes, n => Assert.Null(n.System));
        Assert.Equal("m:a", original.EntryNodeId);
    }

    [Fact] // CORE-HTTP-012
    public void SystemOf_IdComEPrefixoSemSistema()
    {
        Assert.Equal("pagamentos", SystemIds.SystemOf(SystemIds.Prefix("pagamentos", "m:a")));
        Assert.Null(SystemIds.SystemOf("m:a"));
        Assert.Null(SystemIds.SystemOf("::m:a"));
    }

    [Fact] // CORE-HTTP-013
    public void Json_GrafoComHttpESistema_IdaEVoltaPreservaEOmiteNulos()
    {
        var json = FlowGraphJson.Serialize(SystemIds.InSystem(Sample(), "pagamentos"));

        Assert.Contains("\"system\": \"pagamentos\"", json);
        Assert.Contains("\"method\": \"POST\"", json);
        Assert.Contains($"\"reason\": {(int)UnresolvedReason.NoMatchingEndpoint}", json);
        Assert.Equal(json, FlowGraphJson.Serialize(FlowGraphJson.Deserialize(json)));
        Assert.DoesNotContain("\"system\": null", FlowGraphJson.Serialize(Sample()));
    }

    [Fact] // CORE-HTTP-014
    public void Json_GrafoDaVersao1_ContinuaSendoLido()
    {
        const string v1 = """
            {"schemaVersion":1,"entryPoint":"A.Run","entryNodeId":"m:a",
             "nodes":[{"id":"m:a","kind":0,"label":"A.Run"}],
             "edges":[{"from":"m:a","to":"m:a","order":1,"confidence":0}]}
            """;

        var graph = FlowGraphJson.Deserialize(v1);

        Assert.Equal(1, graph.SchemaVersion);
        Assert.Null(graph.Nodes[0].System);
        Assert.Null(graph.Edges[0].Http);
    }

    [Fact] // CORE-HTTP-015
    public void Survey_ComRepositoriosDeCostura_GuardaELe()
    {
        var survey = Survey.Create("pedidos", "Pedidos.sln", "PedidosController.Cancelar", 15, null, null, DateTimeOffset.UnixEpoch,
            ["pagamentos", "cobranca"]);

        Assert.Equal(["pagamentos", "cobranca"], survey.ReadCrossRepositories());
    }

    [Fact] // CORE-HTTP-016
    public void Survey_SemListaOuListaVazia_GravaNulo()
    {
        var sem = Survey.Create("pedidos", "Pedidos.sln", "A.B", 15, null, null, DateTimeOffset.UnixEpoch);
        var vazia = Survey.Create("pedidos", "Pedidos.sln", "A.B", 15, null, null, DateTimeOffset.UnixEpoch, []);

        Assert.Null(sem.CrossRepositoriesJson);
        Assert.Null(vazia.CrossRepositoriesJson);
        Assert.Empty(sem.ReadCrossRepositories());
        Assert.Empty(vazia.ReadCrossRepositories());
    }

    [Fact] // CORE-HTTP-017
    public void SurveySegment_Create_GuardaOTrecho()
    {
        var surveyId = Guid.NewGuid();

        var segment = SurveySegment.Create(surveyId, 1, "pagamentos", "src/Pag.sln", "EstornosController.Criar", "abc123", "main");

        Assert.NotEqual(Guid.Empty, segment.Id);
        Assert.Equal(surveyId, segment.SurveyId);
        Assert.Equal(1, segment.Order);
        Assert.Equal("pagamentos", segment.Repository);
        Assert.Equal("src/Pag.sln", segment.SolutionPath);
        Assert.Equal("EstornosController.Criar", segment.EntryPoint);
        Assert.Equal("abc123", segment.CommitHash);
        Assert.Equal("main", segment.Branch);
    }

    [Fact] // CORE-HTTP-018
    public void RouteIndex_Create_IdaEVoltaDasEntradas()
    {
        RouteEntry[] entries = [new("POST", "api/estornos", "Pag.EstornosController.Criar", "Estornos.cs", 12)];

        var index = RouteIndex.Create("pagamentos", "abc123", entries, DateTimeOffset.UnixEpoch);

        Assert.Equal("pagamentos", index.Repository);
        Assert.Equal("abc123", index.CommitHash);
        Assert.Equal(entries, index.ReadEntries());
    }
}
