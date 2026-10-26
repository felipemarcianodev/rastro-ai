// Data curta em pt-BR (dia/mês hora:minuto).
export function shortDate(iso: string | undefined): string {
  if (!iso) return '';
  return new Date(iso).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
}

export function shortCommit(hash: string | undefined): string {
  return hash ? hash.slice(0, 7) : '';
}

export function plural(count: number, one: string, many: string): string {
  return `${count} ${count === 1 ? one : many}`;
}

const ZERO_WIDTH_SPACE = '​';

// Identificador de código que pode quebrar linha nos pontos e barras (não no meio do nome).
export function breakable(code: string | undefined): string {
  return (code ?? '').replace(/([./\\])/g, `$1${ZERO_WIDTH_SPACE}`);
}
