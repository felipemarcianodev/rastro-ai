import { FlowGraph } from '../models';

// "Ver somente este fluxo": o recorte do grafo com os caminhos da entrada até o passo e tudo o
// que roda depois dele. Ligações laterais (um passo do caminho que chama outra coisa fora do
// fluxo do passo) saem do recorte; é isso que limpa a leitura.
export function focusOn(graph: FlowGraph, targetId: string): FlowGraph {
  if (!graph.nodes.some((n) => n.id === targetId)) return graph;

  const outgoing = new Map<string, string[]>();
  const incoming = new Map<string, string[]>();
  for (const edge of graph.edges) {
    outgoing.set(edge.from, [...(outgoing.get(edge.from) ?? []), edge.to]);
    incoming.set(edge.to, [...(incoming.get(edge.to) ?? []), edge.from]);
  }

  const fromEntry = reach(graph.entryNodeId, outgoing);
  const toTarget = reach(targetId, incoming);
  const upstream = new Set([...toTarget].filter((id) => fromEntry.has(id)));
  const downstream = reach(targetId, outgoing);
  const kept = new Set([...upstream, ...downstream]);

  return {
    ...graph,
    nodes: graph.nodes.filter((n) => kept.has(n.id)),
    edges: graph.edges.filter(
      (e) => kept.has(e.from) && kept.has(e.to) && ((upstream.has(e.from) && upstream.has(e.to)) || downstream.has(e.from)),
    ),
  };
}

function reach(start: string, next: Map<string, string[]>): Set<string> {
  const seen = new Set([start]);
  const queue = [start];
  while (queue.length > 0) {
    for (const id of next.get(queue.shift()!) ?? []) {
      if (seen.has(id)) continue;
      seen.add(id);
      queue.push(id);
    }
  }
  return seen;
}
