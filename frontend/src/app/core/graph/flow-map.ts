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

// ---------- trechos recolhidos (Fase 35) ----------

// O sistema de entrada e os que ele chama direto ficam abertos; a partir daí, cada sistema é um
// bloco só, que abre ao clicar.
export const OPEN_DEPTH = 1;

export function isCollapsible(block: MapBlock): boolean {
  return !!block.segment && (block.depth ?? 0) > OPEN_DEPTH;
}

export interface VisibleMap {
  map: FlowMap;
  // Bloco de entrada recolhido e quantos blocos ele esconde.
  collapsed: ReadonlyMap<string, number>;
}

// O que se desenha: anda a partir da entrada, entrando só nos trechos abertos. Um bloco de fora
// aparece quando alguém visível o chama. Os caminhos ficam só com os blocos visíveis.
export function visibleMap(map: FlowMap, opened: ReadonlySet<string>): VisibleMap {
  if (!map.blocks.some(isCollapsible)) return { map, collapsed: new Map() };

  const members = new Map<string, MapBlock[]>();
  for (const block of map.blocks) if (block.segment) members.set(block.segment, [...(members.get(block.segment) ?? []), block]);
  const byId = new Map(map.blocks.map((b) => [b.id, b]));
  const isOpen = (segment: string) => !isCollapsible(byId.get(segment)!) || opened.has(segment);
  const calls = map.links.filter((l) => l.kind === MapLinkKind.Call);

  const visible = new Set<string>();
  const pending = map.blocks.filter((b) => (b.depth ?? 0) === 0 && b.segment === b.id).map((b) => b.id);
  const entered = new Set<string>();
  while (pending.length > 0) {
    const segment = pending.shift()!;
    visible.add(segment);
    if (entered.has(segment) || !isOpen(segment)) continue;
    entered.add(segment);
    const blocks = members.get(segment) ?? [];
    for (const block of blocks) visible.add(block.id);
    const ids = new Set(blocks.map((b) => b.id));
    for (const call of calls) {
      if (!ids.has(call.from)) continue;
      const target = byId.get(call.to);
      if (!target) continue;
      if (target.segment) pending.push(target.segment);
      else visible.add(target.id);
    }
  }

  const collapsed = new Map<string, number>();
  for (const [segment, blocks] of members) {
    if (!visible.has(segment) || entered.has(segment)) continue;
    const ids = new Set(blocks.map((b) => b.id));
    const targets = new Set(calls.filter((l) => ids.has(l.from) && !visible.has(l.to)).map((l) => l.to));
    const hidden = blocks.length - 1 + targets.size;
    if (hidden > 0) collapsed.set(segment, hidden);
  }

  return {
    collapsed,
    map: {
      ...map,
      lanes: map.lanes.filter((lane) => map.blocks.some((b) => b.lane === lane.id && visible.has(b.id))),
      blocks: map.blocks.filter((b) => visible.has(b.id)),
      links: map.links.filter((l) => visible.has(l.from) && visible.has(l.to)),
      paths: map.paths.map((path) => onlyVisible(path, visible)),
    },
  };
}

export function onlyVisible(path: MapPath, visible: ReadonlySet<string>): MapPath {
  return { ...path, blocks: path.blocks.filter((id) => visible.has(id)) };
}

// Trechos que precisam abrir para o bloco aparecer: o dele (salvo se for a própria entrada) e os
// de quem chama, até um trecho que já fica aberto.
export function segmentsToReveal(map: FlowMap, blockId: string): string[] {
  const byId = new Map(map.blocks.map((b) => [b.id, b]));
  const callerOf = (id: string) => map.links.filter((l) => l.kind === MapLinkKind.Call && l.to === id).map((l) => byId.get(l.from)).filter((b): b is MapBlock => !!b).sort((a, b) => (a.depth ?? 0) - (b.depth ?? 0))[0];
  const found: string[] = [];
  const seen = new Set<string>();
  let block = byId.get(blockId);
  if (block && !block.segment) block = callerOf(block.id);
  else if (block && block.segment === block.id) block = callerOf(block.id);
  while (block?.segment && !seen.has(block.segment)) {
    seen.add(block.segment);
    if (isCollapsible(block)) found.push(block.segment);
    block = callerOf(block.segment);
  }
  return found;
}

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

// A API guarda poucos caminhos, e num fluxo com muitas decisões a maioria dos desfechos não está em
// nenhum deles. O destaque monta o trecho que importa: da entrada até a decisão e, dali, pelo ramo
// escolhido até onde o fluxo segue (nas decisões seguintes, o primeiro ramo que não falha).
export function outcomeTrail(map: FlowMap, decision: string, outcome: number): MapPath | null {
  const chosen = map.blocks.find((b) => b.id === decision)?.outcomes?.[outcome];
  if (!chosen) return null;
  const incoming = (id: string) => map.links.find((l) => l.to === id);
  const leaving = (id: string) => map.links.filter((l) => l.from === id);
  const choices = [{ decision, outcome }];
  const seen = new Set([decision]);
  const before: string[] = [];

  for (let link = incoming(decision); link && !seen.has(link.from); link = incoming(link.from)) {
    seen.add(link.from);
    before.unshift(link.from);
    if (link.kind === MapLinkKind.Outcome) choices.push({ decision: link.from, outcome: outcomeOfLink(map, link) });
  }

  const after: string[] = [];
  const sequence = (id: string) => leaving(id).find((l) => l.kind === MapLinkKind.Sequence)?.to ?? null;
  let next = chosen.to ?? (chosen.ends ? null : sequence(decision));
  while (next && !seen.has(next)) {
    const current: string = next;
    seen.add(current);
    after.push(current);
    for (const call of leaving(current).filter((l) => l.kind === MapLinkKind.Call && !seen.has(l.to))) {
      seen.add(call.to);
      after.push(call.to);
    }
    next = sequence(current);
    const branches = map.blocks.find((b) => b.id === current)?.outcomes ?? [];
    const safe = branches.findIndex((o) => !o.fails);
    if (safe >= 0) {
      choices.push({ decision: current, outcome: safe });
      next = branches[safe].to ?? (branches[safe].ends ? null : next);
    }
  }
  return { id: `trecho:${decision}:${outcome}`, blocks: [...before, decision, ...after], choices, fails: chosen.fails };
}

// Vizinhos do bloco no caminho. Sem caminho escolhido ("Todos"), vale o primeiro que passa pelo bloco.
export function pathNeighbors(map: FlowMap, chosen: MapPath | null, blockId: string): { path: MapPath | null; previous: string | null; next: string | null } {
  const path = chosen && chosen.blocks.includes(blockId) ? chosen : chosen ? null : (map.paths.find((p) => p.blocks.includes(blockId)) ?? null);
  if (!path) return { path: null, previous: null, next: null };
  const at = path.blocks.indexOf(blockId);
  return { path, previous: path.blocks[at - 1] ?? null, next: path.blocks[at + 1] ?? null };
}
