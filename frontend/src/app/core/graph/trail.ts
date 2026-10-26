import { FlowGraph } from '../models';

export interface Trail {
  nodeIds: Set<string>;
  edgeIndexes: Set<number>;
  // Nós da entrada até o alvo, na ordem.
  path: string[];
}

// Menor caminho da entrada até o nó escolhido (busca em largura, respeitando a ordem das arestas).
export function trailTo(graph: FlowGraph, targetId: string): Trail {
  const outgoing = new Map<string, { to: string; index: number; order: number }[]>();
  graph.edges.forEach((edge, index) => {
    const list = outgoing.get(edge.from) ?? [];
    list.push({ to: edge.to, index, order: edge.order });
    outgoing.set(edge.from, list);
  });
  for (const list of outgoing.values()) list.sort((a, b) => a.order - b.order);

  const cameFrom = new Map<string, { node: string; edge: number }>();
  const visited = new Set<string>([graph.entryNodeId]);
  const queue = [graph.entryNodeId];

  while (queue.length > 0) {
    const current = queue.shift()!;
    if (current === targetId) break;
    for (const next of outgoing.get(current) ?? []) {
      if (visited.has(next.to)) continue;
      visited.add(next.to);
      cameFrom.set(next.to, { node: current, edge: next.index });
      queue.push(next.to);
    }
  }

  const path: string[] = [];
  const edgeIndexes = new Set<number>();
  if (targetId !== graph.entryNodeId && !cameFrom.has(targetId)) {
    return { nodeIds: new Set([targetId]), edgeIndexes, path: [targetId] };
  }

  let step: string | undefined = targetId;
  while (step !== undefined) {
    path.unshift(step);
    const previous = cameFrom.get(step);
    if (previous) edgeIndexes.add(previous.edge);
    step = previous?.node;
  }

  return { nodeIds: new Set(path), edgeIndexes, path };
}
