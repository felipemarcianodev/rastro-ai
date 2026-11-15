import { signal } from '@angular/core';

// Idioma ativo para quem está fora da injeção (funções puras como shortDate, apiErrorMessage e
// os textos de core/graph). Quem muda é só o LanguageService; o valor inicial é o dos testes.
let activeLocale = 'pt-BR';
let translator: ((key: string, params?: Record<string, unknown>) => string) | null = null;
// Lido por tr(): um computed que passa por tr() se refaz quando o idioma muda.
const languageVersion = signal(0);

export function currentLocale(): string {
  languageVersion();
  return activeLocale;
}

export function setCurrentLocale(code: string): void {
  activeLocale = code;
}

export function registerTranslator(fn: (key: string, params?: Record<string, unknown>) => string): void {
  translator = fn;
  languageVersion.update((v) => v + 1);
}

// Texto traduzido no idioma ativo. Sem o LanguageService de pé, devolve a própria chave.
export function tr(key: string, params?: Record<string, unknown>): string {
  languageVersion();
  return translator ? translator(key, params) : key;
}
