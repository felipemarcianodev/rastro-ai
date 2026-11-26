import { BusinessFlow, BusinessKind, BusinessStep, LabelOrigin, LabelState } from '../models';
import { businessLayout, edgeKey } from './business-layout';
import {
  BUSINESS_COLORS,
  BUSINESS_MERMAID_CLASS,
  BusinessExportData,
  ExportLane,
  boardNoteLines,
  boardToDrawio,
  businessToMermaid,
  businessToSvg,
  readBusinessColors,
  wrapText,
} from './business-export';
import { BUSINESS_STYLE, businessToDrawio } from './drawio';

// Fase 61: exportar a visão de negócio e o quadro (draw.io, Mermaid, SVG).

function parse(xml: string, type: DOMParserSupportedType = 'application/xml'): Document {
  const doc = new DOMParser().parseFromString(xml, type);
  expect(doc.getElementsByTagName('parsererror').length).withContext(xml).toBe(0);
  return doc;
}

const ref = (repository: string, file: string, line: number) => ({ repository, file, line, commitHash: null, branch: null });

const step = (id: string, kind: BusinessKind, label: string, extra: Partial<BusinessStep> = {}): BusinessStep => ({
  id,
  nodeId: `n-${id}`,
  kind,
  label,
  labelOrigin: LabelOrigin.Rule,
  labelState: LabelState.Confirmed,
  ref: null,
  sourceNodeIds: [],
  inferred: false,
  codeChanged: false,
  ...extra,
});

function flowOf(surveyId: string, prefix: string, repository: string): BusinessFlow {
  return {
    surveyId,
    schemaVersion: 1,
    collapsedDecisions: 0,
    steps: [
      step(`${prefix}0`, BusinessKind.Start, 'Cliente <cancela> o "pedido"', { ref: ref(repository, 'src/Api/Pedidos.cs', 12) }),
      step(`${prefix}1`, BusinessKind.Decision, 'Está dentro do prazo?'),
      step(`${prefix}2`, BusinessKind.Rejection, 'Recusa: fora do prazo', { ref: ref(repository, 'src/Api/Prazo.cs', 30) }),
      step(`${prefix}3`, BusinessKind.End, 'Fim', { ref: ref(repository, 'src/Api/Pedidos.cs', 40) }),
    ],
    edges: [
      { from: `${prefix}0`, to: `${prefix}1`, label: null, inferred: false },
      { from: `${prefix}1`, to: `${prefix}2`, label: 'else', inferred: true },
      { from: `${prefix}1`, to: `${prefix}3`, label: 'then', inferred: false },
    ],
  };
}

function dataOf(surveyId: string, prefix: string, repository: string, bendsFor?: [string, string]): BusinessExportData {
  const flow = flowOf(surveyId, prefix, repository);
  const geometry = businessLayout(flow);
  // Uma posição arrastada: o passo de recusa vai para longe do automático.
  const boxes = new Map(geometry.boxes);
  boxes.set(`${prefix}2`, { ...boxes.get(`${prefix}2`)!, x: 900, y: 500 });
  const bends = new Map<string, { x: number; y: number }[]>();
  if (bendsFor) bends.set(edgeKey(bendsFor[0], bendsFor[1]), [{ x: 700, y: 300 }, { x: 700, y: 480 }]);
  return { surveyId, flow, geometry: { ...geometry, boxes }, bends };
}

const linkFor = (repository: string, location: { file: string; line: number } | null | undefined) => (location ? `https://host/${repository}/${location.file}#L${location.line}` : null);

function lanes(): ExportLane[] {
  return [
    { title: 'Pedidos', data: dataOf('s1', 'a', 'api-pedidos', ['a1', 'a2']), linkOf: linkFor },
    { title: 'Pagamentos', data: dataOf('s2', 'b', 'api-pagamentos'), linkOf: linkFor },
  ];
}

const texts = { glossary: 'Glossário', legend: 'Legenda', kind: (kind: BusinessKind) => `tipo ${kind}` };

