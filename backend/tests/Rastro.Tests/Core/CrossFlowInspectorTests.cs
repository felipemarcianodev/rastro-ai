using Rastro.Core;
using Rastro.Core.Graph;

namespace Rastro.Tests.Core;

// Cenários CORE-CROSS-NNN: regra cruzada entre fluxos (Fase 13).
public class CrossFlowInspectorTests
{
    private static FlowNode Method(string id, string label, string? symbol = null) => new(id, NodeKind.Method, label, Symbol: symbol);

    private static FlowEdge Edge(string from, string to) => new(from, to, 0, EdgeConfidence.Extracted);

    private const string Save = "App.IPedidoRepository.Update(App.Pedido, System.Threading.CancellationToken)";

    // entrada -> [Validator.Validar] e -> IPedidoRepository.Update(Pedido)
    private static FlowSample Flow(string entry, bool withGuard)
    {
        var nodes = new List<FlowNode> { Method("e", entry), Method("w", "IPedidoRepository.Update", Save) };
        var edges = new List<FlowEdge> { Edge("e", "w") };
        if (withGuard)
        {
            nodes.Add(Method("g", "PedidoValidator.Validar", "App.PedidoValidator.Validar(App.Pedido)"));
            edges.Add(Edge("e", "g"));
        }
        return new FlowSample(Guid.NewGuid(), entry, new FlowGraph(RastroSchema.Version, entry, "e", nodes, edges));
    }

    [Fact] // CORE-CROSS-001
    public void Inspect_FluxoQuePulaAGuardaQueOutrosDoisUsam_ViraAchado()
    {
        var target = Flow("Webhook.Receber", withGuard: false);

        var findings = CrossFlowInspector.Inspect(target, [Flow("Pedido.Criar", true), Flow("Pedido.Editar", true), target]);

        var finding = Assert.Single(findings);
        Assert.Equal("w", finding.NodeId);
        Assert.Equal("Pedido", finding.Entity);
        Assert.Equal("PedidoValidator.Validar", finding.Guard);
        Assert.Equal(["Pedido.Criar", "Pedido.Editar"], finding.Flows);
    }

    [Fact] // CORE-CROSS-002
    public void Inspect_SoUmOutroFluxoUsaAGuarda_NaoViraRegra()
    {
        Assert.Empty(CrossFlowInspector.Inspect(Flow("Webhook.Receber", false), [Flow("Pedido.Criar", true)]));
    }

    [Fact] // CORE-CROSS-003
    public void Inspect_FluxoQueTambemValida_SemAchado()
    {
        Assert.Empty(CrossFlowInspector.Inspect(Flow("Pedido.Importar", true), [Flow("Pedido.Criar", true), Flow("Pedido.Editar", true)]));
    }
}
