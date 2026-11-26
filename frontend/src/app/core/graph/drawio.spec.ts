import { BusinessFlow, BusinessKind, EdgeConfidence, FlowGraph, FlowMap, LabelOrigin, LabelState, MapBlockKind, MapLaneKind, MapLinkKind, NodeKind } from '../models';
import { businessLayout } from './business-layout';
import { DRAWIO_STYLE, businessToDrawio, codeReference, graphToDrawio, mapToDrawio, toDrawio } from './drawio';
import { mapGeometry } from './flow-map';
import { LayoutDirection } from './layout';

// Fase 54: o .drawio do grafo e do mapa. O draw.io lê o mxfile como XML; o rótulo é HTML escapado
// como atributo (escape duplo).

function parse(xml: string): Document {
  const doc = new DOMParser().parseFromString(xml, 'application/xml');
  expect(doc.getElementsByTagName('parsererror').length).withContext(xml).toBe(0);
  return doc;
}

const graph: FlowGraph = {
  schemaVersion: 1,
  entryPoint: 'PedidosController.Cancelar',
  entryNodeId: 'a',
  nodes: [
    { id: 'a', kind: NodeKind.Entry, label: 'PedidosController.Cancelar', symbol: 'PedidosController.Cancelar', location: { file: 'src/Api/PedidosController.cs', line: 12 } },
    { id: 'b', kind: NodeKind.Decision, label: 'if (prazo < "hoje" && ok)', location: { file: 'src/Api/CancelamentoService.cs', line: 30 } },
    { id: 'c', kind: NodeKind.Method, label: 'EstornosController.Criar', system: 'api-pagamentos', location: { file: 'src/EstornosController.cs', line: 8 } },
    { id: 'd', kind: NodeKind.Throw, label: 'throw PrazoException' },
  ],
  edges: [
    { from: 'a', to: 'b', order: 0, confidence: EdgeConfidence.Extracted },
    { from: 'b', to: 'c', order: 0, confidence: EdgeConfidence.Inferred, label: 'then', http: { method: 'POST', route: 'api/estornos' } },
    { from: 'b', to: 'd', order: 1, confidence: EdgeConfidence.Confirmed, label: 'else' },
  ],
};

const boxes = new Map([
  ['a', { x: 300, y: 200, width: 220, height: 56 }],
  ['b', { x: 300, y: 300, width: 260, height: 44 }],
  ['c', { x: 700, y: 300, width: 220, height: 56 }],
  ['d', { x: 300, y: 400, width: 200, height: 44 }],
]);