describe('business-export: draw.io', () => {
  it('negócio: o ponto de dobra salvo e a posição arrastada vão para o arquivo', () => {
    const data = dataOf('s1', 'a', 'api-pedidos', ['a1', 'a2']);
    const doc = parse(businessToDrawio(data.flow, data.geometry, 'neg', linkFor, data.bends));

    const points = [...doc.querySelectorAll('Array[as="points"] > mxPoint')];
    expect(points.length).toBe(2);
    // Um único arquivo: a recusa arrastada (900) fica à direita dos demais passos (todos abaixo de 400).
    const xs = [...doc.querySelectorAll('UserObject > mxCell > mxGeometry')].map((g) => Number(g.getAttribute('x')));
    expect(Math.max(...xs) - Math.min(...xs)).toBeGreaterThan(500);
    const edges = [...doc.querySelectorAll('mxCell[edge="1"]')];
    expect(edges[0].getAttribute('style')).toContain('exitX=0.5;exitY=1');
    expect(edges[0].getAttribute('style')).toContain('entryX=0.5;entryY=0');
  });

  it('quadro: uma moldura tracejada por faixa com o título, sem sobrepor faixas', () => {
    const doc = parse(boardToDrawio(lanes(), 'Compra', null));
    const frames = [...doc.querySelectorAll('mxCell[id^="f"]')];

    expect(frames.map((f) => f.getAttribute('value'))).toEqual(['Pedidos', 'Pagamentos']);
    expect(frames.every((f) => f.getAttribute('style')!.includes('dashed=1'))).toBeTrue();
    const box = (f: Element) => {
      const g = f.querySelector('mxGeometry')!;
      return { x: Number(g.getAttribute('x')), width: Number(g.getAttribute('width')) };
    };
    expect(box(frames[0]).x + box(frames[0]).width).toBeLessThan(box(frames[1]).x);
  });

  it('quadro: os passos de cada faixa ficam dentro da moldura dela, com as ligações de cada faixa', () => {
    const doc = parse(boardToDrawio(lanes(), 'Compra', null));
    const frames = [...doc.querySelectorAll('mxCell[id^="f"] > mxGeometry')].map((g) => ({
      x: Number(g.getAttribute('x')),
      right: Number(g.getAttribute('x')) + Number(g.getAttribute('width')),
    }));
    const objects = [...doc.querySelectorAll('UserObject')];

    expect(objects.length).toBe(8);
    objects.forEach((object, i) => {
      const geometry = object.querySelector('mxGeometry')!;
      const x = Number(geometry.getAttribute('x'));
      const lane = frames[i < 4 ? 0 : 1];
      expect(x).withContext(`balão ${i}`).toBeGreaterThanOrEqual(lane.x);
      expect(x + Number(geometry.getAttribute('width'))).withContext(`balão ${i}`).toBeLessThanOrEqual(lane.right);
    });
    const edges = [...doc.querySelectorAll('mxCell[edge="1"]')];
    expect(edges.length).toBe(6);
    // As ligações da segunda faixa ligam os balões dela (índices 4 a 7).
    expect(edges.slice(3).every((e) => Number(e.getAttribute('source')!.slice(1)) >= 4 && Number(e.getAttribute('target')!.slice(1)) >= 4)).toBeTrue();
    // O ponto de dobra da primeira faixa foi junto com ela.
    expect(doc.querySelectorAll('Array[as="points"] > mxPoint').length).toBe(2);
  });

  it('quadro: o link vem do levantamento de cada faixa, e o passo sem código fica sem link', () => {
    const doc = parse(boardToDrawio(lanes(), 'Compra', null));
    const objects = [...doc.querySelectorAll('UserObject')];

    expect(objects[0].getAttribute('link')).toBe('https://host/api-pedidos/src/Api/Pedidos.cs#L12');
    expect(objects[4].getAttribute('link')).toBe('https://host/api-pagamentos/src/Api/Pedidos.cs#L12');
    expect(objects[1].hasAttribute('link')).toBeFalse();
  });

  it('quadro: o losango tem espaço para o texto (240×110 no mínimo)', () => {
    const doc = parse(boardToDrawio(lanes(), 'Compra', null));
    const decision = [...doc.querySelectorAll('UserObject')].find((o) => o.querySelector('mxCell')!.getAttribute('style')!.includes('rhombus'))!;
    const geometry = decision.querySelector('mxGeometry')!;

    expect(Number(geometry.getAttribute('width'))).toBeGreaterThanOrEqual(240);
    expect(Number(geometry.getAttribute('height'))).toBeGreaterThanOrEqual(110);
  });

  it('quadro: a nota (shape=note) leva o glossário e a legenda só dos tipos presentes', () => {
    const note = boardNoteLines('Pedido: compra feita pelo cliente\nlinha sem dois-pontos', lanes().map((l) => l.data.flow), texts);
    const doc = parse(boardToDrawio(lanes(), 'Compra', note));
    const notes = [...doc.querySelectorAll('UserObject')].filter((o) => o.querySelector('mxCell')!.getAttribute('style')!.includes('shape=note'));

    expect(notes.length).toBe(1);
    const label = notes[0].getAttribute('label')!;
    expect(label).toContain('Glossário');
    expect(label).toContain('Pedido: compra feita pelo cliente');
    expect(label).not.toContain('linha sem dois-pontos');
    expect(label).toContain(`tipo ${BusinessKind.Rejection}`);
    expect(label).not.toContain(`tipo ${BusinessKind.Notification}`);
    // A nota fica à direita da última moldura.
    const frames = [...doc.querySelectorAll('mxCell[id^="f"] > mxGeometry')];
    const last = frames[frames.length - 1];
    expect(Number(notes[0].querySelector('mxGeometry')!.getAttribute('x'))).toBeGreaterThan(Number(last.getAttribute('x')) + Number(last.getAttribute('width')));
  });

  it('quadro: sem glossário nem tipo, não há nota; faixa sem passo não entra', () => {
    expect(boardNoteLines(null, [], texts)).toBeNull();
    const empty: ExportLane = { title: 'Vazia', data: { surveyId: 's9', flow: { ...flowOf('s9', 'z', 'x'), steps: [], edges: [] }, geometry: { boxes: new Map(), width: 0, height: 0 }, bends: new Map() } };
    const doc = parse(boardToDrawio([empty, ...lanes()], 'Compra', null));

    expect([...doc.querySelectorAll('mxCell[id^="f"]')].length).toBe(2);
  });

  it('as cores do Mermaid são as do draw.io', () => {
    for (const kind of Object.keys(BUSINESS_COLORS).map(Number) as BusinessKind[]) {
      expect(BUSINESS_STYLE[kind]).withContext(`${kind}`).toContain(`fillColor=${BUSINESS_COLORS[kind].fill}`);
      expect(BUSINESS_STYLE[kind]).withContext(`${kind}`).toContain(`strokeColor=${BUSINESS_COLORS[kind].stroke}`);
    }
  });
});

