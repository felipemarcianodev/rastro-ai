import { BusinessFlow, BusinessKind, EdgeConfidence, FlowGraph, FlowNode, MapBlock, MapBlockKind, MapLane, MapLaneKind, MapLinkKind, NodeKind, SourceLocation } from '../models';
import { EdgeShape, PORT, edgeRoutes, httpLabel, mermaidSystems } from './export';
import { BusinessGeometry, edgeKey } from './business-layout';
import { MAP_BOX, MapGeometry, linkText } from './flow-map';
import { LayoutDirection, NodeBox } from './layout';
import { plainBranch } from './plain';
import { TimelineRepeat } from './timeline-layout';
import { HostLinkOf } from './link-template';

// Exportar para o draw.io (Fase 54, ADR-010): o desenho que está na tela vira um arquivo .drawio
// (mxfile), com as posições da tela, inclusive as arrastadas. As ligações levam o estilo ortogonal
// do draw.io, que as roteia sozinho; as cores são as padrão do draw.io, para o arquivo sair igual ao
// que se desenha à mão. Puro e determinístico: o mesmo desenho dá sempre o mesmo arquivo.

export const DRAWIO_MIME = 'application/vnd.jgraph.mxfile';

// Um balão: o texto (simples, sem HTML) e, abaixo, a referência ao código.
export interface DrawioVertex {
  text: string;
  reference?: string | null;
  tooltip?: string | null;
  link?: string | null;
  // Id do Rastro (nó do grafo, bloco do mapa, passo da visão de negócio): o draw.io dentro do Rastro (ADR-011)
  // devolve o balão escolhido e as posições por ele.
  rastroId?: string | null;
  // Nó do grafo por trás do balão (o mesmo do id no grafo; o bloco e o passo apontam o nó que lhes dá o nome):
  // o clique no draw.io abre o painel desse nó.
  rastroNode?: string | null;
  style: string;
  x: number;
  y: number;
  width: number;
  height: number;
}

// Moldura de um sistema ou faixa: retângulo tracejado com o título no alto, atrás dos balões.
export interface DrawioFrame {
  title: string;
  x: number;
  y: number;
  width: number;
  height: number;
}

// Ligação entre dois balões, pelos índices na lista de balões.
export interface DrawioEdge {
  source: number;
  target: number;
  label?: string | null;
  style: string;
  points?: readonly { x: number; y: number }[];
}

export interface DrawioPage {
  name: string;
  frames: readonly DrawioFrame[];
  vertices: readonly DrawioVertex[];
  edges: readonly DrawioEdge[];
}

const BASE = 'whiteSpace=wrap;html=1;fontSize=12;';
const ROUNDED = `rounded=1;${BASE}`;
const PILL = `rounded=1;arcSize=50;${BASE}`;

// Estilos do draw.io por papel (os mesmos da seção 14.1 da proposta).
export const DRAWIO_STYLE = {
  start: `${PILL}fillColor=#d5e8d4;strokeColor=#82b366;fontStyle=1;`,
  end: `${PILL}fillColor=#dae8fc;strokeColor=#6c8ebf;`,
  step: `${ROUNDED}fillColor=#ffffff;strokeColor=#666666;`,
  decision: `${ROUNDED}fillColor=#fff2cc;strokeColor=#d6b656;`,
  loop: `${ROUNDED}fillColor=#e1d5e7;strokeColor=#9673a6;`,
  notification: `${ROUNDED}fillColor=#ffe6cc;strokeColor=#d79b00;fontStyle=1;`,
  rejection: `${ROUNDED}fillColor=#f8cecc;strokeColor=#b85450;`,
  outside: `${PILL}fillColor=#eeeeee;strokeColor=#999999;`,
  unresolved: `${ROUNDED}fillColor=#ffe6cc;strokeColor=#d79b00;dashed=1;`,
  exit: `${ROUNDED}fillColor=#ffffff;strokeColor=#666666;dashed=1;`,
  edge: 'edgeStyle=orthogonalEdgeStyle;rounded=1;html=1;endArrow=block;endFill=1;strokeColor=#555555;fontSize=11;fontStyle=1;labelBackgroundColor=#ffffff;',
  frame: 'rounded=1;arcSize=2;whiteSpace=wrap;html=1;dashed=1;fillColor=none;strokeColor=#999999;verticalAlign=top;align=left;spacingLeft=8;spacingTop=4;fontSize=12;fontStyle=1;fontColor=#555555;',
} as const;

