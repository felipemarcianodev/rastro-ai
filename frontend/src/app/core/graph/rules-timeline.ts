import { FlowGraph, FlowNode } from '../models';

// Nós com regra na ordem em que o fluxo passa por eles (profundidade primeiro, seguindo a ordem
// de execução das arestas a partir da entrada). Lê como um roteiro do fluxo.
export function rulesTimeline(graph: FlowGraph): FlowNode[] {
  const byId = new Map(graph.nodes.map((n) => [n.id, n]));
  const outgoing = new Map<string, { to: string; order: number }[]>();
  for (const edge of graph.edges) {
    const list = outgoing.get(edge.from) ?? [];
    list.push({ to: edge.to, order: edge.order });
    outgoing.set(edge.from, list);
  }

  const visited = new Set<string>();
  const result: FlowNode[] = [];
  const stack = [graph.entryNodeId];
  while (stack.length > 0) {
    const id = stack.pop()!;
    if (visited.has(id)) continue;
    visited.add(id);
    const node = byId.get(id);
    if (node?.rule) result.push(node);
    const next = (outgoing.get(id) ?? []).sort((a, b) => b.order - a.order);
    for (const edge of next) if (!visited.has(edge.to)) stack.push(edge.to);
  }
  return result;
}
