import { ChangeKind, FlowEdge, FlowGraph } from '../models';

export interface Comparison {
  graph: FlowGraph;
  added: number;
  removed: number;
}

// Comparar antes e depois (Fase 12): o levantamento novo com o antigo por cima. Passos e
// ligações que só existem no novo ficam marcados como novos; os que só existiam no antigo
// entram de volta marcados como "sai do fluxo". O id do passo vem do símbolo, então o mesmo
// método casa entre os dois levantamentos.
export function compareGraphs(before: FlowGraph, after: FlowGraph): Comparison {
  const beforeIds = new Set(before.nodes.map((n) => n.id));
  const afterIds = new Set(after.nodes.map((n) => n.id));
  const key = (edge: FlowEdge) => `${edge.from}>${edge.to}`;
  const beforeEdges = new Set(before.edges.map(key));
  const afterEdges = new Set(after.edges.map(key));

  const added = after.nodes.filter((n) => !beforeIds.has(n.id));
  const removed = before.nodes.filter((n) => !afterIds.has(n.id));

  return {
    added: added.length,
    removed: removed.length,
    graph: {
      // Os achados de qualidade são os do levantamento novo (ids dos passos casam).
      ...after,
      nodes: [
        ...after.nodes.map((n) => ({ ...n, change: beforeIds.has(n.id) ? undefined : ChangeKind.Added })),
        ...removed.map((n) => ({ ...n, change: ChangeKind.Removed })),
      ],
      edges: [
        ...after.edges.map((e) => ({ ...e, change: beforeEdges.has(key(e)) ? undefined : ChangeKind.Added })),
        ...before.edges.filter((e) => !afterEdges.has(key(e))).map((e) => ({ ...e, change: ChangeKind.Removed })),
      ],
    },
  };
}