const NODE_STYLE: Record<NodeKind, string> = {
  [NodeKind.Entry]: DRAWIO_STYLE.start,
  [NodeKind.Method]: DRAWIO_STYLE.step,
  [NodeKind.Decision]: DRAWIO_STYLE.decision,
  [NodeKind.Loop]: DRAWIO_STYLE.loop,
  [NodeKind.Try]: DRAWIO_STYLE.loop,
  [NodeKind.Return]: DRAWIO_STYLE.exit,
  [NodeKind.Throw]: DRAWIO_STYLE.rejection,
  [NodeKind.External]: DRAWIO_STYLE.outside,
  [NodeKind.Unresolved]: DRAWIO_STYLE.unresolved,
  [NodeKind.Jump]: DRAWIO_STYLE.exit,
};

const BLOCK_STYLE: Record<MapBlockKind, string> = {
  [MapBlockKind.Entry]: DRAWIO_STYLE.start,
  [MapBlockKind.Step]: DRAWIO_STYLE.step,
  [MapBlockKind.Decision]: DRAWIO_STYLE.decision,
  [MapBlockKind.Outside]: DRAWIO_STYLE.outside,
};

// Variações da ligação: inferida tracejada (o usuário sabe em que confiar), confirmada mais grossa.
const CONFIDENCE_STYLE: Record<EdgeConfidence, string> = {
  [EdgeConfidence.Extracted]: '',
  [EdgeConfidence.Inferred]: 'dashed=1;',
  [EdgeConfidence.Confirmed]: 'strokeWidth=2;',
};

const CALL_STYLE = 'dashed=1;';

// Margem em volta do desenho e folga da moldura em volta dos balões do sistema.
const MARGIN = 40;
export const FRAME_PAD = 20;
export const FRAME_HEADER = 28;

// "repositório · arquivo:linha", só com o nome do arquivo (o caminho completo vai na dica).
export function codeReference(repository: string | null | undefined, location: SourceLocation | null | undefined): string | null {
  if (!location) return null;
  const file = location.file.slice(location.file.lastIndexOf('/') + 1);
  const where = `${file}:${location.line}`;
  return repository ? `${repository} · ${where}` : where;
}

function tooltipOf(symbol: string | null | undefined, location: SourceLocation | null | undefined): string | null {
  const where = location ? `${location.file}:${location.line}` : null;
  return [symbol, where].filter((part): part is string => !!part).join('\n') || null;
}

// O que o desenho da tela acrescenta ao grafo: o passo repetido e a direção (as portas da ligação).
export interface DrawioGraphExtras {
  repeats?: ReadonlyMap<string, TimelineRepeat>;
  direction?: LayoutDirection;
  // Link de cada balão para o código no host (Fase 56).
  linkOf?: HostLinkOf;
}

// O grafo como está na tela: uma caixa por passo desenhado (o passo repetido, ↻, tem a sua), as
// ligações pelas mesmas rotas e portas da tela e uma moldura por sistema quando o fluxo atravessa
// mais de um.
export function graphToDrawio(
  graph: FlowGraph,
  boxes: ReadonlyMap<string, NodeBox>,
  repository: string,
  name: string,
  extras: DrawioGraphExtras = {},
): string {
  const repeats = extras.repeats ?? new Map<string, TimelineRepeat>();
  const across = (extras.direction ?? LayoutDirection.Horizontal) === LayoutDirection.Horizontal;
  const byId = new Map(graph.nodes.map((node) => [node.id, node]));
  const shown = [
    ...graph.nodes.filter((node) => boxes.has(node.id)).map((node) => ({ id: node.id, node, repeat: false })),
    ...[...repeats.values()].filter((r) => boxes.has(r.id) && byId.has(r.of)).map((r) => ({ id: r.id, node: byId.get(r.of)!, repeat: true })),
  ];
  const index = new Map(shown.map((item, i) => [item.id, i]));
  const vertices = shown.map(({ id, node, repeat }): DrawioVertex => {
    const box = boxes.get(id)!;
    return {
      text: repeat ? `↻ ${node.label}` : node.label,
      rastroId: repeat ? null : node.id,
      rastroNode: node.id,
      reference: codeReference(node.system ?? repository, node.location),
      tooltip: tooltipOf(node.symbol, node.location),
      link: extras.linkOf?.(node.system ?? repository, node.location) ?? null,
      style: repeat ? DRAWIO_STYLE.exit : NODE_STYLE[node.kind],
      x: box.x,
      y: box.y,
      width: box.width,
      height: box.height,
    };
  });
  const edges = edgeRoutes(graph, repeats).flatMap((route): DrawioEdge[] => {
    const source = index.get(route.source);
    const target = index.get(route.target);
    if (source === undefined || target === undefined || source === target) return [];
    const edge = graph.edges[route.index];
    const label = httpLabel(edge) ?? (edge.label ? plainBranch(edge.label) : null);
    const ports = portStyle(route.shape, across, vertices[source], vertices[target]);
    return [{ source, target, label, style: DRAWIO_STYLE.edge + CONFIDENCE_STYLE[edge.confidence] + ports }];
  });
  const frames = (mermaidSystems(graph, repository) ?? []).flatMap((system) => {
    const frame = frameAround(system.title, system.nodes.flatMap((node: FlowNode) => (boxes.has(node.id) ? [boxes.get(node.id)!] : [])));
    return frame ? [frame] : [];
  });
  return toDrawio(shift({ name, frames, vertices, edges }));
}

