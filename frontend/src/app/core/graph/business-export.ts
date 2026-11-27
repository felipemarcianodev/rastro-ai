import { BusinessFlow, BusinessKind, BusinessStep } from '../models';
import { BUSINESS_KIND_GLYPH, BUSINESS_KIND_ORDER, BUSINESS_KIND_SLUG } from './business';
import { BusinessGeometry, LayoutPoint, businessTextLines, edgeKey } from './business-layout';
import { glossaryEntries } from './board';
import { BusinessBends, DrawioEdge, DrawioFrame, DrawioVertex, businessCells, codeReference, frameAround, shift, toDrawio } from './drawio';
import { mermaidText } from './export';
import { HostLinkOf } from './link-template';
import { NodeBox } from './layout';
import { plainBranch } from './plain';

// Exportar a visão de negócio e o quadro (Fase 61, ADR-010): draw.io, Mermaid, SVG e PNG, tudo a partir da
// geometria que está na tela (posições salvas e pontos de dobra). Puro e determinístico.

// O que a tela de uma visão de negócio entrega para exportar: o fluxo, a geometria com as posições salvas e os
// pontos de dobra (por ligação).
export interface BusinessExportData {
  surveyId: string;
  flow: BusinessFlow;
  geometry: BusinessGeometry;
  bends: BusinessBends;
}

// Uma faixa: o título que aparece na moldura, o desenho e o resolvedor de link do levantamento dela.
export interface ExportLane {
  title: string;
  data: BusinessExportData;
  linkOf?: HostLinkOf;
}

// Linha do bloco de glossário e legenda (o cabeçalho sai em negrito no SVG).
export interface NoteLine {
  text: string;
  heading?: boolean;
}

export interface BoardNoteTexts {
  glossary: string;
  legend: string;
  kind: (kind: BusinessKind) => string;
}

// Glossário (só as linhas "Termo: significado") e legenda dos tipos que aparecem nas faixas; nulo sem nenhum dos dois.
export function boardNoteLines(glossary: string | null, flows: readonly BusinessFlow[], texts: BoardNoteTexts): NoteLine[] | null {
  const entries = glossaryEntries(glossary ?? '');
  const present = new Set(flows.flatMap((flow) => flow.steps.map((step) => step.kind)));
  const kinds = BUSINESS_KIND_ORDER.filter((kind) => present.has(kind));
  const lines: NoteLine[] = [];
  if (entries.length > 0) lines.push({ text: texts.glossary, heading: true }, ...entries.map((e) => ({ text: `${e.term}: ${e.meaning}` })));
  if (kinds.length > 0) {
    if (lines.length > 0) lines.push({ text: '' });
    lines.push({ text: texts.legend, heading: true }, ...kinds.map((kind) => ({ text: `${BUSINESS_KIND_GLYPH[kind]} ${texts.kind(kind)}` })));
  }
  return lines.length > 0 ? lines : null;
}

// ---- Faixas lado a lado ----

const LANE_GAP = 60;

interface PlacedLane {
  lane: ExportLane;
  // A geometria e os pontos de dobra já deslocados para a posição da faixa.
  geometry: BusinessGeometry;
  bends: Map<string, LayoutPoint[]>;
  frame: DrawioFrame;
}

// Cada faixa ganha a moldura em volta dos passos e dos pontos de dobra, e as faixas ficam lado a lado, na ordem
// dada, sem se sobrepor. Faixa sem passo desenhado não entra.
function placeLanes(lanes: readonly ExportLane[]): PlacedLane[] {
  const placed: PlacedLane[] = [];
  let cursor = 0;
  for (const lane of lanes) {
    const { flow, geometry, bends } = lane.data;
    const shown = flow.steps.filter((step) => geometry.boxes.has(step.id));
    if (shown.length === 0) continue;
    const bendPoints = [...bends.values()].flat();
    const extent: NodeBox[] = [
      ...shown.map((step) => geometry.boxes.get(step.id)!),
      ...bendPoints.map((p) => ({ x: p.x, y: p.y, width: 0, height: 0 })),
    ];
    const raw = frameAround(lane.title, extent)!;
    const dx = cursor - raw.x;
    const dy = -raw.y;
    const boxes = new Map([...geometry.boxes].map(([id, box]): [string, NodeBox] => [id, { ...box, x: box.x + dx, y: box.y + dy }]));
    const moved = new Map([...bends].map(([key, points]): [string, LayoutPoint[]] => [key, points.map((p) => ({ x: p.x + dx, y: p.y + dy }))]));
    placed.push({
      lane,
      geometry: { boxes, width: geometry.width, height: geometry.height },
      bends: moved,
      frame: { ...raw, x: cursor, y: 0 },
    });
    cursor += raw.width + LANE_GAP;
  }
  return placed;
}

