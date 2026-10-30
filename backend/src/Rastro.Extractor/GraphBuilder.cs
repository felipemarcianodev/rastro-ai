using Rastro.Core;
using Rastro.Core.Graph;

namespace Rastro.Extractor;

// Acumula nós e arestas na ordem em que o percurso os encontra (saída determinística).
internal sealed class GraphBuilder
{
    private readonly Dictionary<string, FlowNode> _nodes = new(StringComparer.Ordinal);
    private readonly List<FlowNode> _nodeOrder = [];
    private readonly List<FlowEdge> _edges = [];
    private readonly Dictionary<string, int> _nextOrder = new(StringComparer.Ordinal);
    private readonly HashSet<(string, string, string?)> _edgeKeys = [];

    public bool HasNode(string id) => _nodes.ContainsKey(id);

    public void AddNode(FlowNode node)
    {
        if (_nodes.TryAdd(node.Id, node)) _nodeOrder.Add(node);
    }

    // Mesma chamada repetida no mesmo ramo (ex.: dois Log() seguidos) vira uma aresta só.
    // location: onde a chamada acontece (só para destino sem posição própria, como o externo);
    // repetida no mesmo ramo, fica a primeira. http: chamada HTTP que a aresta representa (Fase 25).
    public void AddEdge(string from, string to, string? label, SourceLocation? location = null, HttpCall? http = null)
    {
        if (!_edgeKeys.Add((from, to, label))) return;

        var order = _nextOrder.GetValueOrDefault(from) + 1;
        _nextOrder[from] = order;
        _edges.Add(new FlowEdge(from, to, order, EdgeConfidence.Extracted, label, Location: location, Http: http));
    }

    public FlowGraph Build(string entryPoint, string entryNodeId) =>
        new(RastroSchema.Version, entryPoint, entryNodeId, _nodeOrder.ToList(), _edges.ToList());
}
