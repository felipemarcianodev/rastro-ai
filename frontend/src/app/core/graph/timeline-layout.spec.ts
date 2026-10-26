import { EdgeConfidence, FlowEdge, FlowGraph, FlowNode, NodeKind } from '../models';
import { BlockKind, FlowAxis, Port, flowDrawing, timelineDrawing, timelineRoutes } from './timeline-layout';

const node = (id: string, kind: NodeKind, label = id): FlowNode => ({ id, kind, label });
const edge = (from: string, to: string, order: number, label?: string): FlowEdge => ({ from, to, order, label, confidence: EdgeConfidence.Extracted });

// Como o job de um sistema de pedidos: Begin, depois um try com a busca, um if, o laço e
// o MarkSuccess; no catch, MarkFailure e throw. A busca chama outro método.
function alertJob(): FlowGraph {
  return {
    schemaVersion: 1,
    entryPoint: 'OrderJob.Execute',
    entryNodeId: 'job',
    nodes: [
      node('job', NodeKind.Entry),
      node('begin', NodeKind.Method),
      node('try', NodeKind.Try, 'try'),
      node('get', NodeKind.Method),
      node('query', NodeKind.Method),
      node('if', NodeKind.Decision, 'if (pendingAlerts.Count == 0)'),
      node('ret', NodeKind.Return, 'return'),
      node('loop', NodeKind.Loop, 'foreach (var alert in pendingAlerts)'),
      node('send', NodeKind.Method),
      node('success', NodeKind.Method),
      node('failure', NodeKind.Method),
      node('throw', NodeKind.Throw, 'throw'),
    ],
    edges: [
      edge('job', 'begin', 0),
      edge('job', 'try', 1),
      edge('try', 'get', 0, 'try'),
      edge('try', 'if', 1, 'try'),
      edge('try', 'loop', 2, 'try'),
      edge('try', 'success', 3, 'try'),
      edge('try', 'failure', 4, 'catch (Exception)'),
      edge('try', 'throw', 5, 'catch (Exception)'),
      edge('get', 'query', 0),
      edge('if', 'ret', 0, 'then'),
      edge('loop', 'send', 0, 'each'),
    ],
  };
}

