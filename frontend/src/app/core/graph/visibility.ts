import { FlowGraph, FlowNode, NodeKind } from '../models';

// Chamada para o próprio .NET (LINQ, EF Core, cache, DI, HttpClient...): detalhe do framework,
// que polui a leitura do fluxo. A tela esconde por padrão e o usuário pode mostrar.
export function isFrameworkCall(node: FlowNode): boolean {
  return node.kind === NodeKind.External && /^(System|Microsoft)\./.test(node.symbol ?? '');
}

// Grafo sem as chamadas do .NET (elas são sempre folhas: tirar o nó e as arestas até ele basta).
export function withoutFrameworkCalls(graph: FlowGraph): FlowGraph {
  const hidden = new Set(graph.nodes.filter(isFrameworkCall).map((n) => n.id));
  if (hidden.size === 0) return graph;
  return {
    ...graph,
    nodes: graph.nodes.filter((n) => !hidden.has(n.id)),
    edges: graph.edges.filter((e) => !hidden.has(e.to) && !hidden.has(e.from)),
  };
}
