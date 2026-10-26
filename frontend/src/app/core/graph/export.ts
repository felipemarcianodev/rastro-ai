import { EdgeConfidence, FlowEdge, FlowGraph, FlowNode, NodeKind } from '../models';
import { LayoutDirection, NodeBox } from './layout';
import { Port, TimelineHead, TimelineRepeat, timelineRoutes } from './timeline-layout';

// Exportar o recorte (Fase 12): o grafo que está na tela vira Mermaid (para colar em documento
// ou PR) e SVG (imagem com as posições da tela, inclusive as arrastadas).

const MERMAID_DIRECTION: Record<LayoutDirection, string> = {
  [LayoutDirection.Horizontal]: 'LR',
  [LayoutDirection.Timeline]: 'TD',
  [LayoutDirection.Tree]: 'TD',
};

export function toMermaid(graph: FlowGraph, direction = LayoutDirection.Horizontal): string {
  const ids = new Map(graph.nodes.map((node, index) => [node.id, `n${index}`]));
  const lines = [`flowchart ${MERMAID_DIRECTION[direction]}`];
  for (const node of graph.nodes) lines.push(`  ${ids.get(node.id)}${mermaidShape(node)}`);
  for (const edge of graph.edges) {
    const from = ids.get(edge.from);
    const to = ids.get(edge.to);
    if (!from || !to || from === to) continue;
    const arrow = edge.confidence === EdgeConfidence.Inferred ? '-.->' : edge.confidence === EdgeConfidence.Confirmed ? '==>' : '-->';
    lines.push(edge.label ? `  ${from} ${arrow}|"${mermaidText(edge.label)}"| ${to}` : `  ${from} ${arrow} ${to}`);
  }
  return lines.join('\n') + '\n';
}

function mermaidShape(node: FlowNode): string {
  const text = `"${mermaidText(node.label)}"`;
  switch (node.kind) {
    case NodeKind.Decision:
    case NodeKind.Loop:
    case NodeKind.Try:
      return `{${text}}`;
    case NodeKind.External:
      return `([${text}])`;
    case NodeKind.Unresolved:
      return `[[${text}]]`;
    case NodeKind.Entry:
      return `[/${text}/]`;
    default:
      return `[${text}]`;
  }
}