describe('business-export: Mermaid', () => {
  it('quadro: flowchart TD com um subgraph por faixa, forma e classDef por tipo', () => {
    const text = businessToMermaid(lanes().map((l) => ({ title: l.title, flow: l.data.flow })), true);
    const lines = text.split('\n');

    expect(lines[0]).toBe('flowchart TD');
    expect(lines.filter((l) => l.trim().startsWith('subgraph')).length).toBe(2);
    expect(text).toContain('subgraph lane0["Pedidos"]');
    expect(text).toContain('subgraph lane1["Pagamentos"]');
    expect(lines.filter((l) => l.trim() === 'end').length).toBe(2);
    // Losango na decisão, pílula no início e no fim.
    expect(text).toContain('l0s1{"Está dentro do prazo?"}');
    expect(text).toContain('l0s0(["Cliente #lt;cancela#gt; o #quot;pedido#quot;"])');
    expect(text).toContain('l0s3(["Fim"])');
    expect(text).toContain('l1s2["Recusa: fora do prazo"]');
    for (const kind of [BusinessKind.Start, BusinessKind.Decision, BusinessKind.Rejection, BusinessKind.End]) {
      expect(text).toContain(`classDef ${BUSINESS_MERMAID_CLASS[kind]} fill:${BUSINESS_COLORS[kind].fill},stroke:${BUSINESS_COLORS[kind].stroke}`);
    }
    // O tipo que não existe no quadro não ganha classe.
    expect(text).not.toContain(`classDef ${BUSINESS_MERMAID_CLASS[BusinessKind.Notification]}`);
    expect(text).toContain(`class l0s0,l1s0 ${BUSINESS_MERMAID_CLASS[BusinessKind.Start]}`);
  });

  it('o rótulo do ramo vai na ligação, em texto simples, e a inferida é tracejada', () => {
    const text = businessToMermaid([{ title: 'A', flow: flowOf('s', 'a', 'r') }], true);

    expect(text).toContain('l0s1 -.->|"Se não"| l0s2');
    expect(text).toContain('l0s1 -->|"Se sim"| l0s3');
    expect(text).toContain('l0s0 --> l0s1');
  });

  it('o título da faixa também sai escapado', () => {
    const text = businessToMermaid([{ title: 'Faixa "A" <b>', flow: flowOf('s', 'a', 'r') }], true);

    expect(text).toContain('subgraph lane0["Faixa #quot;A#quot; #lt;b#gt;"]');
  });

  it('sem faixas do quadro (visão solta): sem subgraph; sem passo, texto vazio', () => {
    const solto = businessToMermaid([{ title: '', flow: flowOf('s', 'a', 'r') }], false);

    expect(solto).not.toContain('subgraph');
    expect(solto).toContain('classDef');
    expect(businessToMermaid([{ title: '', flow: { ...flowOf('s', 'a', 'r'), steps: [], edges: [] } }], false)).toBe('');
  });

  it('a classe do tipo composto não leva hífen', () => {
    expect(BUSINESS_MERMAID_CLASS[BusinessKind.CrossSystem]).toBe('bizcrosssystem');
  });
});

