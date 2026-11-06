import { AiProvider } from '../models';
import { QueryAnalysis } from './query-analysis';

// Sugestão da IA para uma consulta (Fase 41). A IA só propõe: o Rastro confere a reescrita e os
// índices, e a tela só mostra e oferece "Copiar". Nada é aplicado.
export enum ProposalOrigin {
  Agent = 1,
  Provider = 2,
}

export interface QueryRewrite {
  sql: string;
  accepted: boolean;
  refusals: string[];
  after: QueryAnalysis | null;
}

export interface QueryChange {
  what: string;
  why: string;
}

export interface QueryIndexSuggestion {
  table: string;
  columns: string[];
  include: string[];
  unique: boolean;
  reason: string;
  accepted: boolean;
  refusal: string | null;
  createSql: string | null;
}

export interface QueryPlanComparison {
  costBefore: number | null;
  costAfter: number | null;
  problem: string | null;
}

export interface QuerySuggestionReview {
  origin: ProposalOrigin;
  findingsBefore: number;
  rewrite: QueryRewrite | null;
  changes: QueryChange[];
  indexes: QueryIndexSuggestion[];
  plan: QueryPlanComparison | null;
}

export interface QueryAiSuggestion {
  provider: AiProvider;
  review: QuerySuggestionReview;
}

export const PROPOSAL_ORIGIN_LABEL: Record<ProposalOrigin, string> = {
  [ProposalOrigin.Agent]: 'Agente',
  [ProposalOrigin.Provider]: 'IA',
};

const COST_FORMAT = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export function formatCost(cost: number): string {
  return COST_FORMAT.format(cost);
}

export function findingsText(count: number): string {
  return count === 0 ? 'sem achados' : count === 1 ? '1 achado' : `${count} achados`;
}
