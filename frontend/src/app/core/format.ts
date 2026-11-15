import { currentLocale } from './i18n/locale';

// Data curta no idioma ativo (dia/mês hora:minuto).
export function shortDate(iso: string | undefined): string {
  if (!iso) return '';
  return new Date(iso).toLocaleString(currentLocale(), { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
}

export function shortCommit(hash: string | undefined): string {
  return hash ? hash.slice(0, 7) : '';
}

const ZERO_WIDTH_SPACE = '​';

// Identificador de código que pode quebrar linha nos pontos e barras (não no meio do nome).
export function breakable(code: string | undefined): string {
  return (code ?? '').replace(/([./\\])/g, `$1${ZERO_WIDTH_SPACE}`);
}
