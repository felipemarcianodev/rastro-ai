namespace Rastro.Core.Graph;

// Ids de nós de outro sistema (Fase 25). O id do nó vem do símbolo (m:Namespace.Tipo.Metodo), e dois
// repositórios podem ter o mesmo tipo; o prefixo "<repositório>::" evita a colisão e diz de onde o nó é.
// O primeiro trecho (repositório do levantamento) não leva prefixo: o grafo sem costura sai idêntico.
public static class SystemIds
{
    public const string Separator = "::";

    public static string Prefix(string system, string nodeId) => $"{system}{Separator}{nodeId}";

    // O que vem antes do separador, ou nulo quando o id não tem sistema.
    public static string? SystemOf(string nodeId)
    {
        var index = nodeId.IndexOf(Separator, StringComparison.Ordinal);
        return index <= 0 ? null : nodeId[..index];
    }

    // Grafo de um trecho dentro do sistema: todo id (nós, pontas das arestas, entrada e achados)
    // ganha o prefixo e todo nó passa a dizer o sistema.
    public static FlowGraph InSystem(FlowGraph graph, string system)
    {
        string P(string id) => Prefix(system, id);

        return graph with
        {
            EntryNodeId = P(graph.EntryNodeId),
            Nodes = graph.Nodes.Select(n => n with { Id = P(n.Id), System = system }).ToList(),
            Edges = graph.Edges.Select(e => e with { From = P(e.From), To = P(e.To) }).ToList(),
            Findings = graph.Findings?.Select(f => f with { NodeId = P(f.NodeId) }).ToList()
        };
    }
}
