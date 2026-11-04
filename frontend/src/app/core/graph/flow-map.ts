import { FlowMap, FlowNode, MapBlock, MapBlockKind, MapLane, MapLaneKind, MapLink, MapLinkKind, MapPath } from '../models';
import { plainBranch } from './plain';
import { readingLabel } from './summary';

// Desenho do mapa do fluxo (Fase 31): onde cada bloco fica, como as ligações saem de um para o
// outro e o que acende quando um caminho é escolhido. Puro e determinístico: o mesmo mapa dá
// sempre o mesmo desenho.

export const MAP_BLOCK_SLUG: Record<MapBlockKind, string> = {
  [MapBlockKind.Entry]: 'entry',
  [MapBlockKind.Step]: 'step',
  [MapBlockKind.Decision]: 'decision',
  [MapBlockKind.Outside]: 'outside',
};

// Letra e cor, nunca só cor (design-system.md).
export const MAP_BLOCK_GLYPH: Record<MapBlockKind, string> = {
  [MapBlockKind.Entry]: 'E',
  [MapBlockKind.Step]: 'M',
  [MapBlockKind.Decision]: '?',
  [MapBlockKind.Outside]: '↗',
};

export const MAP_BLOCK_LABEL: Record<MapBlockKind, string> = {
  [MapBlockKind.Entry]: 'Entrada',
  [MapBlockKind.Step]: 'Passo',
  [MapBlockKind.Decision]: 'Decisão',
  [MapBlockKind.Outside]: 'Fora do código',
};

export const OUTSIDE_LANE_TITLE = 'Fora do código';

// Medidas do desenho, em px.
export const MAP_BOX = { width: 208, height: 64, gapX: 72, gapY: 32, header: 40, pad: 16, bow: 28 } as const;
const LABEL_CHAR_PX = 6.6;
const LABEL_PAD_PX = 12;
const LABEL_HEIGHT_PX = 18;

// O primeiro trecho (sistema nulo) é o repositório do levantamento; a faixa de fora tem nome próprio.
export function laneTitle(lane: MapLane, repository: string): string {
  return lane.kind === MapLaneKind.Outside ? OUTSIDE_LANE_TITLE : (lane.system ?? repository);
}

// Título do bloco: a condição legível na decisão, o nome legível da Fase 23 nos demais. Sem o nó
// no grafo (rascunho antigo, nó escondido), vale o título que a API mandou.
export function blockTitle(block: MapBlock, nodes: ReadonlyMap<string, FlowNode>): string {
  const node = nodes.get(block.nodeId);
  if (node) return readingLabel(node);
  return block.condition ? `Se ${block.condition}` : block.title;
}

// "VERBO rota" da chamada entre sistemas; sem rota, só o verbo.
export function callText(link: MapLink): string | null {
  const http = link.http;
  if (!http) return null;
  return http.route ? `${http.method} ${http.route}` : http.method;
}

// Texto da ligação: o ramo da decisão, o verbo e a rota da chamada.
export function linkText(link: MapLink): string | null {
  if (link.kind === MapLinkKind.Outcome) return link.label ? plainBranch(link.label) : null;
  if (link.kind === MapLinkKind.Call) return callText(link);
  return null;
}

export interface PlacedBlock {
  block: MapBlock;
  column: number;
  row: number;
  x: number;
  y: number;
}

export interface PlacedLink {
  link: MapLink;
  // Posição na lista do mapa: o que acende e o que apaga se guarda por ela.
  index: number;
  d: string;
  text: string | null;
  labelX: number;
  labelY: number;
  labelWidth: number;
}

export interface PlacedLane {
  lane: MapLane;
  x: number;
}

export interface MapGeometry {
  width: number;
  height: number;
  lanes: PlacedLane[];
  blocks: PlacedBlock[];
  links: PlacedLink[];
}

