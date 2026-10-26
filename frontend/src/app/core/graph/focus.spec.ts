import { EdgeConfidence, FlowGraph, NodeKind } from '../models';
import { focusOn } from './focus';

const edge = (from: string, to: string) => ({ from, to, order: 0, confidence: EdgeConfidence.Extracted });

// entry -> a -> alvo -> b ; entry -> c (outro fluxo) ; a -> d (lateral) ; c -> alvo (outro caminho)
const graph: FlowGraph = {
  schemaVersion: 1,
  entryPoint: 'Job.Run',
  entryNodeId: 'entry',
  nodes: ['entry', 'a', 'alvo', 'b', 'c', 'd', 'x'].map((id) => ({ id, kind: NodeKind.Method, label: id })),
  edges: [edge('entry', 'a'), edge('a', 'alvo'), edge('alvo', 'b'), edge('entry', 'c'), edge('a', 'd'), edge('c', 'alvo'), edge('c', 'x')],
};

describe('focusOn', () => {
  it('mantém todos os caminhos até o passo e o que roda depois, sem as ligações laterais', () => {
    const focused = focusOn(graph, 'alvo');

    expect(focused.nodes.map((n) => n.id)).toEqual(['entry', 'a', 'alvo', 'b', 'c']);
    expect(focused.edges.map((e) => `${e.from}>${e.to}`)).toEqual(['entry>a', 'a>alvo', 'alvo>b', 'entry>c', 'c>alvo']);
  });

  it('passo inexistente devolve o grafo inteiro', () => {
    expect(focusOn(graph, 'sumiu')).toBe(graph);
  });

  it('na entrada, mostra tudo que é alcançável', () => {
    expect(focusOn(graph, 'entry').nodes.length).toBe(7);
  });
});
