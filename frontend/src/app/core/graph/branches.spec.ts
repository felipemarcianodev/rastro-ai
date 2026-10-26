import { EdgeConfidence, FlowEdge, FlowGraph, FlowNode, NodeKind } from '../models';
import { FoldState, foldStates, isSection } from './branches';

const node = (id: string, kind: NodeKind, label = id): FlowNode => ({ id, kind, label });
const edge = (from: string, to: string, order: number, label?: string): FlowEdge => ({ from, to, order, label, confidence: EdgeConfidence.Extracted });

// Como a criação de uma reserva de estoque: validações em sequência, cada uma com o throw.
function guards(): FlowGraph {
  const nodes = [node('entry', NodeKind.Entry, 'Controller.Reserve'), node('create', NodeKind.Method, 'Reservation.Create')];
  const edges = [edge('entry', 'create', 0)];
  for (let i = 0; i < 3; i++) {
    nodes.push(node(`if${i}`, NodeKind.Decision, `if (guarda ${i})`), node(`throw${i}`, NodeKind.Throw, 'throw new ArgumentException'));
    edges.push(edge('create', `if${i}`, i), edge(`if${i}`, `throw${i}`, 0, 'then'));
  }
  return { schemaVersion: 1, entryPoint: 'Controller.Reserve', entryNodeId: 'entry', nodes, edges };
}

describe('detalhe do método (▾/▸)', () => {
  it('método com passos abre e fecha; a entrada e o método sem passos não', () => {
    const graph = guards();
    graph.nodes.push(node('vazio', NodeKind.Method, 'Util.Nada'));
    graph.edges.push(edge('entry', 'vazio', 1));

    const states = foldStates(graph, 'collapsed:');

    expect(states.get('create')).toBe(FoldState.Open);
    expect(states.has('entry')).toBeFalse();
    expect(states.has('vazio')).toBeFalse();
  });

  it('fechado é quem já tem o "+N passos" no grafo', () => {
    const graph = guards();
    graph.nodes.push(node('collapsed:create', NodeKind.Jump, '+12 passos'));

    expect(foldStates(graph, 'collapsed:').get('create')).toBe(FoldState.Closed);
  });
});

describe('seção do bloco', () => {
  it('só o que sai do try com rótulo é seção; o corpo do laço e o then, não', () => {
    expect(isSection(edge('t', 'a', 0, 'try'), NodeKind.Try)).toBeTrue();
    expect(isSection(edge('t', 'b', 1, 'catch (Exception)'), NodeKind.Try)).toBeTrue();
    expect(isSection(edge('l', 'c', 0, 'each'), NodeKind.Loop)).toBeFalse();
    expect(isSection(edge('i', 'd', 0, 'then'), NodeKind.Decision)).toBeFalse();
  });
});