// Linha de cada bloco: um depois do que chega a ele pela sequência ou pelo desfecho, e na mesma
// linha de quem o chama quando é uma chamada de outro sistema. Dois blocos nunca dividem a mesma
// faixa e a mesma linha: o que chega depois desce. Processa na ordem do mapa, esperando as
// origens ficarem prontas (um laço, que não deixa ninguém pronto, cai na ordem do mapa).
export function placeRows(map: FlowMap): Map<string, number> {
  const rows = new Map<string, number>();
  const taken = new Set<string>();
  const incoming = new Map<string, MapLink[]>();
  for (const link of map.links) incoming.set(link.to, [...(incoming.get(link.to) ?? []), link]);
  const byId = new Map(map.blocks.map((b) => [b.id, b]));
  const pending = [...map.blocks].sort((a, b) => a.order - b.order);

  while (pending.length > 0) {
    let at = pending.findIndex((b) => (incoming.get(b.id) ?? []).every((l) => l.from === b.id || rows.has(l.from) || !byId.has(l.from)));
    if (at < 0) at = 0;
    const [block] = pending.splice(at, 1);
    let row = 0;
    for (const link of incoming.get(block.id) ?? []) {
      const from = rows.get(link.from);
      if (from === undefined) continue;
      const sameLane = byId.get(link.from)?.lane === block.lane;
      row = Math.max(row, link.kind === MapLinkKind.Call && !sameLane ? from : from + 1);
    }
    while (taken.has(`${block.lane}|${row}`)) row++;
    taken.add(`${block.lane}|${row}`);
    rows.set(block.id, row);
  }
  return rows;
}

// "single": uma coluna só (celular). Cada bloco ganha a própria linha, na ordem do desenho largo,
// e a faixa vira uma etiqueta no bloco.
export function mapGeometry(map: FlowMap, single: boolean): MapGeometry {
  const { width, height, gapX, gapY, header, pad } = MAP_BOX;
  const laneIndex = new Map(map.lanes.map((lane, index) => [lane.id, index]));
  const wide = placeRows(map);
  const sorted = [...map.blocks].sort((a, b) => (wide.get(a.id)! - wide.get(b.id)!) || (laneIndex.get(a.lane) ?? 0) - (laneIndex.get(b.lane) ?? 0) || a.order - b.order);
  const singleRow = new Map(sorted.map((block, index) => [block.id, index]));
  const top = single ? pad : pad + header;

  const placed: PlacedBlock[] = map.blocks.map((block) => {
    const column = single ? 0 : (laneIndex.get(block.lane) ?? 0);
    const row = single ? singleRow.get(block.id)! : wide.get(block.id)!;
    return { block, column, row, x: pad + column * (width + gapX), y: top + row * (height + gapY) };
  });
  const byId = new Map(placed.map((p) => [p.block.id, p]));

  const columns = single ? 1 : Math.max(1, map.lanes.length);
  const rowCount = placed.reduce((max, p) => Math.max(max, p.row + 1), 0);
  const links = map.links.flatMap((link, index) => {
    const from = byId.get(link.from);
    const to = byId.get(link.to);
    return from && to ? [placeLink(link, index, from, to)] : [];
  });
  const labelOverflow = links.reduce((max, l) => Math.max(max, l.labelX + l.labelWidth / 2), 0);

  return {
    width: Math.max(pad * 2 + columns * width + (columns - 1) * gapX, Math.ceil(labelOverflow + pad), pad * 2 + width + MAP_BOX.bow * 2),
    height: top + Math.max(rowCount, 1) * (height + gapY) - gapY + pad,
    lanes: single ? [] : map.lanes.map((lane, index) => ({ lane, x: pad + index * (width + gapX) })),
    blocks: placed,
    links,
  };
}

