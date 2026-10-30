import { EdgeConfidence, FlowEdge, FlowGraph, FlowNode, NodeKind, SourceLanguage } from '../models';
import { BlockKind, FlowAxis, Port, SystemInfo, flowDrawing, surveySystems, systemLabel, timelineDrawing, timelineRoutes } from './timeline-layout';

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

describe('moldura do sistema (Fase 25)', () => {
  // Pedidos chama Pagamentos por HTTP: o trecho de Pagamentos fica numa moldura própria.
  const crossGraph = (): FlowGraph => ({
    schemaVersion: 2,
    entryPoint: 'PedidosController.Cancelar',
    entryNodeId: 'cancelar',
    nodes: [
      node('cancelar', NodeKind.Entry),
      node('estornar', NodeKind.Method),
      { ...node('pagamentos::criar', NodeKind.Method, 'EstornosController.Criar'), system: 'pagamentos' },
      { ...node('pagamentos::grava', NodeKind.Method, 'Repo.Salvar'), system: 'pagamentos' },
    ],
    edges: [
      edge('cancelar', 'estornar', 0),
      { ...edge('estornar', 'pagamentos::criar', 0), http: { method: 'POST', route: 'api/estornos' } },
      edge('pagamentos::criar', 'pagamentos::grava', 0),
    ],
  });

  // UI-SYS-001
  it('flowDrawing_trechoDeOutroSistema_ganhaMolduraComCabecalho', () => {
    const drawing = flowDrawing(crossGraph(), FlowAxis.Down);
    const frame = drawing.blocks.find((b) => b.kind === BlockKind.System)!;
    const inside = ['pagamentos::criar', 'pagamentos::grava'].map((id) => drawing.boxes.get(id)!);

    expect(BlockKind.System).toBe(2);
    expect(frame.label).toBe('pagamentos · .NET');
    for (const box of inside) {
      expect(box.x).toBeGreaterThanOrEqual(frame.x);
      expect(box.y).toBeGreaterThanOrEqual(frame.y);
      expect(box.x + box.width).toBeLessThanOrEqual(frame.x + frame.width);
      expect(box.y + box.height).toBeLessThanOrEqual(frame.y + frame.height);
    }
    // O passo que chama (primeiro sistema) fica fora da moldura: ela abre num galho ao lado.
    const outside = drawing.boxes.get('estornar')!;
    expect(outside.x + outside.width).toBeLessThanOrEqual(frame.x);
  });

  // UI-SYS-002
  it('flowDrawing_semSystemEmNenhumNo_naoCriaMolduraDeSistema', () => {
    const drawing = flowDrawing(alertJob(), FlowAxis.Down);

    expect(drawing.blocks.some((b) => b.kind === BlockKind.System)).toBeFalse();
  });

  // Pagamentos tem uma decisão com dois galhos, e o galho "sim" chama um terceiro sistema.
  const branchGraph = (): FlowGraph => {
    const pag = (id: string, kind: NodeKind, label = id): FlowNode => ({ ...node(id, kind, label), system: 'pagamentos' });
    const not = (id: string, label = id): FlowNode => ({ ...node(id, NodeKind.Method, label), system: 'notificacoes' });
    return {
      schemaVersion: 2,
      entryPoint: 'PedidosController.Cancelar',
      entryNodeId: 'cancelar',
      nodes: [
        node('cancelar', NodeKind.Entry),
        node('estornar', NodeKind.Method),
        pag('pag::criar', NodeKind.Method),
        pag('pag::cartao', NodeKind.Decision),
        pag('pag::estorna', NodeKind.Method),
        pag('pag::boleto', NodeKind.Decision),
        pag('pag::grava', NodeKind.Method),
        pag('pag::erro', NodeKind.Method),
        not('not::enviar'),
        not('not::grava'),
      ],
      edges: [
        edge('cancelar', 'estornar', 0),
        edge('estornar', 'pag::criar', 0),
        edge('pag::criar', 'pag::cartao', 0),
        edge('pag::cartao', 'pag::estorna', 0, 'then'),
        edge('pag::estorna', 'not::enviar', 0),
        edge('not::enviar', 'not::grava', 0),
        edge('pag::cartao', 'pag::boleto', 1, 'else'),
        edge('pag::boleto', 'pag::grava', 0, 'then'),
        edge('pag::grava', 'pag::erro', 0),
      ],
    };
  };

  const within = (inner: { x: number; y: number; width: number; height: number }, outer: { x: number; y: number; width: number; height: number }) =>
    inner.x >= outer.x && inner.y >= outer.y && inner.x + inner.width <= outer.x + outer.width && inner.y + inner.height <= outer.y + outer.height;

  const axes: [string, FlowAxis][] = [
    ['vertical', FlowAxis.Down],
    ['horizontal', FlowAxis.Right],
  ];

  for (const [name, axis] of axes) {
    // UI-SYS-003 (vertical), UI-SYS-004 (horizontal)
    it(`flowDrawing_trechoComDecisaoDeDoisGalhos_molduraEnvolveTodosOsNos_${name}`, () => {
      const graph = branchGraph();
      const drawing = flowDrawing(graph, axis);
      const frames = drawing.blocks.filter((b) => b.kind === BlockKind.System);
      const frameOf = (system: string) => frames.find((b) => b.label === `${system} · .NET`)!;

      expect(frames.length).toBe(2);
      for (const system of ['pagamentos', 'notificacoes']) {
        for (const n of graph.nodes.filter((x) => x.system === system)) {
          expect(within(drawing.boxes.get(n.id)!, frameOf(system)))
            .withContext(`${n.id} dentro da moldura de ${system}`)
            .toBeTrue();
        }
      }
      // Aninhamento: a moldura de notificações fica dentro da de pagamentos.
      expect(within(frameOf('notificacoes'), frameOf('pagamentos'))).toBeTrue();
      // Não invade o passo do primeiro sistema.
      const outside = drawing.boxes.get('estornar')!;
      const overlaps = (a: typeof outside, b: typeof outside) => a.x < b.x + b.width && b.x < a.x + a.width && a.y < b.y + b.height && b.y < a.y + a.height;
      expect(overlaps(outside, frameOf('pagamentos'))).toBeFalse();
    });
  }

  // UI-SYS-005
  it('flowDrawing_trechoComDecisaoDeDoisGalhos_molduraEnvolveTodosOsNos_arvore', () => {
    // A árvore usa o mesmo desenho para baixo.
    const graph = branchGraph();
    const drawing = timelineDrawing(graph);
    const frame = drawing.blocks.find((b) => b.kind === BlockKind.System && b.label === 'pagamentos · .NET')!;

    for (const n of graph.nodes.filter((x) => x.system === 'pagamentos')) {
      expect(within(drawing.boxes.get(n.id)!, frame)).withContext(n.id).toBeTrue();
    }
  });
});

