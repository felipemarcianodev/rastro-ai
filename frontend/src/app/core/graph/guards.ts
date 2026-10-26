import { FlowEdge, FlowGraph, FlowNode, NodeKind } from '../models';
import { GUARD_ARROW } from './layout';
import { BranchLabel } from './branches';

// Validação num passo só (Fase 19): um "if" sem "else" cujo único passo é um throw, return,
// break ou continue é uma ideia só ("se o produto estiver vazio, erro"). Na tela vira um passo,
// com o desfecho na ponta, em vez de três peças (if, etiqueta "then" e o throw). É só desenho:
// o grafo guardado continua com os dois nós e a ligação.

const OUTCOME_KINDS = new Set([NodeKind.Throw, NodeKind.Return, NodeKind.Jump]);

export { GUARD_ARROW };

// Texto do desfecho na ponta da validação: "throw new X" vira "throw X" (o "new" só ocupa espaço).
export function outcomeText(outcome: FlowNode): string {
  return outcome.label.replace(/^throw new /, 'throw ');
}

// Validação -> o passo de desfecho que ela engole. "keep" diz quais desfechos precisam ficar
// separados (têm marca própria: mudança do rascunho, achado, anotação).
export function findGuards(graph: FlowGraph, keep: (node: FlowNode) => boolean): Map<string, FlowNode> {
  const nodes = new Map(graph.nodes.map((n) => [n.id, n]));
  const outgoing = new Map<string, FlowEdge[]>();
  const incoming = new Map<string, number>();
  for (const edge of graph.edges) {
    if (edge.from === edge.to) continue;
    outgoing.set(edge.from, [...(outgoing.get(edge.from) ?? []), edge]);
    incoming.set(edge.to, (incoming.get(edge.to) ?? 0) + 1);
  }

  const guards = new Map<string, FlowNode>();
  for (const node of graph.nodes) {
    if (node.kind !== NodeKind.Decision) continue;
    const out = outgoing.get(node.id) ?? [];
    if (out.length !== 1) continue;
    const [edge] = out;
    const outcome = nodes.get(edge.to);
    if (edge.label !== BranchLabel.Then || edge.change !== undefined || !outcome) continue;
    if (!OUTCOME_KINDS.has(outcome.kind) || outgoing.has(outcome.id) || incoming.get(outcome.id) !== 1) continue;
    if (keep(outcome)) continue;
    guards.set(node.id, outcome);
  }
  return guards;
}

// Texto de um passo na tela: o do código, ou em português na linha do tempo (Fase 20).
export type NodeText = (node: FlowNode) => string;
export const codeText: NodeText = (node) => node.label;

// O grafo como a tela desenha: sem os desfechos engolidos, e cada passo com o texto que aparece
// (a largura do passo e a exportação contam com ele, e com o desfecho na validação).
export function withGuards(graph: FlowGraph, guards: ReadonlyMap<string, FlowNode>, text: NodeText = codeText, outcomeOf: NodeText = outcomeText): FlowGraph {
  if (guards.size === 0 && text === codeText) return graph;
  const hidden = new Set([...guards.values()].map((n) => n.id));
  return {
    ...graph,
    nodes: graph.nodes
      .filter((n) => !hidden.has(n.id))
      .map((n) => {
        const outcome = guards.get(n.id);
        const label = outcome ? text(n) + GUARD_ARROW + outcomeOf(outcome) : text(n);
        return label === n.label ? n : { ...n, label };
      }),
    edges: graph.edges.filter((e) => !hidden.has(e.to) && !hidden.has(e.from)),
  };
}
