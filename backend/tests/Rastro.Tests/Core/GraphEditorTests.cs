using Rastro.Core;
using Rastro.Core.Graph;

namespace Rastro.Tests.Core;

// Cenários CORE-GE-NNN: mudanças no grafo depois da extração (funções puras do núcleo).
public class GraphEditorTests
{
    // entry -> a -> hole (não resolvido)
    private static FlowGraph Base() => new(
        RastroSchema.Version, "Job.Run", "entry",
        [
            new FlowNode("entry", NodeKind.Entry, "Job.Run"),
            new FlowNode("a", NodeKind.Method, "Job.A"),
            new FlowNode("hole", NodeKind.Unresolved, "notifier.Notify", Reason: UnresolvedReason.MultipleImplementations)
        ],
        [
            new FlowEdge("entry", "a", 1, EdgeConfidence.Extracted),
            new FlowEdge("a", "hole", 1, EdgeConfidence.Extracted)
        ]);

    // Ramo extraído a partir do destino: target -> b, e b -> a (nó que já existe no grafo).
    private static FlowGraph Branch() => new(
        RastroSchema.Version, "Email.Notify", "target",
        [
            new FlowNode("target", NodeKind.Entry, "Email.Notify"),
            new FlowNode("b", NodeKind.Method, "Email.B"),
            new FlowNode("a", NodeKind.Method, "Job.A")
        ],
        [
            new FlowEdge("target", "b", 1, EdgeConfidence.Extracted),
            new FlowEdge("b", "a", 1, EdgeConfidence.Extracted)
        ]);

    [Fact] // CORE-GE-001
    public void RamoInferido_LigaOBuracoAoDestinoComOrigemEJuntaSemRepetirNos()
    {
        var graph = GraphEditor.WithInferredBranch(Base(), "hole", Branch(), ProposalOrigin.Agent);

        var inferred = Assert.Single(graph.Edges, e => e.From == "hole");
        Assert.Equal("target", inferred.To);
        Assert.Equal(EdgeConfidence.Inferred, inferred.Confidence);
        Assert.Equal(ProposalOrigin.Agent, inferred.Origin);
        Assert.Equal(NodeKind.Method, graph.Nodes.Single(n => n.Id == "target").Kind);
        Assert.Single(graph.Nodes, n => n.Id == "a");
        Assert.Equal(5, graph.Nodes.Count);
    }

    [Fact] // CORE-GE-002
    public void Confirmar_ArestaInferida_ViraConfirmada()
    {
        var graph = GraphEditor.WithInferredBranch(Base(), "hole", Branch(), ProposalOrigin.Agent);

        var confirmed = GraphEditor.WithEdgeDecision(graph, "hole", "target", confirm: true);

        Assert.Equal(EdgeConfidence.Confirmed, confirmed.Edges.Single(e => e.From == "hole").Confidence);
        Assert.Equal(graph.Nodes.Count, confirmed.Nodes.Count);
    }

    [Fact] // CORE-GE-003
    public void Descartar_ArestaInferida_RemoveORamoQueSoEraAlcancavelPorEla()
    {
        var graph = GraphEditor.WithInferredBranch(Base(), "hole", Branch(), ProposalOrigin.Agent);

        var dismissed = GraphEditor.WithEdgeDecision(graph, "hole", "target", confirm: false);

        Assert.Equal(["entry", "a", "hole"], dismissed.Nodes.Select(n => n.Id));
        Assert.DoesNotContain(dismissed.Edges, e => e.From == "hole" || e.From == "target" || e.From == "b");
    }

    [Fact] // CORE-GE-004
    public void Decidir_ArestaExtraida_NaoPode()
    {
        Assert.Throws<InvalidOperationException>(() => GraphEditor.WithEdgeDecision(Base(), "entry", "a", confirm: false));
    }

    [Fact] // CORE-GE-005
    public void Regra_GravaNoNoComAOrigem()
    {
        var graph = GraphEditor.WithRule(Base(), "a", new NodeRule("Calcula os alertas devidos.", ProposalOrigin.Agent));

        Assert.Equal("Calcula os alertas devidos.", graph.Nodes.Single(n => n.Id == "a").Rule!.Text);
        Assert.Throws<InvalidOperationException>(() => GraphEditor.WithRule(Base(), "nao-existe", new NodeRule("x", ProposalOrigin.Agent)));
    }
}
