// Banco vivo (Fase 40b, ADR-006): a conexão só leitura do repositório e o plano estimado pelo banco.
// A string de conexão nunca volta da API: a tela só vê o servidor e o banco.

export interface LiveConnectionView {
  configured: boolean;
  // Data Source, sem usuário nem senha.
  server: string | null;
  // Initial Catalog.
  database: string | null;
  updatedAt: string | null;
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

// Operadores como o SQL Server escreve, em pt-BR. O desconhecido fica como veio.
export const PLAN_OPERATOR_LABEL: Record<string, string> = {
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

export const LIVE_PERMISSION_GRANTS = 'GRANT VIEW DEFINITION TO usuario';
export const LIVE_PERMISSION_SHOWPLAN = 'GRANT SHOWPLAN TO usuario';
// O SQL Server só mostra o plano a quem pode rodar a instrução: o SELECT nas tabelas da consulta.
export const LIVE_PERMISSION_SELECT = 'GRANT SELECT ON SCHEMA::dbo TO usuario';
