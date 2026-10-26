import { FlowGraph } from '../models';

// Andar pelo grafo com as setas (UX-02): para a frente segue a 1ª ligação que sai; para trás
// volta ao passo anterior do caminho (ou à 1ª ligação que chega); para os lados anda entre os
// irmãos, os passos que o mesmo pai chama, na ordem de execução.
export enum StepMove {
  Forward = 0,
  Back = 1,
  Next = 2,
  Previous = 3,
}

export function stepFrom(graph: FlowGraph, id: string, move: StepMove, path: readonly string[] = []): string | null {
  const outgoing = (from: string) =>
    graph.edges
      .filter((e) => e.from === from && e.to !== from)
      .sort((a, b) => a.order - b.order)
      .map((e) => e.to);
  const parentOf = (node: string): string | null => {
    const index = path.indexOf(node);
    if (index > 0) return path[index - 1];
    return graph.edges.find((e) => e.to === node && e.from !== node)?.from ?? null;
  };

  if (move === StepMove.Forward) return outgoing(id)[0] ?? null;
  if (move === StepMove.Back) return parentOf(id);

  const parent = parentOf(id);
  if (!parent) return null;
  const siblings = outgoing(parent);
  const index = siblings.indexOf(id);
  if (index < 0 || siblings.length < 2) return null;
  const next = move === StepMove.Next ? index + 1 : index - 1;
  return siblings[(next + siblings.length) % siblings.length];
}
