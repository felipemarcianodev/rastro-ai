import { DiagramColors } from '../mermaid-render';
import { Cardinality, DbRelation, DbTable, columnKeys, keyMark, tableKey } from './db-schema';

// Mapa relacional (Fase 38): as tabelas em colunas pela FK (quem é apontado fica à esquerda, quem
// aponta à direita) e as ligações saindo da linha da coluna da FK até a chave da tabela de destino.
// Determinístico: o mesmo esquema dá o mesmo desenho.

export const DB_CARD = { width: 272, header: 34, row: 22, padding: 8 } as const;
// Colunas além disso ficam num "+N campos" (o painel mostra todas).
export const DB_CARD_MAX_ROWS = 12;
const GAP_X = 96;
const GAP_Y = 28;
const MARGIN = 24;
// Uma coluna do desenho com mais tabelas que isso continua na coluna do lado.
const MAX_PER_COLUMN = 8;

export interface DbCardRow {
  name: string;
  // Tipo curto para o bloco (varchar(120), timestamptz); o completo fica em fullType, no title e no painel.
  type: string;
  fullType: string;
  mark: string;
  personal: boolean;
  // As fontes divergem no tipo ou no nulo: o Rastro não escolhe.
  divergent: boolean;
}

export interface DbCard {
  key: string;
  table: DbTable;
  x: number;
  y: number;
  height: number;
  rows: DbCardRow[];
  more: number;
}

export interface DbLink {
  index: number;
  relation: DbRelation;
  d: string;
  labelX: number;
  labelY: number;
  text: string;
}

export interface DbMapGeometry {
  width: number;
  height: number;
  cards: DbCard[];
  links: DbLink[];
}

// Apelidos curtos dos tipos que o PostgreSQL escreve por extenso. O resto passa como está,
// só sem o espaço depois da vírgula (numeric(10, 2) vira numeric(10,2)).
const TYPE_SHORT: readonly (readonly [RegExp, string])[] = [
  [/^character varying/i, 'varchar'],
  [/^character(?!\s*varying)/i, 'char'],
  [/^timestamp with time zone/i, 'timestamptz'],
  [/^timestamp without time zone/i, 'timestamp'],
  [/^time with time zone/i, 'timetz'],
  [/^time without time zone/i, 'time'],
  [/^double precision/i, 'float8'],
];

export function shortType(type: string): string {
  let out = type.trim();
  for (const [pattern, short] of TYPE_SHORT) {
    if (pattern.test(out)) {
      out = out.replace(pattern, short);
      break;
    }
  }
  return out.replace(/,\s+/g, ',');
}

function cardHeight(table: DbTable): number {
  const rows = Math.min(table.columns.length, DB_CARD_MAX_ROWS) + (table.columns.length > DB_CARD_MAX_ROWS ? 1 : 0);
  return DB_CARD.header + Math.max(rows, 1) * DB_CARD.row + DB_CARD.padding;
}

// Nível pela FK: tabela que não aponta para ninguém fica no 0; as outras, um a mais que o maior
// nível das tabelas para onde apontam. Um ciclo é cortado onde fecha.
function levels(tables: readonly DbTable[], relations: readonly DbRelation[]): Map<string, number> {
  const keys = new Set(tables.map(tableKey));
  const out = new Map<string, string[]>();
  for (const r of relations) {
    if (r.from === r.to || !keys.has(r.from) || !keys.has(r.to)) continue;
    out.set(r.from, [...(out.get(r.from) ?? []), r.to]);
  }
  const level = new Map<string, number>();
  const visiting = new Set<string>();
  const visit = (key: string): number => {
    const known = level.get(key);
    if (known !== undefined) return known;
    if (visiting.has(key)) return 0;
    visiting.add(key);
    const value = Math.max(-1, ...(out.get(key) ?? []).map(visit)) + 1;
    visiting.delete(key);
    level.set(key, value);
    return value;
  };
  for (const key of keys) visit(key);
  return level;
}

