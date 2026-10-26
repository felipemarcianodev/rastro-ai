import { EdgeConfidence, FlowEdge, FlowGraph, NodeKind } from '../models';
import { COLLAPSED_PREFIX, collapseAfter, collapsedOwner } from './collapse';

const edge = (from: string, to: string): FlowEdge => ({ from, to, order: 0, confidence: EdgeConfidence.Extracted });

// a -> b -> c -> d ; a -> e -> d (d também é alcançado sem passar por b) ; b -> f
const graph: FlowGraph = {
  schemaVersion: 1,
  entryPoint: 'A.Run',
  entryNodeId: 'a',
  nodes: ['a', 'b', 'c', 'd', 'e', 'f'].map((id) => ({ id, kind: NodeKind.Method, label: id })),
  edges: [edge('a', 'b'), edge('b', 'c'), edge('c', 'd'), edge('a', 'e'), edge('e', 'd'), edge('b', 'f')],
};

const ids = (g: FlowGraph) => g.nodes.map((n) => n.id).sort();

describe('collapseAfter', () => {
  it('troca o que só roda depois do passo por um nó "+N passos"', () => {
    const result = collapseAfter(graph, new Set(['b']));

    expect(ids(result)).toEqual(['a', 'b', 'd', 'e', COLLAPSED_PREFIX + 'b'].sort());
    const placeholder = result.nodes.find((n) => n.id === COLLAPSED_PREFIX + 'b')!;
    expect(placeholder.label).toBe('+2 passos');
    expect(result.edges.some((e) => e.from === 'b' && e.to === COLLAPSED_PREFIX + 'b')).toBeTrue();
    expect(result.edges.some((e) => e.to === 'c' || e.from === 'c')).toBeFalse();
  });

  it('mantém o passo que também é alcançado por outro caminho', () => {
    expect(ids(collapseAfter(graph, new Set(['b'])))).toContain('d');
  });

  it('recolhido dentro de outro recolhido some junto', () => {
    const result = collapseAfter(graph, new Set(['b', 'c']));

    expect(result.nodes.some((n) => n.id === COLLAPSED_PREFIX + 'c')).toBeFalse();
    expect(collapsedOwner(COLLAPSED_PREFIX + 'b')).toBe('b');
    expect(collapsedOwner('b')).toBeNull();
  });

  it('passo sem nada depois não ganha nó de recolhido', () => {
    expect(collapseAfter(graph, new Set(['f']))).toEqual(graph);
  });
});
