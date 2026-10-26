import { FlowGraph } from '../models';
import { Trail } from './trail';

// Todos os caminhos de A até B ("como esse job chega no Telegram"): os passos que estão em
// algum caminho são os alcançados a partir de A que também alcançam B. O path é o mais curto,
// para o painel listar na ordem. Sem caminho, devolve null.
export function pathsBetween(graph: FlowGraph, fromId: string, toId: string): Trail | null {
  const outgoing = new Map<string, { to: string; index: number; order: number }[]>();
  const incoming = new Map<string, string[]>();
  graph.edges.forEach((edge, index) => {
    if (edge.from === edge.to) return;
    outgoing.set(edge.from, [...(outgoing.get(edge.from) ?? []), { to: edge.to, index, order: edge.order }]);
    incoming.set(edge.to, [...(incoming.get(edge.to) ?? []), edge.from]);
  });

  const forward = reach(fromId, (id) => (outgoing.get(id) ?? []).map((e) => e.to));
  if (!forward.has(toId)) return null;
  const backward = reach(toId, (id) => incoming.get(id) ?? []);
  const nodeIds = new Set([...forward].filter((id) => backward.has(id)));

  const edgeIndexes = new Set<number>();
  graph.edges.forEach((edge, index) => {
    if (edge.from !== edge.to && nodeIds.has(edge.from) && nodeIds.has(edge.to)) edgeIndexes.add(index);
  });

  // Menor caminho (busca em largura na ordem das ligações) só dentro do recorte.
  const cameFrom = new Map<string, string>();
  const queue = [fromId];
  const seen = new Set([fromId]);
  while (queue.length > 0) {
    const current = queue.shift()!;
    if (current === toId) break;
    for (const next of [...(outgoing.get(current) ?? [])].sort((a, b) => a.order - b.order)) {
      if (seen.has(next.to) || !nodeIds.has(next.to)) continue;
      seen.add(next.to);
      cameFrom.set(next.to, current);
      queue.push(next.to);
    }
  }
  const path: string[] = [];
  for (let step: string | undefined = toId; step !== undefined; step = cameFrom.get(step)) path.unshift(step);

  return { nodeIds, edgeIndexes, path };
}

function reach(start: string, next: (id: string) => string[]): Set<string> {
  const seen = new Set([start]);
  const queue = [start];
  while (queue.length > 0) {
    for (const id of next(queue.shift()!)) {
      if (seen.has(id)) continue;
      seen.add(id);
      queue.push(id);
    }
  }
  return seen;
}
