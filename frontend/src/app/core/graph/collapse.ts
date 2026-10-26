import { EdgeConfidence, FlowGraph, NodeKind } from '../models';

// Nó que representa o que foi recolhido: o id carrega o passo de origem.
export const COLLAPSED_PREFIX = 'collapsed:';

export function collapsedOwner(id: string): string | null {
  return id.startsWith(COLLAPSED_PREFIX) ? id.slice(COLLAPSED_PREFIX.length) : null;
}

// Recolhe o que roda depois de cada passo escolhido num nó só ("+34 passos"). Some apenas o
// que só se alcança passando por ele: um passo que também é chamado por outro caminho continua
// na tela (senão o recolher quebraria a leitura do resto do fluxo).
export function collapseAfter(graph: FlowGraph, collapsed: ReadonlySet<string>): FlowGraph {
  const roots = [...collapsed].filter((id) => graph.nodes.some((n) => n.id === id));
  if (roots.length === 0) return graph;

  const outgoing = new Map<string, string[]>();
  for (const edge of graph.edges) outgoing.set(edge.from, [...(outgoing.get(edge.from) ?? []), edge.to]);

  // O que a entrada alcança sem atravessar nenhum passo recolhido.
  const blocked = new Set(roots);
  const visible = new Set<string>([graph.entryNodeId]);
  const queue = [graph.entryNodeId];
  while (queue.length > 0) {
    const current = queue.shift()!;
    if (blocked.has(current)) continue;
    for (const next of outgoing.get(current) ?? []) {
      if (visible.has(next)) continue;
      visible.add(next);
      queue.push(next);
    }
  }
  // Passos soltos (sem caminho da entrada, como um passo novo do rascunho) ficam visíveis.
  const reachable = reach(graph.entryNodeId, outgoing);
  for (const node of graph.nodes) if (!reachable.has(node.id)) visible.add(node.id);

  const hidden = new Set(graph.nodes.filter((n) => !visible.has(n.id)).map((n) => n.id));
  // Passo recolhido dentro de outro recolhido some junto: só os visíveis viram "+N passos".
  const placeholders = roots
    .filter((root) => visible.has(root))
    .map((root) => ({ root, count: [...reach(root, outgoing)].filter((id) => hidden.has(id)).length }))
    .filter((p) => p.count > 0);

  return {
    ...graph,
    nodes: [
      ...graph.nodes.filter((n) => !hidden.has(n.id)),
      ...placeholders.map((p) => ({
        id: COLLAPSED_PREFIX + p.root,
        kind: NodeKind.Jump,
        label: `+${p.count} ${p.count === 1 ? 'passo' : 'passos'}`,
        detail: 'Recolhido. Clique para expandir.',
      })),
    ],
    edges: [
      ...graph.edges.filter((e) => !hidden.has(e.from) && !hidden.has(e.to)),
      ...placeholders.map((p, index) => ({ from: p.root, to: COLLAPSED_PREFIX + p.root, order: 10_000 + index, confidence: EdgeConfidence.Extracted })),
    ],
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