// ---- draw.io ----

const NOTE_STYLE = 'shape=note;whiteSpace=wrap;html=1;backgroundOutline=1;darkOpacity=0.05;fillColor=#fff2cc;strokeColor=#d6b656;align=left;verticalAlign=top;spacing=8;fontSize=11;';
const NOTE_WIDTH = 300;
const NOTE_LINE_HEIGHT = 16;
const NOTE_CHARS = 42;

function noteHeight(lines: readonly NoteLine[]): number {
  const rows = lines.reduce((sum, line) => sum + Math.max(1, Math.ceil(line.text.length / NOTE_CHARS)), 0);
  return Math.max(80, rows * NOTE_LINE_HEIGHT + 32);
}

// O quadro: uma moldura tracejada por faixa com o título dela, os passos de cada faixa na posição salva (deslocada
// para a faixa), as ligações com os pontos de dobra, o link para o host de cada passo (o levantamento da faixa
// resolve o dele) e, à direita, uma nota com o glossário e a legenda.
export function boardToDrawio(lanes: readonly ExportLane[], name: string, note: readonly NoteLine[] | null): string {
  const placed = placeLanes(lanes);
  const vertices: DrawioVertex[] = [];
  const edges: DrawioEdge[] = [];
  for (const { lane, geometry, bends } of placed) {
    const cells = businessCells(lane.data.flow, geometry, bends, lane.linkOf);
    const offset = vertices.length;
    vertices.push(...cells.vertices.map((vertex) => ({ ...vertex, rastroLane: lane.data.surveyId })));
    edges.push(...cells.edges.map((edge) => ({ ...edge, source: edge.source + offset, target: edge.target + offset })));
  }
  if (note && placed.length > 0) {
    const right = Math.max(...placed.map((p) => p.frame.x + p.frame.width));
    vertices.push({ text: note.map((line) => line.text).join('\n'), style: NOTE_STYLE, x: right + LANE_GAP, y: 0, width: NOTE_WIDTH, height: noteHeight(note) });
  }
  return toDrawio(shift({ name, frames: placed.map((p) => p.frame), vertices, edges }));
}

// ---- Mermaid ----

// As cores do draw.io, para o Mermaid sair com a cara do arquivo do draw.io (o teste confere os dois).
export const BUSINESS_COLORS: Record<BusinessKind, { fill: string; stroke: string; dashed?: boolean }> = {
  [BusinessKind.Start]: { fill: '#d5e8d4', stroke: '#82b366' },
  [BusinessKind.Decision]: { fill: '#fff2cc', stroke: '#d6b656' },
  [BusinessKind.Notification]: { fill: '#ffe6cc', stroke: '#d79b00' },
  [BusinessKind.Rejection]: { fill: '#f8cecc', stroke: '#b85450' },
  [BusinessKind.CrossSystem]: { fill: '#eeeeee', stroke: '#999999', dashed: true },
  [BusinessKind.Step]: { fill: '#ffffff', stroke: '#666666' },
  [BusinessKind.End]: { fill: '#dae8fc', stroke: '#6c8ebf' },
};

const MERMAID_INK = '#1d2a2e';

// Nome da classe do Mermaid por tipo (sem hífen).
export const BUSINESS_MERMAID_CLASS: Record<BusinessKind, string> = Object.fromEntries(
  BUSINESS_KIND_ORDER.map((kind) => [kind, `biz${BUSINESS_KIND_SLUG[kind].replace(/-/g, '')}`]),
) as Record<BusinessKind, string>;

function mermaidShape(step: BusinessStep): string {
  const text = `"${mermaidText(step.label)}"`;
  switch (step.kind) {
    case BusinessKind.Decision:
      return `{${text}}`;
    case BusinessKind.Start:
    case BusinessKind.End:
      return `([${text}])`;
    default:
      return `[${text}]`;
  }
}

