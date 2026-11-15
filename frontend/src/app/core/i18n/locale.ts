// Idioma ativo para a formatação de data e número fora da injeção (funções puras como shortDate).
// Quem muda é só o LanguageService; o valor inicial é o dos testes, que rodam em português.
let activeLocale = 'pt-BR';

export function currentLocale(): string {
  return activeLocale;
}

export function setCurrentLocale(code: string): void {
  activeLocale = code;
}
