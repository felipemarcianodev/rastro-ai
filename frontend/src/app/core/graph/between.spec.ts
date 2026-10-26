import { EdgeConfidence, FlowEdge, FlowGraph, NodeKind } from '../models';
import { pathsBetween } from './between';

const edge = (from: string, to: string, order = 0): FlowEdge => ({ from, to, order, confidence: EdgeConfidence.Extracted });

// a -> b -> d ; a -> c -> d ; d -> e ; a -> x (fora de qualquer caminho até e)
const graph: FlowGraph = {
  schemaVersion: 1,
  entryPoint: 'A.Run',
  entryNodeId: 'a',
  nodes: ['a', 'b', 'c', 'd', 'e', 'x'].map((id) => ({ id, kind: NodeKind.Method, label: id })),
  edges: [edge('a', 'b', 0), edge('a', 'c', 1), edge('b', 'd'), edge('c', 'd'), edge('d', 'e'), edge('a', 'x', 2)],
};

describe('pathsBetween', () => {
  it('junta todos os caminhos de A até B e lista o mais curto', () => {
    const result = pathsBetween(graph, 'a', 'e')!;

    expect([...result.nodeIds].sort()).toEqual(['a', 'b', 'c', 'd', 'e']);
    expect(result.edgeIndexes.has(5)).toBeFalse();
    expect(result.edgeIndexes.size).toBe(5);
    expect(result.path).toEqual(['a', 'b', 'd', 'e']);
  });

  it('sem caminho de A até B devolve null', () => {
    expect(pathsBetween(graph, 'e', 'a')).toBeNull();
    expect(pathsBetween(graph, 'x', 'e')).toBeNull();
  });

  it('A igual a B é o próprio passo', () => {
    expect(pathsBetween(graph, 'd', 'd')!.path).toEqual(['d']);
  });
});
