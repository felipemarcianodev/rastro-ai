import { EdgeConfidence, FlowEdge, FlowGraph, FlowNode, NodeKind } from '../models';
import { GUARD_ARROW, findGuards, withGuards } from './guards';

const node = (id: string, kind: NodeKind, label = id): FlowNode => ({ id, kind, label });
const edge = (from: string, to: string, order: number, label?: string): FlowEdge => ({ from, to, order, label, confidence: EdgeConfidence.Extracted });
const graph = (nodes: FlowNode[], edges: FlowEdge[]): FlowGraph => ({ schemaVersion: 1, entryPoint: 'm', entryNodeId: 'm', nodes, edges });
const keepNone = () => false;

describe('validação num passo só (Fase 19)', () => {
  it('if sem else com só um throw vira um passo, com o desfecho no texto', () => {
    const g = graph(
      [node('m', NodeKind.Method), node('if', NodeKind.Decision, 'if (id == Guid.Empty)'), node('t', NodeKind.Throw, 'throw new ArgumentException')],
      [edge('m', 'if', 0), edge('if', 't', 0, 'then')],
    );

    const guards = findGuards(g, keepNone);
    const shown = withGuards(g, guards);

    expect(guards.get('if')?.id).toBe('t');
    expect(shown.nodes.map((n) => n.id)).toEqual(['m', 'if']);
    expect(shown.nodes[1].label).toBe('if (id == Guid.Empty)' + GUARD_ARROW + 'throw ArgumentException');
    expect(shown.edges).toEqual([edge('m', 'if', 0)]);
  });

  it('return e continue também; if com else, com dois passos ou com um método não', () => {
    const g = graph(
      [
        node('m', NodeKind.Method),
        node('r', NodeKind.Decision), node('ret', NodeKind.Return),
        node('j', NodeKind.Decision), node('cont', NodeKind.Jump),
        node('e', NodeKind.Decision), node('a', NodeKind.Throw), node('b', NodeKind.Return),
        node('two', NodeKind.Decision), node('log', NodeKind.Method), node('t2', NodeKind.Throw),
        node('call', NodeKind.Decision), node('svc', NodeKind.Method),
      ],
      [
        edge('r', 'ret', 0, 'then'),
        edge('j', 'cont', 0, 'then'),
        edge('e', 'a', 0, 'then'), edge('e', 'b', 1, 'else'),
        edge('two', 'log', 0, 'then'), edge('two', 't2', 1, 'then'),
        edge('call', 'svc', 0, 'then'),
      ],
    );

    expect([...findGuards(g, keepNone).keys()]).toEqual(['r', 'j']);
  });

  it('desfecho com marca própria (achado, anotação, mudança) fica separado', () => {
    const g = graph([node('if', NodeKind.Decision), node('t', NodeKind.Throw)], [edge('if', 't', 0, 'then')]);

    expect(findGuards(g, (n) => n.id === 't').size).toBe(0);
  });

  it('desfecho alcançado por outro caminho fica separado', () => {
    const g = graph(
      [node('if', NodeKind.Decision), node('other', NodeKind.Method), node('t', NodeKind.Throw)],
      [edge('if', 't', 0, 'then'), edge('other', 't', 0)],
    );

    expect(findGuards(g, keepNone).size).toBe(0);
  });
});
