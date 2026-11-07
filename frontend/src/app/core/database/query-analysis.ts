import { DbSchema, SqlDialect, tableKey } from './db-schema';

// Análise de consulta pelo código (Fase 40a): regras fixas, sem IA e sem banco. O backend devolve
// os achados com a posição do trecho no texto enviado; a tela marca esses trechos na consulta.
export enum QueryFindingKind {
  SelectStar = 0,
  FunctionOnFilterColumn = 1,
  LeadingWildcardLike = 2,
  OrAcrossColumns = 3,
  ColumnWithoutIndex = 4,
  JoinWithoutCondition = 5,
  DistinctOverJoin = 6,
  CorrelatedSubquery = 7,
  OrderByWithoutLimit = 8,
  NotInNullable = 9,
  WriteWithoutWhere = 10,
  ReadWithoutLimit = 11,
}

// Mesma escala do FindingSeverity: o valor maior é o mais grave. A lista ordena do maior para o menor.
export enum QuerySeverity {
  Low = 0,
  Medium = 1,
  High = 2,
}

export interface QueryFinding {
  kind: QueryFindingKind;
  severity: QuerySeverity;
  start: number;
  length: number;
  line: number;
  snippet: string;
  message: string;
  table: string | null;
}

export interface QueryTableUse {
  name: string;
  alias: string | null;
  inSchema: boolean;
}

export interface QueryParseError {
  message: string;
  line: number;
  column: number;
}

export interface QueryAnalysis {
  dialect: SqlDialect;
  statements: number;
  findings: QueryFinding[];
  tables: QueryTableUse[];
  error: QueryParseError | null;
}

// O valor de ?aba= que abre a análise de consulta na tela do banco; sem ele, abre o mapa.
export const DB_TAB_QUERY_PARAM = 'consulta';

// ?levantamento=<id> na rota do banco: o levantamento de onde o usuário veio. O "←" da tela do banco
// volta a ele; sem o parâmetro, volta à tela inicial. (?origem= é outra coisa: o rótulo do passo da consulta.)
export const DB_SURVEY_ORIGIN_PARAM = 'levantamento';

const EXAMPLE_COLUMNS = 3;
const EXAMPLE_LIMIT = 10;

// Consulta de exemplo no dialeto: usa a primeira tabela do esquema (com até 3 colunas dela) ou, sem
// esquema, nomes genéricos. Já traz o limite, para a análise não começar apontando falta dele.
export function exampleQuery(dialect: SqlDialect, schema: DbSchema | null): string {
  const table = schema?.tables.find((t) => t.columns.length > 0);
  const source = table ? tableKey(table) : 'clientes';
  const columns = table ? table.columns.slice(0, EXAMPLE_COLUMNS).map((c) => c.name) : ['id', 'nome'];
  const list = columns.join(', ');
  return dialect === SqlDialect.PostgreSql
    ? `SELECT ${list}
FROM ${source}
LIMIT ${EXAMPLE_LIMIT};`
    : `SELECT TOP ${EXAMPLE_LIMIT} ${list}
FROM ${source};`;
}

export const QUERY_FINDING_TITLE: Record<QueryFindingKind, string> = {
  [QueryFindingKind.SelectStar]: 'SELECT *',
  [QueryFindingKind.FunctionOnFilterColumn]: 'Função ou conversão na coluna do filtro',
  [QueryFindingKind.LeadingWildcardLike]: 'Curinga no começo do LIKE ou ILIKE',
  [QueryFindingKind.OrAcrossColumns]: 'OR entre colunas diferentes',
  [QueryFindingKind.ColumnWithoutIndex]: 'Coluna sem índice no filtro ou na junção',
  [QueryFindingKind.JoinWithoutCondition]: 'Junção sem condição',
  [QueryFindingKind.DistinctOverJoin]: 'DISTINCT sobre junção',
  [QueryFindingKind.CorrelatedSubquery]: 'Subconsulta correlacionada',
  [QueryFindingKind.OrderByWithoutLimit]: 'ORDER BY sem limite',
  [QueryFindingKind.NotInNullable]: 'NOT IN com coluna que aceita nulo',
  [QueryFindingKind.WriteWithoutWhere]: 'UPDATE ou DELETE sem WHERE',
  [QueryFindingKind.ReadWithoutLimit]: 'Leitura sem limite, filtrada só por igualdade',
};

export const QUERY_SEVERITY_LABEL: Record<QuerySeverity, string> = {
  [QuerySeverity.High]: 'Alta',
  [QuerySeverity.Medium]: 'Média',
  [QuerySeverity.Low]: 'Baixa',
};

export const QUERY_SEVERITY_SLUG: Record<QuerySeverity, string> = {
  [QuerySeverity.High]: 'high',
  [QuerySeverity.Medium]: 'medium',
  [QuerySeverity.Low]: 'low',
};

export interface QuerySegment {
  text: string;
  // Índices (na lista de achados) dos achados que cobrem este pedaço; vazio quando nenhum.
  findings: number[];
}

// Parte o texto nos pedaços em que o conjunto de achados que o cobrem não muda. Trechos que se
// sobrepõem dão pedaços com mais de um índice; start e length fora do texto são cortados.
export function querySegments(sql: string, findings: readonly Pick<QueryFinding, 'start' | 'length'>[]): QuerySegment[] {
  if (sql.length === 0) return [];
  const ranges = findings.map((f) => {
    const start = Math.min(sql.length, Math.max(0, f.start));
    const end = Math.min(sql.length, Math.max(start, f.start + f.length));
    return { start, end };
  });
  const cuts = new Set<number>([0, sql.length]);
  for (const r of ranges) {
    if (r.end > r.start) {
      cuts.add(r.start);
      cuts.add(r.end);
    }
  }
  const points = [...cuts].sort((a, b) => a - b);
  const segments: QuerySegment[] = [];
  for (let i = 0; i < points.length - 1; i++) {
    const from = points[i];
    const to = points[i + 1];
    const covering: number[] = [];
    ranges.forEach((r, index) => {
      if (r.end > r.start && r.start <= from && r.end >= to) covering.push(index);
    });
    segments.push({ text: sql.slice(from, to), findings: covering });
  }
  return segments;
}
