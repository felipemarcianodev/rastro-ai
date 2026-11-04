import { AnswerSeal, QuestionRole, RuleEffect } from '../models';

// Rótulos, letras e classes das regras (Fase 32) e das respostas (Fase 33). Letra e cor, nunca só cor.

export const RULE_EFFECT_LABEL: Record<RuleEffect, string> = {
  [RuleEffect.Blocks]: 'Bloqueia',
  [RuleEffect.Allows]: 'Permite',
  [RuleEffect.SideEffect]: 'Efeito colateral',
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

export const QUESTION_ROLE_LABEL: Record<QuestionRole, string> = {
  [QuestionRole.TechLead]: 'TL',
  [QuestionRole.Staff]: 'Staff',
  [QuestionRole.Manager]: 'Gerente',
};

export const ANSWER_SEAL_LABEL: Record<AnswerSeal, string> = {
  [AnswerSeal.Extracted]: 'Extraída do código',
  [AnswerSeal.Inferred]: 'Inferida pela IA',
  [AnswerSeal.Outside]: 'Fora do código levantado',
  [AnswerSeal.None]: 'Sem resposta',
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

export const MORE_FIELDS_TEXT = 'e outros campos';

// Campos que a chamada envia (Fase 34): "id, valor e outros campos"; sem campo lido, só o aviso de que há mais.
export function fieldsText(http: { fields?: string[] | null; moreFields?: boolean } | null | undefined): string | null {
  const fields = http?.fields ?? [];
  if (fields.length === 0) return http?.moreFields ? 'outros campos' : null;
  return http?.moreFields ? `${fields.join(', ')} ${MORE_FIELDS_TEXT}` : fields.join(', ');
}