describe('linguagem na moldura do sistema (Fase 26)', () => {
  // A tela Angular chama a API de pedidos por HTTP.
  const angularGraph = (): FlowGraph => ({
    schemaVersion: 2,
    entryPoint: 'DetalhePedidoComponent.cancelar',
    entryNodeId: 'cancelar',
    nodes: [
      node('cancelar', NodeKind.Entry),
      node('post', NodeKind.External, 'POST api/pedidos/{}/cancelamento'),
      { ...node('pedidos::cancelar', NodeKind.Method, 'PedidosController.Cancelar'), system: 'pedidos-api' },
    ],
    edges: [
      edge('cancelar', 'post', 0),
      { ...edge('post', 'pedidos::cancelar', 0), http: { method: 'POST', route: 'api/pedidos/{}/cancelamento' } },
    ],
  });
  const systems: SystemInfo[] = [
    { name: 'loja-web', language: SourceLanguage.Angular },
    { name: 'pedidos-api', language: SourceLanguage.DotNet },
  ];

  // UI-ANG-001
  it('systemLabel_porLinguagem_escreveAngularOuDotNet', () => {
    expect(systemLabel('loja-web', SourceLanguage.Angular)).toBe('loja-web · Angular');
    expect(systemLabel('pedidos-api', SourceLanguage.DotNet)).toBe('pedidos-api · .NET');
    expect(systemLabel('pedidos-api')).toBe('pedidos-api · .NET');
  });

  // UI-ANG-002
  it('flowDrawing_primeiroSistemaAngular_ganhaMolduraEAsOutrasUsamSuaLinguagem', () => {
    const drawing = flowDrawing(angularGraph(), FlowAxis.Down, () => 0, systems);
    const frames = drawing.blocks.filter((b) => b.kind === BlockKind.System);
    const web = frames.find((b) => b.label === 'loja-web · Angular')!;
    const api = frames.find((b) => b.label === 'pedidos-api · .NET')!;

    expect(frames.length).toBe(2);
    // A moldura da tela envolve tudo, inclusive a moldura da API; o cabeçalho não cobre a entrada.
    expect(web.x).toBeLessThanOrEqual(api.x);
    expect(web.y).toBeLessThanOrEqual(api.y);
    expect(web.x + web.width).toBeGreaterThanOrEqual(api.x + api.width);
    expect(web.y + web.height).toBeGreaterThanOrEqual(api.y + api.height);
    const entry = drawing.boxes.get('cancelar')!;
    expect(entry.y).toBeGreaterThan(web.y);
    expect(entry.x).toBeGreaterThanOrEqual(web.x);
  });

  // UI-ANG-003
  it('flowDrawing_sistemaUnicoAngular_naoCriaMoldura', () => {
    const drawing = flowDrawing(angularGraph(), FlowAxis.Down, () => 0, [systems[0]]);

    expect(drawing.blocks.some((b) => b.label === 'loja-web · Angular')).toBeFalse();
  });

  // UI-ANG-004
  it('flowDrawing_primeiroSistemaDotNet_naoGanhaMolduraNoPrimeiro', () => {
    const drawing = flowDrawing(angularGraph(), FlowAxis.Down, () => 0, [
      { name: 'pedidos', language: SourceLanguage.DotNet },
      { name: 'pedidos-api', language: SourceLanguage.DotNet },
    ]);

    expect(drawing.blocks.filter((b) => b.kind === BlockKind.System).map((b) => b.label)).toEqual(['pedidos-api · .NET']);
  });

  // UI-ANG-005
  it('surveySystems_levantamentoComTrechos_listaOPrimeiroECostura', () => {
    const list = surveySystems({
      repository: 'loja-web',
      language: SourceLanguage.Angular,
      entryPoint: 'DetalhePedidoComponent.cancelar',
      segments: [{ order: 1, repository: 'pedidos-api', language: SourceLanguage.DotNet, solutionPath: 'P.slnx', entryPoint: 'x' }],
    });

    expect(list).toEqual(systems);
  });
});