function placeLink(link: MapLink, index: number, from: PlacedBlock, to: PlacedBlock): PlacedLink {
  const { width, height, gapX, bow } = MAP_BOX;
  const text = linkText(link);
  let d: string;
  let mx: number;
  let my: number;

  if (from.column !== to.column) {
    // Entre faixas: sai do lado de dentro de um bloco e entra no lado de dentro do outro.
    const forward = from.column < to.column;
    const sx = forward ? from.x + width : from.x;
    const ex = forward ? to.x : to.x + width;
    const sy = from.y + height / 2;
    const ey = to.y + height / 2;
    const dx = Math.min(gapX / 2, Math.abs(ex - sx) / 2) * (forward ? 1 : -1);
    d = `M ${sx} ${sy} C ${sx + dx} ${sy}, ${ex - dx} ${ey}, ${ex} ${ey}`;
    mx = (sx + ex) / 2;
    my = (sy + ey) / 2;
  } else if (to.row === from.row + 1) {
    const x = from.x + width / 2;
    d = `M ${x} ${from.y + height} L ${x} ${to.y}`;
    mx = x;
    my = (from.y + height + to.y) / 2;
  } else {
    // Mesma faixa, linhas distantes: contorna pela direita para não passar por cima dos blocos do meio.
    const sx = from.x + width;
    const sy = from.y + height / 2;
    const ey = to.y + height / 2;
    d = `M ${sx} ${sy} C ${sx + bow} ${sy}, ${sx + bow} ${ey}, ${sx} ${ey}`;
    mx = sx + bow * 0.75;
    my = (sy + ey) / 2;
  }
  const labelWidth = text ? Math.ceil(text.length * LABEL_CHAR_PX + LABEL_PAD_PX) : 0;
  return { link, index, d, text, labelX: mx, labelY: my - LABEL_HEIGHT_PX / 2, labelWidth };
}

export const MAP_LABEL_HEIGHT = LABEL_HEIGHT_PX;

// ---------- caminhos ----------

// Blocos de um caminho; sem caminho escolhido, null (nada apaga).
export function litBlocks(path: MapPath | null): ReadonlySet<string> | null {
  return path ? new Set(path.blocks) : null;
}

// Posição do desfecho que a ligação de desfecho representa, na decisão de onde ela sai.
export function outcomeOfLink(map: FlowMap, link: MapLink): number {
  if (link.kind !== MapLinkKind.Outcome) return -1;
  const decision = map.blocks.find((b) => b.id === link.from);
  return (decision?.outcomes ?? []).findIndex((o) => o.to === link.to && (!link.label || o.label === link.label));
}

// Ligações que o caminho percorre: as duas pontas estão nele e, no desfecho, é o ramo que ele escolheu.
export function litLinks(map: FlowMap, path: MapPath | null): ReadonlySet<number> | null {
  if (!path) return null;
  const inPath = new Set(path.blocks);
  const lit = new Set<number>();
  map.links.forEach((link, index) => {
    if (!inPath.has(link.from) || !inPath.has(link.to)) return;
    if (link.kind === MapLinkKind.Outcome) {
      const outcome = outcomeOfLink(map, link);
      if (!path.choices.some((c) => c.decision === link.from && c.outcome === outcome)) return;
    }
    lit.add(index);
  });
  return lit;
}

// Clicar num desfecho escolhe o primeiro caminho que fez aquela escolha.
export function pathForOutcome(map: FlowMap, decision: string, outcome: number): MapPath | null {
  return map.paths.find((p) => p.choices.some((c) => c.decision === decision && c.outcome === outcome)) ?? null;
}

// Rótulo do botão do caminho: o texto do desfecho de cada decisão escolhida ("Se sim, Caso pix"),
// ou "Caminho N" quando o caminho não passa por decisão.
export function pathLabel(map: FlowMap, path: MapPath, position: number): string {
  const texts = path.choices.flatMap((choice) => {
    const label = map.blocks.find((b) => b.id === choice.decision)?.outcomes?.[choice.outcome]?.label;
    return label ? [plainBranch(label)] : [];
  });
  return texts.length > 0 ? `${position + 1}. ${texts.join(', ')}` : `Caminho ${position + 1}`;
}

// Vizinhos do bloco no caminho. Sem caminho escolhido ("Todos"), vale o primeiro que passa pelo bloco.
export function pathNeighbors(map: FlowMap, chosen: MapPath | null, blockId: string): { path: MapPath | null; previous: string | null; next: string | null } {
  const path = chosen && chosen.blocks.includes(blockId) ? chosen : chosen ? null : (map.paths.find((p) => p.blocks.includes(blockId)) ?? null);
  if (!path) return { path: null, previous: null, next: null };
  const at = path.blocks.indexOf(blockId);
  return { path, previous: path.blocks[at - 1] ?? null, next: path.blocks[at + 1] ?? null };
}