// `flowchart TD`. Com `framed` (o quadro) cada faixa vira um `subgraph` com o título; sem ele, o fluxo vem solto.
// O Mermaid não guarda posição: só a estrutura, a forma e a cor por tipo. Vazio quando não há passo.
export function businessToMermaid(lanes: readonly { title: string; flow: BusinessFlow }[], framed: boolean): string {
  if (lanes.every((lane) => lane.flow.steps.length === 0)) return '';
  const lines = ['flowchart TD'];
  const byKind = new Map<BusinessKind, string[]>();
  const edgeLines: string[] = [];
  lanes.forEach((lane, laneIndex) => {
    const ids = new Map(lane.flow.steps.map((step, index) => [step.id, `l${laneIndex}s${index}`]));
    const indent = framed ? '    ' : '  ';
    if (framed) lines.push(`  subgraph lane${laneIndex}["${mermaidText(lane.title)}"]`);
    for (const step of lane.flow.steps) {
      const id = ids.get(step.id)!;
      lines.push(`${indent}${id}${mermaidShape(step)}`);
      byKind.set(step.kind, [...(byKind.get(step.kind) ?? []), id]);
    }
    if (framed) lines.push('  end');
    for (const edge of lane.flow.edges) {
      const from = ids.get(edge.from);
      const to = ids.get(edge.to);
      if (!from || !to || from === to) continue;
      const arrow = edge.inferred ? '-.->' : '-->';
      const label = edge.label ? plainBranch(edge.label) : null;
      edgeLines.push(label ? `  ${from} ${arrow}|"${mermaidText(label)}"| ${to}` : `  ${from} ${arrow} ${to}`);
    }
  });
  lines.push(...edgeLines);
  for (const kind of BUSINESS_KIND_ORDER) {
    const ids = byKind.get(kind);
    if (!ids) continue;
    const { fill, stroke, dashed } = BUSINESS_COLORS[kind];
    lines.push(`  classDef ${BUSINESS_MERMAID_CLASS[kind]} fill:${fill},stroke:${stroke},color:${MERMAID_INK}${dashed ? ',stroke-dasharray:4 3' : ''}`);
    lines.push(`  class ${ids.join(',')} ${BUSINESS_MERMAID_CLASS[kind]}`);
  }
  return lines.join('\n') + '\n';
}

// ---- SVG e PNG ----

export interface BusinessSvgColors {
  paper: string;
  ink: string;
  inkSoft: string;
  line: string;
  edge: string;
  inferred: string;
  kinds: Record<BusinessKind, { fill: string; stroke: string; ink: string }>;
}

// Lê as cores da paleta que está na tela (tokens --biz-* de cada tipo e os do tema); o que faltar cai nas cores do draw.io.
export function readBusinessColors(read: (name: string) => string): BusinessSvgColors {
  const pick = (name: string, fallback: string) => read(name).trim() || fallback;
  const kinds = Object.fromEntries(
    BUSINESS_KIND_ORDER.map((kind) => {
      const slug = BUSINESS_KIND_SLUG[kind];
      const base = BUSINESS_COLORS[kind];
      return [kind, { fill: pick(`--biz-${slug}-fill`, base.fill), stroke: pick(`--biz-${slug}-stroke`, base.stroke), ink: pick(`--biz-${slug}-ink`, MERMAID_INK) }];
    }),
  ) as BusinessSvgColors['kinds'];
  return {
    paper: pick('--paper', '#ffffff'),
    ink: pick('--ink', MERMAID_INK),
    inkSoft: pick('--ink-soft', '#555555'),
    line: pick('--line-strong', '#999999'),
    edge: pick('--edge', '#555555'),
    inferred: pick('--inferred', '#777777'),
    kinds,
  };
}

const SVG_PADDING = 24;
// Famílias do tema (--font-sans e --font-code) pelo nome, com os recursos de reserva: o SVG não leva o arquivo da fonte.
const FONT_SANS = 'Instrument Sans, Segoe UI, system-ui, sans-serif';
const FONT_CODE = 'JetBrains Mono, Cascadia Code, Consolas, monospace';
const SANS_WIDTH = 6.4;
const CODE_WIDTH = 5.4;
const TEXT_LINE = 15;

// Quebra o texto em linhas de até `max` caracteres, sem passar de `maxLines` (a última leva "…").
export function wrapText(text: string, max: number, maxLines: number): string[] {
  const width = Math.max(4, max);
  const lines: string[] = [];
  let current = '';
  for (const word of text.split(/\s+/).filter((w) => w.length > 0)) {
    let rest = word;
    while (rest.length > width) {
      if (current) {
        lines.push(current);
        current = '';
      }
      lines.push(rest.slice(0, width));
      rest = rest.slice(width);
    }
    if (!current) current = rest;
    else if (current.length + 1 + rest.length <= width) current += ` ${rest}`;
    else {
      lines.push(current);
      current = rest;
    }
  }
  if (current) lines.push(current);
  if (lines.length <= maxLines) return lines;
  const kept = lines.slice(0, maxLines);
  const last = kept[maxLines - 1];
  kept[maxLines - 1] = `${last.slice(0, Math.max(0, width - 1))}…`;
  return kept;
}

