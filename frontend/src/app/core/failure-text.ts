import { tr } from './i18n/locale';
import {
  DRAFT_ERROR_LABEL_KEY,
  PROPOSAL_REFUSAL_LABEL_KEY,
  ProposalRefusal,
  SURVEY_FAILURE_LABEL_KEY,
  VALIDATION_FAILURE_LABEL_KEY,
  ValidationFailure,
} from './models';

// Texto de uma falha do servidor no idioma ativo (Fase 49): o servidor manda o código e os parâmetros e a tela
// traduz. Sem código (falha gravada antes da Fase 49) ou com código que esta versão da tela não conhece, devolve
// o texto antigo, que o servidor continua mandando em português.

function params(args: readonly string[] | null | undefined): Record<string, string> {
  const result: Record<string, string> = {};
  (args ?? []).forEach((value, index) => (result[`a${index}`] = value));
  return result;
}

function known<T extends number>(labels: Record<T, string>, code: number | null | undefined): code is T {
  return typeof code === 'number' && Object.prototype.hasOwnProperty.call(labels, code);
}

export function surveyFailureText(code: number | null | undefined, args: readonly string[] | null | undefined, fallback?: string | null): string {
  if (!known(SURVEY_FAILURE_LABEL_KEY, code)) return fallback ?? '';
  return tr(SURVEY_FAILURE_LABEL_KEY[code], params(args));
}

// A causa aninhada: o primeiro parâmetro é o código do SurveyFailure e o resto são os parâmetros dele.
function nestedText(args: readonly string[] | null | undefined, fallback?: string | null): string {
  const [code, ...rest] = args ?? [];
  return surveyFailureText(Number(code), rest, fallback);
}

export function validationFailureText(code: number | null | undefined, args: readonly string[] | null | undefined, fallback?: string | null): string {
  if (!known(VALIDATION_FAILURE_LABEL_KEY, code)) return fallback ?? '';
  if (code === ValidationFailure.Analysis) return tr(VALIDATION_FAILURE_LABEL_KEY[code], { reason: nestedText(args, fallback) });
  return tr(VALIDATION_FAILURE_LABEL_KEY[code], params(args));
}

export function refusalText(code: number | null | undefined, args: readonly string[] | null | undefined, fallback?: string | null): string {
  if (!known(PROPOSAL_REFUSAL_LABEL_KEY, code)) return fallback ?? '';
  if (code === ProposalRefusal.AnalysisFailed || code === ProposalRefusal.ProofTargetInvalid)
    return tr(PROPOSAL_REFUSAL_LABEL_KEY[code], { reason: nestedText(args, fallback) });
  return tr(PROPOSAL_REFUSAL_LABEL_KEY[code], params(args));
}

export function draftErrorText(code: number | null | undefined, args: readonly string[] | null | undefined, fallback?: string | null): string {
  if (!known(DRAFT_ERROR_LABEL_KEY, code)) return fallback ?? '';
  return tr(DRAFT_ERROR_LABEL_KEY[code], params(args));
}