// Portas de saída e de entrada, como na tela (edgePath do SVG). Para baixo: o passo seguinte desce
// pela esquerda e o galho sai pela direita. Para a direita: o passo seguinte segue pelo alto e o
// galho sai por baixo. A porta fica a PORT px da borda; o draw.io quer a fração do tamanho.
function portStyle(shape: EdgeShape, across: boolean, a: NodeBox, b: NodeBox): string {
  const fx = (box: NodeBox) => num(Math.min(1, PORT / box.width));
  const fy = (box: NodeBox) => num(Math.min(1, PORT / box.height));
  let exit: [number, number];
  let entry: [number, number];
  if (!across) {
    if (shape === 'branch') [exit, entry] = [[1, 0.5], [0, 0.5]];
    else if (shape === 'section') [exit, entry] = [[fx(a), 1], [0, 0.5]];
    else [exit, entry] = [[fx(a), 1], [fx(b), 0]];
  } else if (shape === 'branch') [exit, entry] = [[fx(a), 1], [fx(b), 0]];
  else if (shape === 'section') [exit, entry] = [[1, fy(a)], [fx(b), 0]];
  else [exit, entry] = [[1, fy(a)], [0, fy(b)]];
  return `exitX=${exit[0]};exitY=${exit[1]};exitDx=0;exitDy=0;entryX=${entry[0]};entryY=${entry[1]};entryDx=0;entryDy=0;`;
}

// O mapa por sistemas como está na tela: as faixas viram molduras com o título e as ligações de
// desfecho levam o texto do ramo.
export function mapToDrawio(
  geometry: MapGeometry,
  titleOf: (block: MapBlock) => string,
  laneTitleOf: (lane: MapLane) => string,
  repository: string,
  name: string,
  linkOf?: HostLinkOf,
): string {
  const { width, height } = MAP_BOX;
  const index = new Map(geometry.blocks.map((placed, i) => [placed.block.id, i]));
  // A faixa de fora do código não tem repositório.
  const systemOf = new Map(geometry.lanes.map((placed) => [placed.lane.id, placed.lane.kind === MapLaneKind.Outside ? null : (placed.lane.system ?? repository)]));
  // Repositório da faixa: o da faixa, o do levantamento quando a faixa não está na lista, e nenhum na de fora.
  const laneRepository = (lane: string): string | null => (systemOf.has(lane) ? (systemOf.get(lane) ?? null) : repository);
  const vertices = geometry.blocks.map(
    (placed): DrawioVertex => ({
      text: titleOf(placed.block),
      rastroId: placed.block.id,
      rastroNode: placed.block.nodeId,
      reference: codeReference(laneRepository(placed.block.lane), placed.block.location),
      tooltip: tooltipOf(placed.block.symbol, placed.block.location),
      link: laneRepository(placed.block.lane) ? (linkOf?.(laneRepository(placed.block.lane)!, placed.block.location) ?? null) : null,
      style: BLOCK_STYLE[placed.block.kind],
      x: placed.x,
      y: placed.y,
      width,
      height,
    }),
  );
  const edges = geometry.links.flatMap((placed): DrawioEdge[] => {
    const source = index.get(placed.link.from);
    const target = index.get(placed.link.to);
    if (source === undefined || target === undefined || source === target) return [];
    const style = DRAWIO_STYLE.edge + (placed.link.kind === MapLinkKind.Call ? CALL_STYLE : '');
    return [{ source, target, label: linkText(placed.link), style }];
  });
  const frames = geometry.lanes.flatMap((placed) => {
    const inLane = geometry.blocks.filter((b) => b.block.lane === placed.lane.id).map((b) => ({ x: b.x, y: b.y, width, height }));
    const frame = frameAround(laneTitleOf(placed.lane), inLane);
    return frame ? [frame] : [];
  });
  return toDrawio(shift({ name, frames, vertices, edges }));
}