const r = (value: number): number => Math.round(value * 10) / 10;

function xml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function stepSvg(step: BusinessStep, box: NodeBox, colors: BusinessSvgColors): string {
  const palette = colors.kinds[step.kind];
  const decision = step.kind === BusinessKind.Decision;
  const pill = step.kind === BusinessKind.Start || step.kind === BusinessKind.End;
  const dash = step.kind === BusinessKind.CrossSystem ? ' stroke-dasharray="5 3"' : '';
  const stroke = `fill="${palette.fill}" stroke="${palette.stroke}" stroke-width="${step.kind === BusinessKind.Start ? 2 : 1.5}"${dash}`;
  const { x, y, width, height } = box;
  const shape = decision
    ? `<polygon points="${r(x + width / 2)},${r(y)} ${r(x + width)},${r(y + height / 2)} ${r(x + width / 2)},${r(y + height)} ${r(x)},${r(y + height / 2)}" ${stroke}/>`
    : `<rect x="${r(x)}" y="${r(y)}" width="${r(width)}" height="${r(height)}" rx="${pill ? r(height / 2) : 8}" ${stroke}/>`;

  const textWidth = decision ? width * 0.58 : width - 24;
  const label = wrapText(step.label, Math.floor(textWidth / SANS_WIDTH), Math.max(3, businessTextLines(step)));
  const location = step.ref ? { file: step.ref.file, line: step.ref.line } : null;
  const reference = codeReference(step.ref?.repository, location);
  const refChars = Math.floor((decision ? width * 0.6 : width - 16) / CODE_WIDTH);
  const refText = reference ? (reference.length > refChars ? `…${reference.slice(reference.length - refChars + 1)}` : reference) : null;
  const blockHeight = label.length * TEXT_LINE + (refText ? 14 : 0);
  let cursor = y + height / 2 - blockHeight / 2 + 11;
  const cx = r(x + width / 2);
  const parts = label.map((line) => {
    const out = `<text x="${cx}" y="${r(cursor)}" text-anchor="middle" font-size="12"${step.kind === BusinessKind.Start || step.kind === BusinessKind.Notification ? ' font-weight="600"' : ''} fill="${palette.ink}">${xml(line)}</text>`;
    cursor += TEXT_LINE;
    return out;
  });
  if (refText) parts.push(`<text x="${cx}" y="${r(cursor + 2)}" text-anchor="middle" font-size="9" font-family="${FONT_CODE}" fill="${colors.inkSoft}">${xml(refText)}</text>`);
  const where = reference ? `\n${step.ref!.file}:${step.ref!.line}` : '';
  return `<g data-step="${xml(step.id)}"><title>${xml(step.label + where)}</title>${shape}${parts.join('')}</g>`;
}

function edgeSvg(label: string | null, inferred: boolean, a: NodeBox, b: NodeBox, bends: readonly LayoutPoint[], colors: BusinessSvgColors): string {
  const start = { x: a.x + a.width / 2, y: a.y + a.height };
  const end = { x: b.x + b.width / 2, y: b.y };
  const mid = (start.y + end.y) / 2;
  // Com pontos de dobra, a linha passa por eles (como na tela); sem eles, desce, anda de lado e desce de novo.
  const points = bends.length > 0 ? [start, ...bends, end] : [start, { x: start.x, y: mid }, { x: end.x, y: mid }, end];
  const d = points.map((p, i) => `${i === 0 ? 'M' : 'L'}${r(p.x)} ${r(p.y)}`).join(' ');
  const color = inferred ? colors.inferred : colors.edge;
  const path = `<path d="${d}" fill="none" stroke="${color}" stroke-width="1.5"${inferred ? ' stroke-dasharray="6 4"' : ''} marker-end="url(#bz-arrow-${inferred ? 'inferred' : 'plain'})"/>`;
  if (!label) return path;
  // O rótulo do ramo fica no último trecho, sobre um fundo da cor da tela para não se misturar com a linha.
  const from = points[points.length - 2];
  const to = points[points.length - 1];
  const lx = r((from.x + to.x) / 2);
  const ly = r((from.y + to.y) / 2);
  const w = label.length * SANS_WIDTH + 10;
  return (
    `${path}<rect x="${r(lx - w / 2)}" y="${r(ly - 9)}" width="${r(w)}" height="16" rx="3" fill="${colors.paper}"/>` +
    `<text x="${lx}" y="${r(ly + 3)}" text-anchor="middle" font-size="11" font-weight="600" fill="${colors.ink}">${xml(label)}</text>`
  );
}

