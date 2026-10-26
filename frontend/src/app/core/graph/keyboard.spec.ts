import { EdgeConfidence, FlowEdge, FlowGraph, NodeKind } from '../models';
import { StepMove, stepFrom } from './keyboard';

const edge = (from: string, to: string, order: number): FlowEdge => ({ from, to, order, confidence: EdgeConfidence.Extracted });

// a -> b (0), a -> c (1), a -> d (2); b -> e; x -> e
const graph: FlowGraph = {
  schemaVersion: 1,
  entryPoint: 'A.Run',
  entryNodeId: 'a',
  nodes: ['a', 'b', 'c', 'd', 'e', 'x'].map((id) => ({ id, kind: NodeKind.Method, label: id })),
  edges: [edge('a', 'c', 1), edge('a', 'b', 0), edge('a', 'd', 2), edge('b', 'e', 0), edge('x', 'e', 0)],
};

describe('stepFrom', () => {
  it('para a frente segue a primeira ligação na ordem de execução', () => {
    expect(stepFrom(graph, 'a', StepMove.Forward)).toBe('b');
    expect(stepFrom(graph, 'e', StepMove.Forward)).toBeNull();
  });

  it('para trás volta pelo caminho de onde se veio', () => {
    expect(stepFrom(graph, 'e', StepMove.Back, ['a', 'b', 'e'])).toBe('b');
    expect(stepFrom(graph, 'a', StepMove.Back)).toBeNull();
  });

  it('para os lados anda entre os irmãos e dá a volta', () => {
    expect(stepFrom(graph, 'b', StepMove.Next)).toBe('c');
    expect(stepFrom(graph, 'd', StepMove.Next)).toBe('b');
    expect(stepFrom(graph, 'b', StepMove.Previous)).toBe('d');
  });

  it('passo sem irmãos não anda para os lados', () => {
    expect(stepFrom(graph, 'e', StepMove.Next, ['a', 'b', 'e'])).toBeNull();
  });
});