export function dbMapGeometry(tables: readonly DbTable[], relations: readonly DbRelation[]): DbMapGeometry | null {
  if (tables.length === 0) return null;
  const level = levels(tables, relations);
  const byKey = new Map(tables.map((t) => [tableKey(t), t]));
  const shown = relations.filter((r) => byKey.has(r.from) && byKey.has(r.to));

  // Cada nível na ordem que deixa a tabela perto de quem ela aponta (média da posição dos destinos).
  const position = new Map<string, number>();
  const columns: DbTable[][] = [];
  const maxLevel = Math.max(...level.values());
  if (shown.length === 0) {
    // Sem nenhuma FK entre as tabelas, não há nível a respeitar: grade, em ordem alfabética.
    const sorted = [...tables].sort((a, b) => tableKey(a).localeCompare(tableKey(b)));
    const perColumn = Math.ceil(sorted.length / Math.ceil(Math.sqrt(sorted.length)));
    for (let i = 0; i < sorted.length; i += perColumn) columns.push(sorted.slice(i, i + perColumn));
  } else for (let l = 0; l <= maxLevel; l++) {
    const here = tables.filter((t) => level.get(tableKey(t)) === l);
    const weight = (t: DbTable) => {
      const targets = shown.filter((r) => r.from === tableKey(t) && position.has(r.to)).map((r) => position.get(r.to)!);
      return targets.length > 0 ? targets.reduce((a, b) => a + b, 0) / targets.length : Number.MAX_SAFE_INTEGER;
    };
    const ordered = [...here].sort((a, b) => weight(a) - weight(b) || tableKey(a).localeCompare(tableKey(b)));
    ordered.forEach((t, i) => position.set(tableKey(t), i));
    for (let i = 0; i < ordered.length; i += MAX_PER_COLUMN) columns.push(ordered.slice(i, i + MAX_PER_COLUMN));
  }

  const cards: DbCard[] = [];
  let height = 0;
  columns.forEach((column, c) => {
    let y = MARGIN;
    for (const table of column) {
      const h = cardHeight(table);
      const rows = table.columns.slice(0, DB_CARD_MAX_ROWS).map((col) => ({
        name: col.name,
        type: col.type == null ? 'divergente' : shortType(col.type),
        fullType: col.type ?? 'divergente',
        mark: keyMark(columnKeys(table, col.name)),
        personal: col.personal,
        divergent: col.type == null || col.nullable == null,
      }));
      cards.push({ key: tableKey(table), table, x: MARGIN + c * (DB_CARD.width + GAP_X), y, height: h, rows, more: Math.max(0, table.columns.length - DB_CARD_MAX_ROWS) });
      y += h + GAP_Y;
    }
    height = Math.max(height, y - GAP_Y + MARGIN);
  });
  const width = MARGIN * 2 + columns.length * DB_CARD.width + (columns.length - 1) * GAP_X;
  const cardOf = new Map(cards.map((card) => [card.key, card]));

  const links = shown.map((relation, index) => {
    const from = cardOf.get(relation.from)!;
    const to = cardOf.get(relation.to)!;
    const y1 = rowY(from, relation.fk.columns[0]);
    const y2 = rowY(to, relation.fk.principalColumns[0] ?? to.table.primaryKey?.columns[0]);
    // Ligações que dividem o mesmo corredor descolam um pouco uma da outra.
    const offset = ((index % 5) - 2) * 6;
    let d: string;
    let labelX: number;
    if (to.x + DB_CARD.width < from.x) {
      const mid = to.x + DB_CARD.width + GAP_X / 2 + offset;
      d = `M${from.x} ${y1} H${mid} V${y2} H${to.x + DB_CARD.width}`;
      labelX = mid;
    } else if (from.x + DB_CARD.width < to.x) {
      const mid = from.x + DB_CARD.width + GAP_X / 2 + offset;
      d = `M${from.x + DB_CARD.width} ${y1} H${mid} V${y2} H${to.x}`;
      labelX = mid;
    } else {
      // Mesma coluna (ou a própria tabela): a volta sai e entra pela direita.
      const out = from.x + DB_CARD.width + 24 + Math.abs(offset);
      d = `M${from.x + DB_CARD.width} ${y1} H${out} V${y2} H${to.x + DB_CARD.width}`;
      labelX = out;
    }
    const leftIsTarget = to.x <= from.x;
    const many = relation.cardinality === Cardinality.ManyToOne ? 'N' : '1';
    return { index, relation, d, labelX, labelY: (y1 + y2) / 2, text: leftIsTarget ? `1 : ${many}` : `${many} : 1` };
  });

  return { width, height, cards, links };
}