describe('linha do tempo (Fase 20)', () => {
  it('os passos de um bloco descem um embaixo do outro, na ordem do código', () => {
    const { boxes } = timelineDrawing(alertJob());
    const order = ['get', 'if', 'loop', 'success', 'failure', 'throw'].map((id) => boxes.get(id)!);

    order.slice(1).forEach((box, i) => expect(box.y).toBeGreaterThan(order[i].y + order[i].height));
    expect(new Set(order.map((b) => b.x)).size).toBe(1);
  });

  it('o que um método chama sai para a direita, e o passo seguinte vem logo abaixo dele', () => {
    const { boxes } = timelineDrawing(alertJob());
    const [get, query, next] = ['get', 'query', 'if'].map((id) => boxes.get(id)!);

    expect(query.x).toBeGreaterThan(get.x + get.width);
    expect(query.y).toBe(get.y);
    expect(next.y).toBeLessThan(get.y + get.height + 40);
  });

  it('o corpo do laço também sai para a direita, e o try vira bloco com o catch como seção', () => {
    const { boxes, blocks } = timelineDrawing(alertJob());
    const [loop, send] = ['loop', 'send'].map((id) => boxes.get(id)!);

    expect(send.x).toBeGreaterThan(loop.x + loop.width);
    const tries = blocks.filter((b) => b.kind === BlockKind.Try);
    expect(tries.map((b) => b.id)).toEqual(['try']);
    expect(tries[0].sections.map((s) => s.label)).toEqual(['catch (Exception)']);
  });

  it('galho com "else" ganha cabeçalho; o "then" sozinho, não', () => {
    const graph: FlowGraph = {
      schemaVersion: 1,
      entryPoint: 'M',
      entryNodeId: 'm',
      nodes: [node('m', NodeKind.Entry), node('a', NodeKind.Decision), node('b', NodeKind.Decision), node('x', NodeKind.Method), node('y', NodeKind.Method), node('z', NodeKind.Method)],
      edges: [edge('m', 'a', 0), edge('m', 'b', 1), edge('a', 'x', 0, 'then'), edge('b', 'y', 0, 'then'), edge('b', 'z', 1, 'else')],
    };

    expect(timelineDrawing(graph).heads.map((h) => h.label)).toEqual(['Se sim', 'Se não']);
  });

  it('horizontal (Fase 21): os passos seguem para a direita e o que o método chama desce', () => {
    const { boxes } = flowDrawing(alertJob(), FlowAxis.Right);
    const order = ['get', 'if', 'loop', 'success'].map((id) => boxes.get(id)!);
    const [get, query] = ['get', 'query'].map((id) => boxes.get(id)!);

    order.slice(1).forEach((box, i) => expect(box.x).toBeGreaterThan(order[i].x + order[i].width));
    expect(new Set(order.map((b) => b.y)).size).toBe(1);
    expect(query.y).toBeGreaterThan(get.y + get.height);
    expect(query.x).toBe(get.x);
  });

  it('passos numerados na ordem de leitura e profundidade pela cadeia de chamadas (Fase 23)', () => {
    const { numbers, depths, outline } = timelineDrawing(alertJob());

    expect(['begin', 'try', 'get', 'query', 'if', 'ret', 'loop', 'send', 'success', 'failure', 'throw'].map((id) => numbers.get(id))).toEqual([
      '1', '2', '2.1', '2.1.1', '2.2', '2.2.1', '2.3', '2.3.1', '2.4', '2.5', '2.6',
    ]);
    // O corpo do try e do laço é código da entrada; o que o método chama fica um nível mais fundo.
    expect(depths.get('loop')).toBe(0);
    expect(depths.get('send')).toBe(0);
    expect(depths.get('query')).toBe(1);
    expect(outline.map((i) => i.of)).toEqual(['begin', 'try']);
    expect(outline[1].children.find((i) => i.of === 'failure')?.head).toBe('catch (Exception)');
  });

  it('o laço ganha moldura com "Repete ..." em volta do que se repete (Fase 23)', () => {
    const g = alertJob();
    g.nodes.find((n) => n.id === 'loop')!.label = 'Para cada alert em pendingAlerts';
    const { blocks, boxes } = timelineDrawing(g);
    const frame = blocks.find((b) => b.label?.startsWith('↻'))!;
    const send = boxes.get('send')!;

    expect(frame.label).toBe('↻ Repete para cada alert em pendingAlerts');
    expect(send.x).toBeGreaterThan(frame.x);
    expect(send.y + send.height).toBeLessThan(frame.y + frame.height);
  });

  it('método chamado em dois lugares: a segunda chamada vira passo repetido onde acontece', () => {
    const g = alertJob();
    g.edges.push(edge('if', 'success', 1, 'then'));
    const { boxes, repeats } = timelineDrawing(g);
    const [repeat] = [...repeats.values()];

    expect(repeat.of).toBe('success');
    expect(boxes.has(repeat.id)).toBeTrue();
  });

  it('ligações: "depois" vem do passo anterior por cima; "chama" sai pela direita', () => {
    const g = alertJob();
    const routes = timelineRoutes(g);
    const route = (from: string, to: string) => routes.get(g.edges.findIndex((e) => e.from === from && e.to === to));

    expect(route('job', 'begin')).toEqual({ source: Port.Out + 'job', target: Port.Top + 'begin' });
    expect(route('job', 'try')).toEqual({ source: Port.Out + 'begin', target: Port.Top + 'try' });
    expect(route('try', 'get')).toEqual({ source: Port.Out + 'try', target: Port.In + 'get' });
    expect(route('try', 'if')).toEqual({ source: Port.Out + 'get', target: Port.Top + 'if' });
    expect(route('try', 'failure')).toEqual({ source: Port.Out + 'try', target: Port.In + 'failure' });
    expect(route('try', 'throw')).toEqual({ source: Port.Out + 'failure', target: Port.Top + 'throw' });
    expect(route('get', 'query')).toEqual({ source: Port.Side + 'get', target: Port.In + 'query' });
    expect(route('loop', 'send')).toEqual({ source: Port.Side + 'loop', target: Port.In + 'send' });
  });
});
