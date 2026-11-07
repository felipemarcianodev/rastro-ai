import { ProposalOrigin } from '../models';

// Selo curto da origem de uma ligação inferida ou confirmada, desenhado ao lado da marca de
// confiança (UX-G-F02). O provedor de IA vira "IA", o agente vira "MCP" e o lápis é a mão do usuário.
export const EDGE_ORIGIN_MARK: Record<ProposalOrigin, string> = {
  [ProposalOrigin.Manual]: '✎',
  [ProposalOrigin.Agent]: 'MCP',
  [ProposalOrigin.Provider]: 'IA',
};

export const EDGE_ORIGIN_MARK_SLUG: Record<ProposalOrigin, string> = {
  [ProposalOrigin.Manual]: 'manual',
  [ProposalOrigin.Agent]: 'agent',
  [ProposalOrigin.Provider]: 'provider',
};

export const EDGE_ORIGIN_MARK_LABEL: Record<ProposalOrigin, string> = {
  [ProposalOrigin.Manual]: 'Ligação feita por você',
  [ProposalOrigin.Agent]: 'Proposta por um agente (MCP)',
  [ProposalOrigin.Provider]: 'Proposta pelo provedor de IA',
};

// Ordem em que a legenda lista as origens.
export const EDGE_ORIGIN_ORDER: readonly ProposalOrigin[] = [ProposalOrigin.Provider, ProposalOrigin.Agent, ProposalOrigin.Manual];