// Estilos da visão de negócio (seção 14.1 da proposta): a decisão é um losango e a chamada entre
// sistemas é tracejada.
export const BUSINESS_STYLE: Record<BusinessKind, string> = {
  [BusinessKind.Start]: DRAWIO_STYLE.start,
  [BusinessKind.Decision]: 'rhombus;whiteSpace=wrap;html=1;fillColor=#fff2cc;strokeColor=#d6b656;fontSize=11;',
  [BusinessKind.Notification]: DRAWIO_STYLE.notification,
  [BusinessKind.Rejection]: DRAWIO_STYLE.rejection,
  [BusinessKind.CrossSystem]: 'rounded=1;whiteSpace=wrap;html=1;fillColor=#eeeeee;strokeColor=#999999;dashed=1;fontSize=10;align=left;spacingLeft=6;',
  [BusinessKind.Step]: DRAWIO_STYLE.step,
  [BusinessKind.End]: DRAWIO_STYLE.end,
};

// Pontos de dobra salvos da tela, por ligação (de, para): o draw.io os leva em <Array as="points">.
export type BusinessBends = ReadonlyMap<string, readonly { x: number; y: number }[]>;

// Na tela a ligação sai do meio da borda de baixo e chega no meio da borda de cima.
const BUSINESS_PORTS = 'exitX=0.5;exitY=1;exitDx=0;exitDy=0;entryX=0.5;entryY=0;entryDx=0;entryDy=0;';

export interface BusinessCells {
  vertices: DrawioVertex[];
  edges: DrawioEdge[];
}

// Os balões e as ligações de um fluxo, na geometria que vier (a da tela, com as posições arrastadas) e com os
// pontos de dobra salvos. Os índices das ligações são os da lista de balões devolvida.
export function businessCells(flow: BusinessFlow, geometry: BusinessGeometry, bends: BusinessBends, linkOf?: HostLinkOf): BusinessCells {
  const shown = flow.steps.filter((step) => geometry.boxes.has(step.id));
  const index = new Map(shown.map((step, i) => [step.id, i]));
  const vertices = shown.map((step): DrawioVertex => {
    const box = geometry.boxes.get(step.id)!;
    const location = step.ref ? { file: step.ref.file, line: step.ref.line } : null;
    return {
      text: step.label,
      rastroId: step.id,
      rastroNode: step.nodeId,
      reference: codeReference(step.ref?.repository, location),
      tooltip: tooltipOf(null, location),
      link: step.ref ? (linkOf?.(step.ref.repository, location) ?? null) : null,
      style: BUSINESS_STYLE[step.kind],
      x: box.x,
      y: box.y,
      width: box.width,
      height: box.height,
    };
  });
  const edges = flow.edges.flatMap((edge): DrawioEdge[] => {
    const source = index.get(edge.from);
    const target = index.get(edge.to);
    if (source === undefined || target === undefined || source === target) return [];
    const points = bends.get(edgeKey(edge.from, edge.to));
    return [
      {
        source,
        target,
        label: edge.label ? plainBranch(edge.label) : null,
        style: DRAWIO_STYLE.edge + (edge.inferred ? CALL_STYLE : '') + BUSINESS_PORTS,
        ...(points?.length ? { points: points.map((p) => ({ x: p.x, y: p.y })) } : {}),
      },
    ];
  });
  return { vertices, edges };
}

// A visão de negócio como está na tela: um balão por passo-chave, com o texto de negócio e a
// referência ao código, e as ligações com o rótulo do ramo (a inferida, tracejada). A posição é a da tela
// (inclusive a arrastada) e os pontos de dobra salvos vão nas ligações.
export function businessToDrawio(flow: BusinessFlow, geometry: BusinessGeometry, name: string, linkOf?: HostLinkOf, bends: BusinessBends = new Map()): string {
  const { vertices, edges } = businessCells(flow, geometry, bends, linkOf);
  return toDrawio(shift({ name, frames: [], vertices, edges }));
}

