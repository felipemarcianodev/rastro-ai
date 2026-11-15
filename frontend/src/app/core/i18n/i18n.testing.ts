import { EnvironmentProviders, Injectable, Provider, inject, provideEnvironmentInitializer } from '@angular/core';
import { TranslateLoader, TranslationObject } from '@ngx-translate/core';
import { Observable, of } from 'rxjs';
import enUS from '../../../i18n/en-US.json';
import es from '../../../i18n/es.json';
import ptBR from '../../../i18n/pt-BR.json';
import MessageFormat, { MessageFunction } from '@messageformat/core';
import { LanguageService, translateProviders } from './i18n';
import { FALLBACK_LANGUAGE, UI_LANGUAGE_CODE, UiLanguage, languageFromCode } from './language';
import { registerTranslator, setCurrentLocale } from './locale';

// Só para os testes: os três idiomas carregados de uma vez, sem espera.
export const TRANSLATIONS: Record<UiLanguage, TranslationObject> = {
  [UiLanguage.English]: enUS,
  [UiLanguage.Portuguese]: ptBR,
  [UiLanguage.Spanish]: es,
};

@Injectable()
class StaticTranslateLoader implements TranslateLoader {
  getTranslation(code: string): Observable<TranslationObject> {
    return of(TRANSLATIONS[languageFromCode(code) ?? FALLBACK_LANGUAGE]);
  }
}

// Português por padrão, para os textos que os testes já conferem continuarem valendo.
// Tradutor sem injeção, para teste de função pura que não sobe nenhum componente: cada teste
// começa com ele em português, e o LanguageService de um teste com TestBed passa por cima.
export function registerStaticTranslator(language = UiLanguage.Portuguese): void {
  const code = UI_LANGUAGE_CODE[language];
  const formatter = new MessageFormat(code);
  const cache = new Map<string, MessageFunction<'string'>>();
  setCurrentLocale(code);
  registerTranslator((key, params) => {
    let message = cache.get(key);
    if (!message) {
      const text = key.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], TRANSLATIONS[language]);
      if (typeof text !== 'string') return key;
      message = formatter.compile(text);
      cache.set(key, message);
    }
    return message(params ?? {});
  });
}

// O LanguageService sobe junto, para tr() e currentLocale() funcionarem em funções puras.
export function provideRastroI18nTesting(language = UiLanguage.Portuguese): (Provider | EnvironmentProviders)[] {
  return [
    translateProviders(StaticTranslateLoader, language),
    provideEnvironmentInitializer(() => {
      inject(LanguageService);
    }),
  ];
}