// Aspas e sinais que o Mermaid interpreta viram entidades.
function mermaidText(text: string): string {
  return text.replace(/"/g, '#quot;').replace(/</g, '#lt;').replace(/>/g, '#gt;').replace(/\n/g, ' ');
}

export interface SvgColors {
  paper: string;
  surface: string;
  ink: string;
  inkSoft: string;
  line: string;
  edge: string;
  inferred: string;
  confirmed: string;
  unresolved: string;
}

const PADDING = 24;
const CHAR_WIDTH = 7.3;

// O que o desenho da tela acrescenta ao grafo: o passo repetido e os cabeçalhos dos galhos.
export interface SvgExtras {
  repeats?: ReadonlyMap<string, TimelineRepeat>;
  heads?: readonly TimelineHead[];
}

// Ligação no SVG, como na tela (timelineRoutes): "next" é o passo seguinte, "section" o primeiro
// passo de uma seção do bloco, "branch" o primeiro passo de um galho.
type EdgeShape = 'next' | 'section' | 'branch';

// Porta a 16px da borda, como no tema (centro a 20px).
const PORT = 20;

export function toSvg(
  graph: FlowGraph,
  boxes: ReadonlyMap<string, NodeBox>,
  colors: SvgColors,
  direction = LayoutDirection.Horizontal,
  extras: SvgExtras = {},
): string {
  const byId = new Map(graph.nodes.map((n) => [n.id, n]));
  const repeats = extras.repeats ?? new Map<string, TimelineRepeat>();
  const shown = [
    ...graph.nodes.filter((n) => boxes.has(n.id)).map((node) => ({ node, id: node.id, repeat: false })),
    ...[...repeats.values()].filter((r) => boxes.has(r.id) && byId.has(r.of)).map((r) => ({ node: byId.get(r.of)!, id: r.id, repeat: true })),
  ];
  if (shown.length === 0) return '';
  const all = shown.map((n) => boxes.get(n.id)!);
  const minX = Math.min(...all.map((b) => b.x)) - PADDING;
  const minY = Math.min(...all.map((b) => b.y)) - PADDING;
  const width = Math.max(...all.map((b) => b.x + b.width)) - minX + PADDING;
  const height = Math.max(...all.map((b) => b.y + b.height)) - minY + PADDING;
  const across = direction === LayoutDirection.Horizontal;

  const routes = timelineRoutes(graph, repeats);
  const portless = (id: string) => id.slice(id.indexOf(':') + 1);
  const edges = graph.edges.flatMap((edge, index) => {
    const route = routes.get(index);
    const a = route && boxes.get(portless(route.source));
    const b = route && boxes.get(portless(route.target));
    if (!route || !a || !b) return [];
    const shape: EdgeShape = route.source.startsWith(Port.Side) ? 'branch' : route.target.startsWith(Port.Top) ? 'next' : 'section';
    return [edgePath(edge, shape, across, a, b, minX, minY, colors)];
  });

  const nodes = shown.map(({ node, id, repeat }) => {
    const box = boxes.get(id)!;
    const x = box.x - minX;
    const y = box.y - minY;
    const unresolved = node.kind === NodeKind.Unresolved;
    const rounded = node.kind === NodeKind.External ? box.height / 2 : 8;
    const stroke = unresolved ? colors.unresolved : node.kind === NodeKind.Entry ? colors.ink : colors.line;
    const dashed = repeat || node.kind === NodeKind.Return || node.kind === NodeKind.Jump;
    const dash = unresolved ? ' stroke-dasharray="2 3"' : dashed ? ' stroke-dasharray="5 3"' : '';
    const text = repeat ? `↻ ${node.label}` : node.label;
    const max = Math.max(4, Math.floor((box.width - 20) / CHAR_WIDTH));
    const label = text.length > max ? `…${text.slice(text.length - max + 1)}` : text;
    return (
      `<g><title>${xml(node.label)}</title>` +
      `<rect x="${r(x)}" y="${r(y)}" width="${r(box.width)}" height="${r(box.height)}" rx="${r(rounded)}" fill="${colors.surface}" stroke="${stroke}" stroke-width="${node.kind === NodeKind.Entry ? 2 : 1}"${dash}/>` +
      `<text x="${r(x + 10)}" y="${r(y + box.height / 2 + 4)}" font-size="12" fill="${repeat ? colors.inkSoft : colors.ink}">${xml(label)}</text></g>`
    );
  });

  const heads = (extras.heads ?? []).map(
    (head) => `<text x="${r(head.x - minX)}" y="${r(head.y - minY + 13)}" font-size="11" font-weight="600" fill="${colors.inkSoft}">${xml(head.label)}</text>`,
  );

  const marker = (id: string, color: string) =>
    `<marker id="arrow-${id}" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0 0 L10 5 L0 10 z" fill="${color}"/></marker>`;

  return (
    `<svg xmlns="http://www.w3.org/2000/svg" width="${r(width)}" height="${r(height)}" viewBox="0 0 ${r(width)} ${r(height)}" font-family="JetBrains Mono, Cascadia Code, Consolas, monospace">` +
    `<defs>${marker('extracted', colors.edge)}${marker('inferred', colors.inferred)}${marker('confirmed', colors.confirmed)}</defs>` +
    `<rect width="100%" height="100%" fill="${colors.paper}"/>` +
    edges.join('') +
    heads.join('') +
    nodes.join('') +
    `</svg>`
  );
}

// Ligação em ângulo reto, saindo e entrando pelas mesmas portas da tela. Para baixo: o passo
// seguinte desce pela esquerda; o galho sai pela direita. Para a direita: o passo seguinte segue
// pelo alto; o galho sai por baixo.
function edgePath(edge: FlowEdge, shape: EdgeShape, across: boolean, a: NodeBox, b: NodeBox, minX: number, minY: number, colors: SvgColors): string {
  const X = (value: number) => r(value - minX);
  const Y = (value: number) => r(value - minY);
  let d: string;
  if (!across) {
    if (shape === 'branch') {
      const sx = a.x + a.width;
      const turn = b.x - 24 > sx ? b.x - 24 : sx + 16;
      d = `M${X(sx)} ${Y(a.y + a.height / 2)} H${X(turn)} V${Y(b.y + b.height / 2)} H${X(b.x)}`;
    } else if (shape === 'section') {
      d = `M${X(a.x + PORT)} ${Y(a.y + a.height)} V${Y(b.y + b.height / 2)} H${X(b.x)}`;
    } else {
      const mid = (a.y + a.height + b.y) / 2;
      d = `M${X(a.x + PORT)} ${Y(a.y + a.height)} V${Y(mid)} H${X(b.x + PORT)} V${Y(b.y)}`;
    }
  } else if (shape === 'branch') {
    const sy = a.y + a.height;
    const turn = b.y - 18 > sy ? b.y - 18 : sy + 12;
    d = `M${X(a.x + PORT)} ${Y(sy)} V${Y(turn)} H${X(b.x + PORT)} V${Y(b.y)}`;
  } else if (shape === 'section') {
    d = `M${X(a.x + a.width)} ${Y(a.y + PORT)} H${X(b.x + PORT)} V${Y(b.y)}`;
  } else {
    const mid = (a.x + a.width + b.x) / 2;
    d = `M${X(a.x + a.width)} ${Y(a.y + PORT)} H${X(mid)} V${Y(b.y + PORT)} H${X(b.x)}`;
  }
  const color = edge.confidence === EdgeConfidence.Inferred ? colors.inferred : edge.confidence === EdgeConfidence.Confirmed ? colors.confirmed : colors.edge;
  const dash = edge.confidence === EdgeConfidence.Inferred ? ' stroke-dasharray="6 4"' : '';
  const widthAttr = edge.confidence === EdgeConfidence.Confirmed ? 2.5 : 1.5;
  const marker = edge.confidence === EdgeConfidence.Inferred ? 'inferred' : edge.confidence === EdgeConfidence.Confirmed ? 'confirmed' : 'extracted';
  return `<path d="${d}" fill="none" stroke="${color}" stroke-width="${widthAttr}"${dash} marker-end="url(#arrow-${marker})"/>`;
}

function r(value: number): number {
  return Math.round(value * 10) / 10;
}

function xml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