describe('business-export: SVG', () => {
  const colors = readBusinessColors(() => '');

  it('quadro: SVG válido com as molduras, um balão por passo, o texto escapado e a referência', () => {
    const image = businessToSvg(lanes(), colors, true, null)!;
    const doc = parse(image.svg, 'image/svg+xml');

    expect(doc.documentElement.nodeName).toBe('svg');
    expect(image.width).toBeGreaterThan(0);
    expect(doc.querySelectorAll('g[data-step]').length).toBe(8);
    expect(doc.querySelectorAll('rect[stroke-dasharray="6 4"]').length).toBe(2);
    expect(image.svg).toContain('>Pedidos<');
    expect(image.svg).toContain('Cliente &lt;cancela&gt; o &quot;pedido&quot;');
    expect(image.svg).toContain('api-pedidos · Pedidos.cs:12');
    expect(doc.querySelectorAll('polygon').length).toBe(2);
  });

  it('usa as cores por tipo da paleta e as famílias de fonte do tema pelo nome', () => {
    const custom = readBusinessColors((name) => (name === '--biz-rejection-fill' ? '#123456' : ''));
    const image = businessToSvg(lanes(), custom, true, null)!;

    expect(image.svg).toContain('fill="#123456"');
    expect(image.svg).toContain(`fill="${BUSINESS_COLORS[BusinessKind.Start].fill}"`);
    expect(image.svg).toContain('Instrument Sans');
    expect(image.svg).toContain('JetBrains Mono');
  });

  it('desenha o ponto de dobra e o rótulo do ramo; a inferida é tracejada', () => {
    const image = businessToSvg([lanes()[0]], colors, false, null)!;
    const doc = parse(image.svg, 'image/svg+xml');
    const paths = [...doc.querySelectorAll('path[marker-end]')];

    expect(paths.length).toBe(3);
    expect(paths.some((p) => p.getAttribute('d')!.includes('L'))).toBeTrue();
    expect(paths.filter((p) => p.hasAttribute('stroke-dasharray')).length).toBe(1);
    expect(image.svg).toContain('>Se sim<');
    expect(doc.querySelectorAll('rect[stroke-dasharray="6 4"]').length).toBe(0);
  });

  it('a nota de glossário e legenda entra no quadro, com o cabeçalho em negrito', () => {
    const note = boardNoteLines('Pedido: compra do cliente', lanes().map((l) => l.data.flow), texts);
    const image = businessToSvg(lanes(), colors, true, note)!;

    expect(image.svg).toContain('font-weight="700"');
    expect(image.svg).toContain('Pedido: compra do cliente');
    expect(image.width).toBeGreaterThan(businessToSvg(lanes(), colors, true, null)!.width);
  });

  it('sem passo desenhado, nulo', () => {
    expect(businessToSvg([], colors, true)).toBeNull();
  });

  it('wrapText quebra por palavra, parte palavra longa e corta com reticências', () => {
    expect(wrapText('um dois três quatro', 8, 5)).toEqual(['um dois', 'três', 'quatro']);
    expect(wrapText('abcdefghij', 4, 5)).toEqual(['abcd', 'efgh', 'ij']);
    const cut = wrapText('um dois três quatro cinco seis', 8, 2);
    expect(cut.length).toBe(2);
    expect(cut[1].endsWith('…')).toBeTrue();
  });

});
