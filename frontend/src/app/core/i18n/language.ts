// Idiomas do Rastro (ADR-009). O valor numérico é o que viaja para a API.
export enum UiLanguage {
  English = 0,
  Portuguese = 1,
  Spanish = 2,
}

export const UI_LANGUAGES: UiLanguage[] = [UiLanguage.English, UiLanguage.Portuguese, UiLanguage.Spanish];

// Código do arquivo de tradução, do Accept-Language, do <html lang> e do Intl.
export const UI_LANGUAGE_CODE: Record<UiLanguage, string> = {
  [UiLanguage.English]: 'en-US',
  [UiLanguage.Portuguese]: 'pt-BR',
  [UiLanguage.Spanish]: 'es',
};

// Nome de cada idioma nele mesmo: quem não lê o idioma atual acha o seu no seletor.
export const UI_LANGUAGE_LABEL: Record<UiLanguage, string> = {
  [UiLanguage.English]: 'English',
  [UiLanguage.Portuguese]: 'Português',
  [UiLanguage.Spanish]: 'Español',
};

// Reserva quando o sistema está num idioma que o Rastro não tem.
export const FALLBACK_LANGUAGE = UiLanguage.English;

const LANGUAGE_BY_PREFIX: Record<string, UiLanguage> = {
  en: UiLanguage.English,
  pt: UiLanguage.Portuguese,
  es: UiLanguage.Spanish,
};

// O primeiro idioma do sistema que o Rastro tem; senão, o inglês.
export function languageFromSystem(preferred: readonly string[]): UiLanguage {
  for (const tag of preferred) {
    const prefix = tag.toLowerCase().split('-')[0];
    if (prefix in LANGUAGE_BY_PREFIX) return LANGUAGE_BY_PREFIX[prefix];
  }
  return FALLBACK_LANGUAGE;
}

export function languageFromCode(code: string | null | undefined): UiLanguage | null {
  const found = UI_LANGUAGES.find((language) => UI_LANGUAGE_CODE[language] === code);
  return found ?? null;
}
