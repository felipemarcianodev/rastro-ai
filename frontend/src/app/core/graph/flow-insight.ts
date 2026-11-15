import { tr } from '../i18n/locale';
import { AnswerSeal, QuestionRole, RuleEffect } from '../models';

// Rótulos, letras e classes das regras (Fase 32) e das respostas (Fase 33). Letra e cor, nunca só cor.

export const RULE_EFFECT_LABEL_KEY: Record<RuleEffect, string> = {
  [RuleEffect.Blocks]: 'graph.flowInsight.ruleEffect.blocks',
  [RuleEffect.Allows]: 'graph.flowInsight.ruleEffect.allows',
  [RuleEffect.SideEffect]: 'graph.flowInsight.ruleEffect.sideEffect',
};

export const RULE_EFFECT_GLYPH: Record<RuleEffect, string> = {
  [RuleEffect.Blocks]: 'B',
  [RuleEffect.Allows]: 'P',
  [RuleEffect.SideEffect]: 'E',
};

export const RULE_EFFECT_SLUG: Record<RuleEffect, string> = {
  [RuleEffect.Blocks]: 'blocks',
  [RuleEffect.Allows]: 'allows',
  [RuleEffect.SideEffect]: 'side-effect',
};

export const QUESTION_ROLE_LABEL_KEY: Record<QuestionRole, string> = {
  [QuestionRole.TechLead]: 'graph.flowInsight.questionRole.techLead',
  [QuestionRole.Staff]: 'graph.flowInsight.questionRole.staff',
  [QuestionRole.Manager]: 'graph.flowInsight.questionRole.manager',
};

export const ANSWER_SEAL_LABEL_KEY: Record<AnswerSeal, string> = {
  [AnswerSeal.Extracted]: 'graph.flowInsight.answerSeal.extracted',
  [AnswerSeal.Inferred]: 'graph.flowInsight.answerSeal.inferred',
  [AnswerSeal.Outside]: 'graph.flowInsight.answerSeal.outside',
  [AnswerSeal.None]: 'graph.flowInsight.answerSeal.none',
};

export const ANSWER_SEAL_GLYPH: Record<AnswerSeal, string> = {
  [AnswerSeal.Extracted]: 'C',
  [AnswerSeal.Inferred]: 'IA',
  [AnswerSeal.Outside]: '↗',
  [AnswerSeal.None]: '∅',
};

export const ANSWER_SEAL_SLUG: Record<AnswerSeal, string> = {
  [AnswerSeal.Extracted]: 'extracted',
  [AnswerSeal.Inferred]: 'inferred',
  [AnswerSeal.Outside]: 'outside',
  [AnswerSeal.None]: 'none',
};

// Filtro de uma lista: null é "todos".
export function filterBy<T, K>(items: readonly T[], key: (item: T) => K, chosen: K | null): T[] {
  return chosen === null ? [...items] : items.filter((item) => key(item) === chosen);
}

// Campos que a chamada envia (Fase 34): "id, valor e outros campos"; sem campo lido, só o aviso de que há mais.
export function fieldsText(http: { fields?: string[] | null; moreFields?: boolean } | null | undefined): string | null {
  const fields = http?.fields ?? [];
  if (fields.length === 0) return http?.moreFields ? tr('graph.flowInsight.moreFields') : null;
  return http?.moreFields ? tr('graph.flowInsight.fieldsAndMore', { fields: fields.join(', ') }) : fields.join(', ');
}
