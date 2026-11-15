import { translatedRecord } from '../graph/translated';
import { tr } from '../i18n/locale';
import { SqlDialect } from './db-schema';

// Banco vivo (Fase 40b, ADR-006): a conexão só leitura do repositório e o plano estimado pelo banco.
// A string de conexão nunca volta da API: a tela só vê o servidor, o banco e o dialeto (Fase 42b).

export interface LiveConnectionView {
  configured: boolean;
  // Data Source, sem usuário nem senha.
  server: string | null;
  // Initial Catalog.
  database: string | null;
  updatedAt: string | null;
  // Fase 42b: tirado da própria string, ou do repositório quando a string serve aos dois drivers.
  dialect: SqlDialect;
}

export interface LiveConnectionTest {
  ok: boolean;
  // Em pt-BR, pronta para a tela.
  message: string;
}

export interface PlanOperation {
  physicalOp: string;
  logicalOp: string;
  // "pedidos" ou "pedidos.PK_pedidos", quando há.
  object: string | null;
  cost: number;
  // 0 a 100, do custo total da consulta.
  percent: number;
  estimatedRows: number;
  // Varredura de tabela ou de índice.
  scan: boolean;
}

export interface MissingIndex {
  table: string;
  equality: string[];
  inequality: string[];
  include: string[];
  // 0 a 100, o ganho estimado pelo próprio banco.
  impact: number;
}

export interface QueryPlan {
  totalCost: number;
  operations: PlanOperation[];
  missingIndexes: MissingIndex[];
  warnings: string[];
}

// Operadores como o SQL Server e o PostgreSQL (Fase 42b) escrevem; a descrição vem do idioma ativo.
// O desconhecido fica como veio. O nome do operador em si (a chave do mapa) nunca se traduz.
const PLAN_OPERATOR_LABEL_KEY: Record<string, string> = {
  'Seq Scan': 'liveDb.operator.seqScan',
  'Index Only Scan': 'liveDb.operator.indexOnlyScan',
  'Bitmap Heap Scan': 'liveDb.operator.bitmapHeapScan',
  'Bitmap Index Scan': 'liveDb.operator.bitmapIndexScan',
  'Nested Loop': 'liveDb.operator.nestedLoop',
  'Hash Join': 'liveDb.operator.hashJoin',
  'Hash': 'liveDb.operator.hash',
  'Aggregate': 'liveDb.operator.aggregate',
  'HashAggregate': 'liveDb.operator.hashAggregate',
  'GroupAggregate': 'liveDb.operator.groupAggregate',
  'Limit': 'liveDb.operator.limit',
  'ModifyTable': 'liveDb.operator.modifyTable',
  'Materialize': 'liveDb.operator.materialize',
  'Gather': 'liveDb.operator.gather',
  'Gather Merge': 'liveDb.operator.gatherMerge',
  'Unique': 'liveDb.operator.unique',
  'Subquery Scan': 'liveDb.operator.subqueryScan',
  'CTE Scan': 'liveDb.operator.cteScan',
  'Result': 'liveDb.operator.result',
  'Incremental Sort': 'liveDb.operator.incrementalSort',
  'Clustered Index Scan': 'liveDb.operator.clusteredIndexScan',
  'Index Scan': 'liveDb.operator.indexScan',
  'Table Scan': 'liveDb.operator.tableScan',
  'Index Seek': 'liveDb.operator.indexSeek',
  'Clustered Index Seek': 'liveDb.operator.clusteredIndexSeek',
  'Sort': 'liveDb.operator.sort',
  'Hash Match': 'liveDb.operator.hashMatch',
  'Nested Loops': 'liveDb.operator.nestedLoops',
  'Merge Join': 'liveDb.operator.mergeJoin',
  'Key Lookup': 'liveDb.operator.keyLookup',
};

export function operatorLabel(physicalOp: string): string {
  return Object.prototype.hasOwnProperty.call(PLAN_OPERATOR_LABEL_KEY, physicalOp) ? tr(PLAN_OPERATOR_LABEL_KEY[physicalOp]) : physicalOp;
}

// "O próprio banco aponta índice faltando em pedidos (criado_em), ganho estimado de 87%".
export function missingIndexText(index: MissingIndex): string {
  const columns = [...index.equality, ...index.inequality].join(', ');
  return tr(columns ? 'liveDb.missingIndexColumns' : 'liveDb.missingIndex', { table: index.table, columns, impact: Math.round(index.impact) });
}

// Como o plano é pedido, por dialeto (Fase 42b). Lido no idioma ativo.
const PLAN_NOTE_KEY: Record<SqlDialect, string> = {
  [SqlDialect.SqlServer]: 'liveDb.note.sqlServer',
  [SqlDialect.PostgreSql]: 'liveDb.note.postgreSql',
};
export const PLAN_NOTE: Record<SqlDialect, string> = translatedRecord(PLAN_NOTE_KEY);

export const LIVE_PERMISSION_GRANTS = 'GRANT VIEW DEFINITION TO usuario';
export const LIVE_PERMISSION_SHOWPLAN = 'GRANT SHOWPLAN TO usuario';
// O SQL Server só mostra o plano a quem pode rodar a instrução: o SELECT nas tabelas da consulta.
export const LIVE_PERMISSION_SELECT = 'GRANT SELECT ON SCHEMA::dbo TO usuario';

// PostgreSQL (Fase 42b): o catálogo (pg_catalog) se lê só com o CONNECT; o EXPLAIN confere a permissão
// da instrução, então o plano pede o USAGE no schema e o SELECT nas tabelas da consulta.
export const LIVE_PG_PERMISSION_CONNECT = 'GRANT CONNECT ON DATABASE banco TO usuario';
export const LIVE_PG_PERMISSION_USAGE = 'GRANT USAGE ON SCHEMA public TO usuario';
export const LIVE_PG_PERMISSION_SELECT = 'GRANT SELECT ON ALL TABLES IN SCHEMA public TO usuario';
export const LIVE_PG_READ_ONLY = 'ALTER ROLE usuario SET default_transaction_read_only = on';

// A permissão mínima como lista curta, um comando por linha (UX-PGL-05), por dialeto: a mesma lista
// aparece no diálogo e vai inteira para a área de transferência.
export const LIVE_PERMISSION_COMMANDS: Record<SqlDialect, readonly string[]> = {
  [SqlDialect.SqlServer]: [LIVE_PERMISSION_GRANTS, LIVE_PERMISSION_SHOWPLAN, LIVE_PERMISSION_SELECT].map((c) => c + ';'),
  [SqlDialect.PostgreSql]: [LIVE_PG_PERMISSION_CONNECT, LIVE_PG_PERMISSION_USAGE, LIVE_PG_PERMISSION_SELECT, LIVE_PG_READ_ONLY].map((c) => c + ';'),
};

export const LIVE_CONNECTION_PLACEHOLDER: Record<SqlDialect, string> = {
  [SqlDialect.SqlServer]: 'Server=...;Database=...;User Id=...;Password=...',
  [SqlDialect.PostgreSql]: 'Host=...;Database=...;Username=...;Password=...',
};
