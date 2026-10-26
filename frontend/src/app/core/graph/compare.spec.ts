import { ChangeKind, EdgeConfidence, FlowEdge, FlowGraph, NodeKind } from '../models';
import { compareGraphs } from './compare';

const edge = (from: string, to: string): FlowEdge => ({ from, to, order: 0, confidence: EdgeConfidence.Extracted });
const graph = (ids: string[], edges: FlowEdge[]): FlowGraph => ({
  schemaVersion: 1,
  entryPoint: 'Job.Run',
  entryNodeId: 'a',
  nodes: ids.map((id) => ({ id, kind: NodeKind.Method, label: id })),
  edges,
});

describe('compareGraphs', () => {
  it('marca o que entrou, devolve o que saiu e conta os dois', () => {
    const before = graph(['a', 'b', 'c'], [edge('a', 'b'), edge('b', 'c')]);
    const after = graph(['a', 'b', 'd'], [edge('a', 'b'), edge('b', 'd')]);

    const result = compareGraphs(before, after);

    expect(result.added).toBe(1);
    expect(result.removed).toBe(1);
    const change = (id: string) => result.graph.nodes.find((n) => n.id === id)?.change;
    expect(change('a')).toBeUndefined();
    expect(change('d')).toBe(ChangeKind.Added);
    expect(change('c')).toBe(ChangeKind.Removed);
    const edgeChange = (from: string, to: string) => result.graph.edges.find((e) => e.from === from && e.to === to)?.change;
    expect(edgeChange('a', 'b')).toBeUndefined();
    expect(edgeChange('b', 'd')).toBe(ChangeKind.Added);
    expect(edgeChange('b', 'c')).toBe(ChangeKind.Removed);
  });

  it('levantamentos iguais não marcam nada e mantêm os achados', () => {
    const same = { ...graph(['a', 'b'], [edge('a', 'b')]), findings: [] };
    const result = compareGraphs(same, same);

    expect(result.added + result.removed).toBe(0);
    expect(result.graph.findings).toEqual(same.findings);
    expect(result.graph.nodes.every((n) => n.change === undefined)).toBeTrue();
  });
});
