import { EdgeConfidence, FlowGraph, NodeKind } from '../models';
import { trailTo } from './trail';

// entry -> a -> c
//       -> b -> c   (dois caminhos até c; o primeiro na ordem de execução vence)
//       -> d        (d não leva a lugar nenhum)
// x fica solto (inalcançável)
const graph: FlowGraph = {
  schemaVersion: 1,
  entryPoint: 'Job.Run',
  entryNodeId: 'entry',
  nodes: ['entry', 'a', 'b', 'c', 'd', 'x'].map((id) => ({ id, kind: id === 'entry' ? NodeKind.Entry : NodeKind.Method, label: id })),
  edges: [
    { from: 'entry', to: 'b', order: 2, confidence: EdgeConfidence.Extracted },
    { from: 'entry', to: 'a', order: 1, confidence: EdgeConfidence.Extracted },
    { from: 'a', to: 'c', order: 1, confidence: EdgeConfidence.Extracted },
    { from: 'b', to: 'c', order: 1, confidence: EdgeConfidence.Extracted },
    { from: 'entry', to: 'd', order: 3, confidence: EdgeConfidence.Extracted },
  ],
};

describe('trailTo', () => {
  it('segue o caminho mais curto respeitando a ordem de execução', () => {
    const trail = trailTo(graph, 'c');

    expect(trail.path).toEqual(['entry', 'a', 'c']);
    expect([...trail.edgeIndexes].sort()).toEqual([1, 2]);
    expect(trail.nodeIds.has('b')).toBeFalse();
  });

  it('na própria entrada, o caminho é só ela', () => {
    const trail = trailTo(graph, 'entry');

    expect(trail.path).toEqual(['entry']);
    expect(trail.edgeIndexes.size).toBe(0);
  });

  it('nó inalcançável fica sozinho, sem inventar caminho', () => {
    const trail = trailTo(graph, 'x');

    expect(trail.path).toEqual(['x']);
    expect(trail.edgeIndexes.size).toBe(0);
  });
});
