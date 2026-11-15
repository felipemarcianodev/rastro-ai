import { Injectable, Provider } from '@angular/core';
import { TranslateLoader, TranslationObject } from '@ngx-translate/core';
import { Observable, of } from 'rxjs';
import enUS from '../../../i18n/en-US.json';
import es from '../../../i18n/es.json';
import ptBR from '../../../i18n/pt-BR.json';
import { translateProviders } from './i18n';
import { FALLBACK_LANGUAGE, UiLanguage, languageFromCode } from './language';

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
export function provideRastroI18nTesting(language = UiLanguage.Portuguese): Provider[] {
  return translateProviders(StaticTranslateLoader, language);
}