describe('drawio', () => {
  it('grafo: XML válido, um balão por passo desenhado e uma ligação por aresta', () => {
    const doc = parse(graphToDrawio(graph, boxes, 'api-pedidos', 'cancelar'));

    expect(doc.documentElement.nodeName).toBe('mxfile');
    expect(doc.querySelector('diagram')?.getAttribute('name')).toBe('cancelar');
    expect(doc.querySelectorAll('UserObject').length).toBe(4);
    const edges = [...doc.querySelectorAll('mxCell[edge="1"]')];
    expect(edges.length).toBe(3);
    expect(edges.every((e) => e.getAttribute('style')!.includes('edgeStyle=orthogonalEdgeStyle'))).toBeTrue();
    expect(edges.map((e) => e.getAttribute('source'))).toEqual(['n0', 'n1', 'n1']);
  });

  it('grafo: o rótulo leva a referência e o texto sai escapado duas vezes', () => {
    const xml = graphToDrawio(graph, boxes, 'api-pedidos', 'cancelar');
    const doc = parse(xml);
    const decision = doc.querySelectorAll('UserObject')[1];

    // No arquivo, o "<" do texto vira "&amp;lt;"; lido como atributo, volta a ser HTML com "&lt;".
    expect(xml).toContain('prazo &amp;lt; &quot;hoje&quot; &amp;amp;&amp;amp; ok');
    expect(decision.getAttribute('label')).toContain('if (prazo &lt; "hoje" &amp;&amp; ok)');
    expect(decision.getAttribute('label')).toContain('api-pedidos · CancelamentoService.cs:30');
    expect(doc.querySelectorAll('UserObject')[0].getAttribute('tooltip')).toBe('PedidosController.Cancelar\nsrc/Api/PedidosController.cs:12');
  });

  it('grafo: o passo de outro sistema cita o repositório dele, e cada sistema ganha moldura', () => {
    const doc = parse(graphToDrawio(graph, boxes, 'api-pedidos', 'cancelar'));

    expect(doc.querySelectorAll('UserObject')[2].getAttribute('label')).toContain('api-pagamentos · EstornosController.cs:8');
    const frames = [...doc.querySelectorAll('mxCell[id^="f"]')];
    expect(frames.map((f) => f.getAttribute('value'))).toEqual(['api-pedidos', 'api-pagamentos']);
    expect(frames.every((f) => f.getAttribute('style')!.includes('dashed=1'))).toBeTrue();
  });

  it('grafo: inferida tracejada, confirmada grossa, HTTP com verbo e rota, ramo em texto simples', () => {
    const doc = parse(graphToDrawio(graph, boxes, 'api-pedidos', 'cancelar'));
    const [, http, other] = [...doc.querySelectorAll('mxCell[edge="1"]')];

    expect(http.getAttribute('style')).toContain('dashed=1');
    expect(http.getAttribute('value')).toBe('POST api/estornos');
    expect(other.getAttribute('style')).toContain('strokeWidth=2');
    expect(other.getAttribute('value')).not.toBe('else');
  });

  it('grafo: estilo por tipo e desenho levado para perto da origem', () => {
    const doc = parse(graphToDrawio(graph, boxes, 'api-pedidos', 'cancelar'));
    const cells = [...doc.querySelectorAll('UserObject > mxCell')];

    expect(cells[0].getAttribute('style')).toBe(DRAWIO_STYLE.start);
    expect(cells[3].getAttribute('style')).toBe(DRAWIO_STYLE.rejection);
    const xs = [...doc.querySelectorAll('mxGeometry[as="geometry"]')].map((g) => Number(g.getAttribute('x'))).filter((x) => !Number.isNaN(x) && x !== 0);
    expect(Math.min(...xs)).toBe(40);
  });

  it('grafo: passo fora do desenho fica de fora, e a ligação dele também', () => {
    const doc = parse(graphToDrawio(graph, new Map([...boxes].filter(([id]) => id !== 'd')), 'api-pedidos', 'x'));

    expect(doc.querySelectorAll('UserObject').length).toBe(3);
    expect(doc.querySelectorAll('mxCell[edge="1"]').length).toBe(2);
  });

  it('grafo: a ligação sai e entra pelas portas da tela, conforme a direção', () => {
    const across = parse(graphToDrawio(graph, boxes, 'api-pedidos', 'x', { direction: LayoutDirection.Horizontal }));
    const down = parse(graphToDrawio(graph, boxes, 'api-pedidos', 'x', { direction: LayoutDirection.Timeline }));
    const styles = (doc: Document) => [...doc.querySelectorAll('mxCell[edge="1"]')].map((e) => e.getAttribute('style')!);

    expect(styles(across).every((s) => /exitX=[\d.]+;exitY=[\d.]+;.*entryX=[\d.]+;entryY=[\d.]+;/.test(s))).toBeTrue();
    expect(styles(across)).not.toEqual(styles(down));
  });

  it('grafo: o passo repetido ganha o próprio balão, tracejado e com o ↻', () => {
    const repeats = new Map([['again:a', { id: 'again:a', of: 'a' }]]);
    const withRepeat = new Map([...boxes, ['again:a', { x: 300, y: 500, width: 220, height: 56 }]]);
    const doc = parse(graphToDrawio(graph, withRepeat, 'api-pedidos', 'x', { repeats }));
    const last = [...doc.querySelectorAll('UserObject')].at(-1)!;

    expect(doc.querySelectorAll('UserObject').length).toBe(5);
    expect(last.getAttribute('label')).toContain('↻ PedidosController.Cancelar');
    expect(last.querySelector('mxCell')!.getAttribute('style')).toBe(DRAWIO_STYLE.exit);
  });

  it('mapa: faixas viram molduras, ramo do desfecho no rótulo e chamada tracejada', () => {
    const map: FlowMap = {
      lanes: [
        { id: 'l0', kind: MapLaneKind.Code, system: null },
        { id: 'l1', kind: MapLaneKind.Code, system: 'api-pagamentos' },
      ],
      blocks: [
        { id: 'b0', kind: MapBlockKind.Entry, lane: 'l0', order: 0, title: 'Cancelar', nodeId: 'a', nodeIds: ['a'], location: { file: 'src/Api/PedidosController.cs', line: 12 } },
        { id: 'b1', kind: MapBlockKind.Decision, lane: 'l0', order: 1, title: 'Prazo?', nodeId: 'b', nodeIds: ['b'] },
        { id: 'b2', kind: MapBlockKind.Step, lane: 'l1', order: 2, title: 'Estorno', nodeId: 'c', nodeIds: ['c'] },
      ],
      links: [
        { from: 'b0', to: 'b1', kind: MapLinkKind.Sequence },
        { from: 'b1', to: 'b2', kind: MapLinkKind.Call, http: { method: 'POST', route: 'api/estornos' } },
      ],
      paths: [],
      pathsTruncated: false,
    };
    const doc = parse(mapToDrawio(mapGeometry(map, false), (b) => b.title, (l) => l.system ?? 'api-pedidos', 'api-pedidos', 'mapa'));

    const frames = [...doc.querySelectorAll('mxCell[id^="f"]')];
    expect(frames.map((f) => f.getAttribute('value'))).toEqual(['api-pedidos', 'api-pagamentos']);
    expect(doc.querySelectorAll('UserObject')[0].getAttribute('label')).toContain('api-pedidos · PedidosController.cs:12');
    const [, call] = [...doc.querySelectorAll('mxCell[edge="1"]')];
    expect(call.getAttribute('value')).toBe('POST api/estornos');
    expect(call.getAttribute('style')).toContain('dashed=1');
  });

  it('grafo e mapa: o balão leva o link do repositório dele, e a faixa de fora do código fica sem link (Fase 56)', () => {
    const linkOf = (repository: string, location: { file: string; line: number } | null | undefined) =>
      location ? `https://host/${repository}/${location.file}#L${location.line}` : null;
    const doc = parse(graphToDrawio(graph, boxes, 'api-pedidos', 'x', { linkOf }));
    const objects = [...doc.querySelectorAll('UserObject')];

    expect(objects[0].getAttribute('link')).toBe('https://host/api-pedidos/src/Api/PedidosController.cs#L12');
    expect(objects[2].getAttribute('link')).toBe('https://host/api-pagamentos/src/EstornosController.cs#L8');
    expect(objects[3].hasAttribute('link')).toBeFalse();

    const map: FlowMap = {
      lanes: [
        { id: 'l0', kind: MapLaneKind.Code, system: null },
        { id: 'out', kind: MapLaneKind.Outside, system: null },
      ],
      blocks: [
        { id: 'b0', kind: MapBlockKind.Entry, lane: 'l0', order: 0, title: 'A', nodeId: 'a', nodeIds: ['a'], location: { file: 'a.cs', line: 1 } },
        { id: 'b1', kind: MapBlockKind.Outside, lane: 'out', order: 1, title: 'Gateway', nodeId: 'g', nodeIds: ['g'], location: { file: 'g.cs', line: 2 } },
      ],
      links: [{ from: 'b0', to: 'b1', kind: MapLinkKind.Call }],
      paths: [],
      pathsTruncated: false,
    };
    const mapDoc = parse(mapToDrawio(mapGeometry(map, false), (b) => b.title, (l) => l.id, 'api-pedidos', 'm', linkOf));
    const blocks = [...mapDoc.querySelectorAll('UserObject')];

    expect(blocks[0].getAttribute('link')).toBe('https://host/api-pedidos/a.cs#L1');
    expect(blocks[1].hasAttribute('link')).toBeFalse();
  });

  it('pontos de dobra e link do balão vão para o arquivo', () => {
    const xml = toDrawio({
      name: 'p',
      frames: [],
      vertices: [
        { text: 'A', style: DRAWIO_STYLE.step, x: 40, y: 40, width: 100, height: 40, link: 'https://github.com/o/r/blob/abc/a.cs#L1' },
        { text: 'B', style: DRAWIO_STYLE.end, x: 40, y: 200, width: 100, height: 40 },
      ],
      edges: [{ source: 0, target: 1, style: DRAWIO_STYLE.edge, points: [{ x: 200, y: 60 }, { x: 200, y: 220 }] }],
    });
    const doc = parse(xml);

    expect(doc.querySelector('UserObject')?.getAttribute('link')).toBe('https://github.com/o/r/blob/abc/a.cs#L1');
    expect(doc.querySelectorAll('Array[as="points"] > mxPoint').length).toBe(2);
  });

  it('negócio: losango na decisão, link pelo repositório, ramo no rótulo e inferida tracejada (Fase 57)', () => {
    const flow: BusinessFlow = {
      surveyId: 's',
      schemaVersion: 1,
      collapsedDecisions: 0,
      steps: [
        { id: 'p0', nodeId: 'a', kind: BusinessKind.Start, label: 'Cliente cancela o pedido', labelOrigin: LabelOrigin.Rule, labelState: LabelState.Confirmed, ref: { repository: 'api-pedidos', file: 'src/Api/PedidosController.cs', line: 12, commitHash: null, branch: null }, sourceNodeIds: ['a'], inferred: false, codeChanged: false },
        { id: 'p1', nodeId: 'b', kind: BusinessKind.Decision, label: 'Está dentro do prazo?', labelOrigin: LabelOrigin.Rule, labelState: LabelState.Proposed, ref: null, sourceNodeIds: ['b'], inferred: false, codeChanged: false },
        { id: 'p2', nodeId: 'c', kind: BusinessKind.CrossSystem, label: 'Pede o estorno', labelOrigin: LabelOrigin.Rule, labelState: LabelState.Confirmed, ref: { repository: 'api-pagamentos', file: 'src/Estornos.cs', line: 8, commitHash: null, branch: null }, sourceNodeIds: ['c'], inferred: true, codeChanged: false },
      ],
      edges: [
        { from: 'p0', to: 'p1', label: null, inferred: false },
        { from: 'p1', to: 'p2', label: 'then', inferred: true },
      ],
    };
    const linkOf = (repository: string, location: { file: string; line: number } | null | undefined) =>
      location ? `https://host/${repository}/${location.file}#L${location.line}` : null;
    const doc = parse(businessToDrawio(flow, businessLayout(flow), 'negocio', linkOf));
    const objects = [...doc.querySelectorAll('UserObject')];

    expect(objects.length).toBe(3);
    expect(objects[1].querySelector('mxCell')!.getAttribute('style')).toContain('rhombus');
    expect(objects[2].querySelector('mxCell')!.getAttribute('style')).toContain('dashed=1');
    expect(objects[0].getAttribute('label')).toContain('api-pedidos · PedidosController.cs:12');
    expect(objects[0].getAttribute('link')).toBe('https://host/api-pedidos/src/Api/PedidosController.cs#L12');
    expect(objects[1].hasAttribute('link')).toBeFalse();
    const [plain, branch] = [...doc.querySelectorAll('mxCell[edge="1"]')];
    expect(plain.getAttribute('style')).toContain('edgeStyle=orthogonalEdgeStyle');
    expect(plain.getAttribute('style')).not.toContain('dashed=1');
    // O rótulo da API vem em código (then, else) e sai em texto simples, como no grafo.
    expect(branch.getAttribute('value')).toBe('Se sim');
    expect(branch.getAttribute('style')).toContain('dashed=1');
  });

  it('referência: só o nome do arquivo, com ou sem repositório', () => {
    expect(codeReference('loja-web', { file: 'src/app/pedidos/detalhe-pedido.component.ts', line: 42 })).toBe('loja-web · detalhe-pedido.component.ts:42');
    expect(codeReference(null, { file: 'a.cs', line: 1 })).toBe('a.cs:1');
    expect(codeReference('x', null)).toBeNull();
  });
});
