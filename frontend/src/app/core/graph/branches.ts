import { FlowEdge, FlowGraph, NodeKind } from '../models';

// Ramos do fluxo e o abrir e fechar do método, comuns às três visões (ver timeline-layout.ts).

// Rótulos de ramo que o extrator grava (FlowWalker). "case ..." e "catch (...)" variam.
export const BranchLabel = {
  Then: 'then',
  Else: 'else',
  Try: 'try',
  Each: 'each',
  Finally: 'finally',
  Catch: 'catch',
  Default: 'default',
} as const;

// Recuo das seções do bloco do "try" em relação ao próprio "try".
export const BLOCK_INDENT = 40;
// Folga do bloco em volta do "try" e dos passos dele.
export const BLOCK_PAD = 8;

// Ligação que é seção do bloco: tudo o que sai do "try" (o corpo, cada catch e o finally).
export function isSection(edge: FlowEdge, fromKind: NodeKind | undefined): boolean {
  return fromKind === NodeKind.Try && !!edge.label;
}

// Detalhe de um método (nas três visões): aberto (os passos dele aparecem) ou fechado ("+N passos").
export enum FoldState {
  Open = 0,
  Closed = 1,
}

export const FOLD_GLYPH: Record<FoldState, string> = {
  [FoldState.Open]: '▾',
  [FoldState.Closed]: '▸',
};

export const FOLD_ACTION: Record<FoldState, string> = {
  [FoldState.Open]: 'Fechar o detalhe deste método (tecla −)',
  [FoldState.Closed]: 'Abrir o detalhe deste método (tecla +)',
};

// Quais métodos podem abrir e fechar: os que têm passos depois deles, menos a entrada (fechar a
// entrada esconderia o fluxo inteiro). Fechado é quem já tem o "+N passos" no grafo.
export function foldStates(graph: FlowGraph, collapsedPrefix: string): Map<string, FoldState> {
  const ids = new Set(graph.nodes.map((n) => n.id));
  const withSteps = new Set(graph.edges.filter((e) => e.from !== e.to).map((e) => e.from));
  const states = new Map<string, FoldState>();
  for (const node of graph.nodes) {
    if (node.id === graph.entryNodeId || (node.kind !== NodeKind.Method && node.kind !== NodeKind.Entry)) continue;
    if (ids.has(collapsedPrefix + node.id)) states.set(node.id, FoldState.Closed);
    else if (withSteps.has(node.id)) states.set(node.id, FoldState.Open);
  }
  return states;
}
