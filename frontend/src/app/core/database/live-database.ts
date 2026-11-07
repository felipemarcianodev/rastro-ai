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

// Operadores como o SQL Server e o PostgreSQL (Fase 42b) escrevem, em pt-BR. O desconhecido fica como veio.
export const PLAN_OPERATOR_LABEL: Record<string, string> = {
  'Seq Scan': 'Varredura da tabela',
  'Index Only Scan': 'Busca só no índice',
  'Bitmap Heap Scan': 'Leitura da tabela pelo mapa do índice',
  'Bitmap Index Scan': 'Busca no índice (mapa)',
  'Nested Loop': 'Laços aninhados',
  'Hash Join': 'Junção por hash',
  Hash: 'Tabela de hash',
  Aggregate: 'Agregação',
  HashAggregate: 'Agregação por hash',
  GroupAggregate: 'Agregação por grupo',
  Limit: 'Limite',
  ModifyTable: 'Alteração da tabela',
  Materialize: 'Materialização',
  Gather: 'Junção dos processos paralelos',
  'Gather Merge': 'Junção ordenada dos processos paralelos',
  Unique: 'Remoção de repetidos',
  'Subquery Scan': 'Leitura da subconsulta',
  'CTE Scan': 'Leitura do CTE',
  Result: 'Resultado',
  'Incremental Sort': 'Ordenação incremental',
  'Clustered Index Scan': 'Varredura do índice clustered',
  'Index Scan': 'Varredura do índice',
  'Table Scan': 'Varredura da tabela',
  'Index Seek': 'Busca no índice',
  'Clustered Index Seek': 'Busca no índice clustered',
  Sort: 'Ordenação',
  'Hash Match': 'Junção por hash',
  'Nested Loops': 'Laços aninhados',
  'Merge Join': 'Junção por mesclagem',
  'Key Lookup': 'Busca da chave',
};

export function operatorLabel(physicalOp: string): string {
  return Object.prototype.hasOwnProperty.call(PLAN_OPERATOR_LABEL, physicalOp) ? PLAN_OPERATOR_LABEL[physicalOp] : physicalOp;
}

// "O próprio banco aponta índice faltando em pedidos (criado_em), ganho estimado de 87%".
export function missingIndexText(index: MissingIndex): string {
  const columns = [...index.equality, ...index.inequality].join(', ');
  return `O próprio banco aponta índice faltando em ${index.table}${columns ? ` (${columns})` : ''}, ganho estimado de ${Math.round(index.impact)}%`;
}

export const PLAN_SHOWPLAN_NOTE = 'SET SHOWPLAN_XML: o banco diz como faria, sem executar a consulta. Parâmetros (@p0) entram como texto.';

// Como o plano é pedido, por dialeto (Fase 42b).
export const PLAN_NOTE: Record<SqlDialect, string> = {
  [SqlDialect.SqlServer]: PLAN_SHOWPLAN_NOTE,
  [SqlDialect.PostgreSql]: 'EXPLAIN sem ANALYZE, numa conexão só de leitura: o banco diz como faria, sem executar a consulta. Com parâmetro ($1, @nome), o plano é o genérico, que não depende do valor.',
};

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
