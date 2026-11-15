import { tr } from '../i18n/locale';
import { ProposalOrigin } from '../models';
import { translatedRecord } from './translated';

// Selo curto da origem de uma ligação inferida ou confirmada, desenhado ao lado da marca de
// confiança (UX-G-F02). O provedor de IA vira "IA", o agente vira "MCP" e o lápis é a mão do usuário.
export const EDGE_ORIGIN_MARK: Record<ProposalOrigin, string> = {
  [ProposalOrigin.Manual]: '✎',
  [ProposalOrigin.Agent]: 'MCP',
  // "IA" no português, "AI" no inglês: traduzido na leitura.
  get [ProposalOrigin.Provider]() {
    return tr('enum.proposalOrigin.provider');
  },
};

export const EDGE_ORIGIN_MARK_SLUG: Record<ProposalOrigin, string> = {
  [ProposalOrigin.Manual]: 'manual',
  [ProposalOrigin.Agent]: 'agent',
  [ProposalOrigin.Provider]: 'provider',
};

export const EDGE_ORIGIN_MARK_LABEL_KEY: Record<ProposalOrigin, string> = {
  [ProposalOrigin.Manual]: 'graph.origin.manual',
  [ProposalOrigin.Agent]: 'graph.origin.agent',
  [ProposalOrigin.Provider]: 'graph.origin.provider',
};

// Já traduzido na leitura (quem importa este nome continua lendo texto).
export const EDGE_ORIGIN_MARK_LABEL = translatedRecord(EDGE_ORIGIN_MARK_LABEL_KEY);

// Ordem em que a legenda lista as origens.
export const EDGE_ORIGIN_ORDER: readonly ProposalOrigin[] = [ProposalOrigin.Provider, ProposalOrigin.Agent, ProposalOrigin.Manual];