export function frameAround(title: string, boxes: readonly NodeBox[]): DrawioFrame | null {
  if (boxes.length === 0) return null;
  const x = Math.min(...boxes.map((b) => b.x)) - FRAME_PAD;
  const y = Math.min(...boxes.map((b) => b.y)) - FRAME_PAD - FRAME_HEADER;
  const right = Math.max(...boxes.map((b) => b.x + b.width)) + FRAME_PAD;
  const bottom = Math.max(...boxes.map((b) => b.y + b.height)) + FRAME_PAD;
  return { title, x, y, width: right - x, height: bottom - y };
}

// Leva o desenho para perto da origem, com margem: as posições da tela podem começar longe do zero.
export function shift(page: DrawioPage): DrawioPage {
  const all = [...page.frames, ...page.vertices];
  if (all.length === 0) return page;
  const dx = MARGIN - Math.min(...all.map((b) => b.x));
  const dy = MARGIN - Math.min(...all.map((b) => b.y));
  const move = <T extends { x: number; y: number }>(b: T): T => ({ ...b, x: b.x + dx, y: b.y + dy });
  return {
    ...page,
    frames: page.frames.map(move),
    vertices: page.vertices.map(move),
    edges: page.edges.map((e) => (e.points ? { ...e, points: e.points.map(move) } : e)),
  };
}

// O arquivo: um mxfile com uma página. Cada balão é um UserObject (rótulo em HTML, dica e link);
// as molduras vêm antes, para ficarem atrás.
export function toDrawio(page: DrawioPage): string {
  const cells: string[] = ['<mxCell id="0"/>', '<mxCell id="1" parent="0"/>'];
  page.frames.forEach((frame, i) => {
    cells.push(
      `<mxCell id="f${i}" value="${attr(html(frame.title))}" style="${attr(DRAWIO_STYLE.frame)}" vertex="1" parent="1">` +
        `${geometry(frame)}</mxCell>`,
    );
  });
  page.vertices.forEach((vertex, i) => {
    const extra =
      (vertex.tooltip ? ` tooltip="${attr(vertex.tooltip)}"` : '') +
      (vertex.link ? ` link="${attr(vertex.link)}"` : '') +
      (vertex.rastroId ? ` rastroId="${attr(vertex.rastroId)}"` : '') +
      (vertex.rastroNode ? ` rastroNode="${attr(vertex.rastroNode)}"` : '');
    cells.push(
      `<UserObject id="n${i}" label="${attr(vertexLabel(vertex))}"${extra}>` +
        `<mxCell style="${attr(vertex.style)}" vertex="1" parent="1">${geometry(vertex)}</mxCell></UserObject>`,
    );
  });
  page.edges.forEach((edge, i) => {
    const points = edge.points?.length
      ? `<Array as="points">${edge.points.map((p) => `<mxPoint x="${num(p.x)}" y="${num(p.y)}"/>`).join('')}</Array>`
      : '';
    cells.push(
      `<mxCell id="e${i}" value="${attr(edge.label ? html(edge.label) : '')}" style="${attr(edge.style)}" edge="1" parent="1" source="n${edge.source}" target="n${edge.target}">` +
        `<mxGeometry relative="1" as="geometry">${points}</mxGeometry></mxCell>`,
    );
  });
  return (
    '<?xml version="1.0" encoding="UTF-8"?>\n' +
    `<mxfile host="Rastro" type="device"><diagram id="rastro" name="${attr(page.name)}">` +
    '<mxGraphModel grid="1" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" page="0" pageScale="1" math="0" shadow="0">' +
    `<root>${cells.join('')}</root></mxGraphModel></diagram></mxfile>\n`
  );
}

// Rótulo em HTML: o texto e, abaixo, a referência em fonte monoespaçada.
function vertexLabel(vertex: DrawioVertex): string {
  const text = html(vertex.text);
  if (!vertex.reference) return text;
  return `${text}<br><span style="font-size:9px;color:#555555;font-family:Consolas,monospace">${html(vertex.reference)}</span>`;
}

function geometry(box: { x: number; y: number; width: number; height: number }): string {
  return `<mxGeometry x="${num(box.x)}" y="${num(box.y)}" width="${num(box.width)}" height="${num(box.height)}" as="geometry"/>`;
}

function num(value: number): number {
  return Math.round(value * 100) / 100;
}

// Texto que vai dentro do HTML do rótulo. Depois, o HTML inteiro é escapado de novo como atributo
// XML: o escape duplo que o draw.io espera (o "<" do texto vira "&amp;lt;" no arquivo).
function html(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/\n/g, '<br>');
}

function attr(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/\n/g, '&#10;');
}