// Centro da linha da coluna no bloco; coluna escondida no "+N" ou desconhecida vai para o cabeçalho.
function rowY(card: DbCard, column: string | undefined): number {
  const index = column ? card.rows.findIndex((r) => r.name.toLowerCase() === column.toLowerCase()) : -1;
  return index < 0 ? card.y + DB_CARD.header / 2 : card.y + DB_CARD.header + index * DB_CARD.row + DB_CARD.row / 2;
}

// O mapa como está na tela, em SVG puro (para baixar e para virar PNG).
export function dbMapSvg(geometry: DbMapGeometry, colors: DiagramColors, badges: ReadonlyMap<string, string> = new Map()): string {
  const { width: w, header, row } = DB_CARD;
  const links = geometry.links.map(
    (l) =>
      `<path d="${l.d}" fill="none" stroke="${colors.edge}" stroke-width="1.5" marker-end="url(#db-arrow)"/>` +
      `<rect x="${l.labelX - 20}" y="${l.labelY - 9}" width="40" height="16" rx="3" fill="${colors.paper}"/>` +
      `<text x="${l.labelX}" y="${l.labelY + 3}" font-size="11" text-anchor="middle" fill="${colors.inkSoft}">${xml(l.text)}</text>`,
  );
  const cards = geometry.cards.map((card) => {
    const badge = badges.get(card.key);
    const rows = card.rows.map((r, i) => {
      const y = card.y + header + i * row + row / 2 + 4;
      return (
        `<text x="${card.x + 10}" y="${y}" font-size="10" font-weight="600" fill="${colors.inkSoft}">${xml(r.mark)}</text>` +
        `<text x="${card.x + 50}" y="${y}" font-size="12" fill="${colors.ink}">${xml(r.name)}${r.personal ? ' *' : ''}</text>` +
        `<text x="${card.x + w - 10}" y="${y}" font-size="11" text-anchor="end" fill="${colors.inkSoft}">${xml(clip(r.type, 16))}</text>`
      );
    });
    const more = card.more > 0 ? `<text x="${card.x + 50}" y="${card.y + header + card.rows.length * row + row / 2 + 4}" font-size="11" fill="${colors.inkSoft}">+${card.more} campos</text>` : '';
    return (
      `<g><rect x="${card.x}" y="${card.y}" width="${w}" height="${card.height}" rx="8" fill="${colors.surface}" stroke="${colors.lineStrong}"/>` +
      `<rect x="${card.x}" y="${card.y}" width="${w}" height="${header}" rx="8" fill="${colors.surfaceSunk}"/>` +
      `<text x="${card.x + 10}" y="${card.y + 22}" font-size="13" font-weight="600" fill="${colors.ink}">${xml(clip(card.key, badge ? 18 : 28))}</text>` +
      (badge ? `<text x="${card.x + w - 10}" y="${card.y + 22}" font-size="10" font-weight="600" text-anchor="end" fill="${colors.inkSoft}">${xml(badge)}</text>` : '') +
      rows.join('') +
      more +
      `</g>`
    );
  });
  return (
    `<svg xmlns="http://www.w3.org/2000/svg" width="${geometry.width}" height="${geometry.height}" viewBox="0 0 ${geometry.width} ${geometry.height}" font-family="JetBrains Mono, Cascadia Code, Consolas, monospace">` +
    `<defs><marker id="db-arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0 0 L10 5 L0 10 z" fill="${colors.edge}"/></marker></defs>` +
    `<rect width="100%" height="100%" fill="${colors.paper}"/>` +
    links.join('') +
    cards.join('') +
    `</svg>`
  );
}

function clip(text: string, max: number): string {
  return text.length > max ? text.slice(0, max - 1) + '…' : text;
}

function xml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
