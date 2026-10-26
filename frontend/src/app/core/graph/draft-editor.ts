import { ChangeKind, EdgeConfidence, FlowEdge, FlowGraph, FlowNode, NodeKind } from '../models';

// Edição do rascunho (Fase 7). Funções puras: recebem um rascunho e devolvem outro (o histórico
// de desfazer é só uma pilha de rascunhos). O que é do código nunca é apagado: é marcado como
// removido ou modificado; o que é novo leva marca de novo e id "new:".

export const ADDED_PREFIX = 'new:';

export function isAdded(node: FlowNode | FlowEdge): boolean {
  return node.change === ChangeKind.Added;
}

export function addStep(draft: FlowGraph, label: string, kind: NodeKind.Method | NodeKind.Decision, intent: string): { draft: FlowGraph; id: string } {
  const id = `${ADDED_PREFIX}${crypto.randomUUID()}`;
  const node: FlowNode = { id, kind, label: label.trim(), change: ChangeKind.Added, intent: intent.trim() };
  return { draft: { ...draft, nodes: [...draft.nodes, node] }, id };
}

// Liga "from" a "to" no fim da sequência de "from". Religar uma ligação do código que estava
// marcada como removida só desfaz a marca.
export function connect(draft: FlowGraph, from: string, to: string, label?: string): FlowGraph {
  if (from === to) return draft;
  const existing = draft.edges.find((e) => e.from === from && e.to === to && (label === undefined || e.label === label));
  if (existing) {
    if (existing.change !== ChangeKind.Removed) return draft;
    return { ...draft, edges: draft.edges.map((e) => (e === existing ? withoutChange(e) : e)) };
  }
  const order = Math.max(0, ...draft.edges.filter((e) => e.from === from).map((e) => e.order)) + 1;
  const edge: FlowEdge = { from, to, order, confidence: EdgeConfidence.Extracted, change: ChangeKind.Added, label };
  return { ...draft, edges: [...draft.edges, edge] };
}

// Mesma ligação: origem, destino e ramo (o objeto pode vir de outra cópia do rascunho).
export function sameEdge(a: FlowEdge, b: FlowEdge): boolean {
  return a.from === b.from && a.to === b.to && (a.label ?? null) === (b.label ?? null);
}

// Ligação nova some; ligação do código vira "removida".
export function removeEdge(draft: FlowGraph, edge: FlowEdge): FlowGraph {
  const current = draft.edges.find((e) => sameEdge(e, edge));
  if (!current) return draft;
  if (isAdded(current)) return { ...draft, edges: draft.edges.filter((e) => e !== current) };
  return { ...draft, edges: draft.edges.map((e) => (e === current ? { ...e, change: ChangeKind.Removed } : e)) };
}

// Mover um passo: a ligação sai de onde estava e entra no novo lugar (origem ou destino).
export function reassign(draft: FlowGraph, edge: FlowEdge, next: { from?: string; to?: string }): FlowGraph {
  const from = next.from ?? edge.from;
  const to = next.to ?? edge.to;
  if (from === edge.from && to === edge.to) return draft;
  return connect(removeEdge(draft, edge), from, to, edge.label);
}

// Passo novo some com as ligações dele; passo do código vira "sai do fluxo" (com as ligações).
export function removeStep(draft: FlowGraph, nodeId: string): FlowGraph {
  if (nodeId === draft.entryNodeId) return draft;
  const node = draft.nodes.find((n) => n.id === nodeId);
  if (!node) return draft;
  if (isAdded(node)) {
    return {
      ...draft,
      nodes: draft.nodes.filter((n) => n.id !== nodeId),
      edges: draft.edges.filter((e) => e.from !== nodeId && e.to !== nodeId),
    };
  }
  return {
    ...draft,
    nodes: draft.nodes.map((n) => (n.id === nodeId ? { ...n, change: ChangeKind.Removed } : n)),
    edges: draft.edges
      .filter((e) => !(isAdded(e) && (e.from === nodeId || e.to === nodeId)))
      .map((e) => (e.from === nodeId || e.to === nodeId ? { ...e, change: ChangeKind.Removed } : e)),
  };
}

// Volta um passo do código que tinha sido tirado (e as ligações que saíram com ele).
export function restoreStep(draft: FlowGraph, nodeId: string): FlowGraph {
  return {
    ...draft,
    nodes: draft.nodes.map((n) => (n.id === nodeId && n.change === ChangeKind.Removed ? (n.intent ? { ...n, change: ChangeKind.Modified } : withoutChange(n)) : n)),
    edges: draft.edges.map((e) => ((e.from === nodeId || e.to === nodeId) && e.change === ChangeKind.Removed ? withoutChange(e) : e)),
  };
}

// O que o passo deve passar a fazer. Em passo do código, texto vazio desfaz o "muda".
export function setIntent(draft: FlowGraph, nodeId: string, intent: string): FlowGraph {
  const text = intent.trim();
  return {
    ...draft,
    nodes: draft.nodes.map((n) => {
      if (n.id !== nodeId) return n;
      if (isAdded(n)) return { ...n, intent: text || n.intent };
      if (n.change === ChangeKind.Removed) return { ...n, intent: text || undefined };
      return text ? { ...n, change: ChangeKind.Modified, intent: text } : withoutChange({ ...n, intent: undefined });
    }),
  };
}

// Mudanças como quem vai programar enxerga (mesmo critério do prompt da Fase 8): cada passo
// novo, que muda ou que sai conta uma vez com as ligações dele; passo movido (ligação que sai e
// ligação nova que chega ao mesmo passo) conta uma; o resto das ligações conta uma cada.
export function changeCount(draft: FlowGraph): number {
  const touched = new Set(draft.nodes.filter((n) => n.change === ChangeKind.Added || n.change === ChangeKind.Removed).map((n) => n.id));
  const steps = draft.nodes.filter((n) => n.change !== undefined).length;
  const loose = draft.edges.filter((e) => e.change !== undefined && !touched.has(e.from) && !touched.has(e.to));
  const removed = loose.filter((e) => e.change === ChangeKind.Removed);
  const added = loose.filter((e) => e.change === ChangeKind.Added);
  const moved = removed.filter((r) => added.some((a) => a.to === r.to && a.from !== r.from)).length;
  return steps + removed.length + added.length - moved;
}

function withoutChange<T extends { change?: ChangeKind }>(item: T): T {
  const copy = { ...item };
  delete copy.change;
  return copy;
}