function noteSvg(note: readonly NoteLine[], x: number, y: number, colors: BusinessSvgColors): { svg: string; height: number } {
  const palette = colors.kinds[BusinessKind.Decision];
  const rows = note.flatMap((line) => (line.text === '' ? [{ text: '', heading: false }] : wrapText(line.text, NOTE_CHARS, 8).map((text) => ({ text, heading: !!line.heading }))));
  const height = Math.max(60, rows.length * NOTE_LINE_HEIGHT + 24);
  const texts = rows.map(
    (row, i) => `<text x="${r(x + 12)}" y="${r(y + 22 + i * NOTE_LINE_HEIGHT)}" font-size="11"${row.heading ? ' font-weight="700"' : ''} fill="${palette.ink}">${xml(row.text)}</text>`,
  );
  return {
    svg: `<g><rect x="${r(x)}" y="${r(y)}" width="${NOTE_WIDTH}" height="${r(height)}" rx="4" fill="${palette.fill}" stroke="${palette.stroke}"/>${texts.join('')}</g>`,
    height,
  };
}

// A visão de negócio ou o quadro como imagem, com as posições da tela e as cores por tipo. Com `framed` (o quadro) cada
// faixa leva a moldura tracejada com o título. Nulo quando não há passo desenhado.
export function businessToSvg(
  lanes: readonly ExportLane[],
  colors: BusinessSvgColors,
  framed: boolean,
  note: readonly NoteLine[] | null = null,
): { svg: string; width: number; height: number } | null {
  const placed = placeLanes(lanes);
  if (placed.length === 0) return null;

  const rects: { x: number; y: number; width: number; height: number }[] = [];
  const body: string[] = [];
  const edges: string[] = [];
  const nodes: string[] = [];
  for (const { lane, geometry, bends, frame } of placed) {
    const { flow } = lane.data;
    if (framed) {
      rects.push(frame);
      body.push(
        `<rect x="${r(frame.x)}" y="${r(frame.y)}" width="${r(frame.width)}" height="${r(frame.height)}" rx="6" fill="none" stroke="${colors.line}" stroke-dasharray="6 4"/>` +
          `<text x="${r(frame.x + 10)}" y="${r(frame.y + 20)}" font-size="12" font-weight="700" fill="${colors.inkSoft}">${xml(lane.title)}</text>`,
      );
    }
    for (const step of flow.steps) {
      const box = geometry.boxes.get(step.id);
      if (box) {
        rects.push(box);
        nodes.push(stepSvg(step, box, colors));
      }
    }
    for (const edge of flow.edges) {
      const a = geometry.boxes.get(edge.from);
      const b = geometry.boxes.get(edge.to);
      if (!a || !b || edge.from === edge.to) continue;
      const points = bends.get(edgeKey(edge.from, edge.to)) ?? [];
      points.forEach((p) => rects.push({ x: p.x, y: p.y, width: 0, height: 0 }));
      edges.push(edgeSvg(edge.label ? plainBranch(edge.label) : null, edge.inferred, a, b, points, colors));
    }
  }
  if (note) {
    const right = Math.max(...placed.map((p) => p.frame.x + p.frame.width));
    const top = framed ? 0 : Math.min(...rects.map((b) => b.y));
    const drawn = noteSvg(note, right + LANE_GAP, top, colors);
    rects.push({ x: right + LANE_GAP, y: top, width: NOTE_WIDTH, height: drawn.height });
    nodes.push(drawn.svg);
  }

  const minX = Math.min(...rects.map((b) => b.x)) - SVG_PADDING;
  const minY = Math.min(...rects.map((b) => b.y)) - SVG_PADDING;
  const width = Math.max(...rects.map((b) => b.x + b.width)) + SVG_PADDING - minX;
  const height = Math.max(...rects.map((b) => b.y + b.height)) + SVG_PADDING - minY;
  const marker = (id: string, color: string) =>
    `<marker id="bz-arrow-${id}" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0 0 L10 5 L0 10 z" fill="${color}"/></marker>`;
  const svg =
    `<svg xmlns="http://www.w3.org/2000/svg" width="${r(width)}" height="${r(height)}" viewBox="${r(minX)} ${r(minY)} ${r(width)} ${r(height)}" font-family="${FONT_SANS}">` +
    `<defs>${marker('plain', colors.edge)}${marker('inferred', colors.inferred)}</defs>` +
    `<rect x="${r(minX)}" y="${r(minY)}" width="${r(width)}" height="${r(height)}" fill="${colors.paper}"/>` +
    body.join('') +
    edges.join('') +
    nodes.join('') +
    `</svg>`;
  return { svg, width: r(width), height: r(height) };
}
