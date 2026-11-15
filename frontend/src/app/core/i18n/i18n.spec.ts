import { TestBed } from '@angular/core/testing';
import { parse, Token } from '@messageformat/parser';
import { TranslateService } from '@ngx-translate/core';
import { LanguageService } from './i18n';
import { TRANSLATIONS, provideRastroI18nTesting } from './i18n.testing';
import { UI_LANGUAGES, UI_LANGUAGE_CODE, UiLanguage, languageFromCode, languageFromSystem } from './language';
import { currentLocale, setCurrentLocale } from './locale';

type Tree = { [key: string]: string | Tree };

// Chave pontuada -> texto, para comparar os idiomas folha a folha.
function flatten(tree: Tree, prefix = ''): Map<string, string> {
  const result = new Map<string, string>();
  for (const [key, value] of Object.entries(tree)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (typeof value === 'string') result.set(path, value);
    else for (const [k, v] of flatten(value, path)) result.set(k, v);
  }
  return result;
}

// Marcadores do texto ICU ({name}, {count, plural, ...}), com o tipo de cada um.
function placeholders(text: string): string[] {
  const found = new Set<string>();
  const walk = (tokens: Token[]): void => {
    for (const token of tokens) {
      if (token.type === 'argument') found.add(token.arg);
      else if (token.type === 'function') found.add(`${token.arg}:${token.key}`);
      else if (token.type === 'plural' || token.type === 'select' || token.type === 'selectordinal') {
        found.add(`${token.arg}:${token.type}`);
        for (const c of token.cases) walk(c.tokens);
      }
    }
  };
  walk(parse(text));
  return [...found].sort();
}

const LANGUAGE_FILES = UI_LANGUAGES.map((language) => ({
  code: UI_LANGUAGE_CODE[language],
  keys: flatten(TRANSLATIONS[language] as Tree),
}));

describe('Arquivos de tradução', () => {
  // WEB-I18N-001
  it('têm as mesmas chaves nos três idiomas', () => {
    const [reference, ...others] = LANGUAGE_FILES;
    for (const other of others) {
      const missing = [...reference.keys.keys()].filter((key) => !other.keys.has(key));
      const extra = [...other.keys.keys()].filter((key) => !reference.keys.has(key));
      expect(missing).withContext(`faltam em ${other.code}`).toEqual([]);
      expect(extra).withContext(`sobram em ${other.code}`).toEqual([]);
    }
  });

  // WEB-I18N-002
  it('têm os mesmos marcadores em cada chave, com ICU válido', () => {
    const [reference, ...others] = LANGUAGE_FILES;
    for (const [key, text] of reference.keys) {
      const expected = placeholders(text);
      for (const other of others) {
        const translated = other.keys.get(key);
        if (translated === undefined) continue;
        expect(placeholders(translated)).withContext(`${other.code}: ${key}`).toEqual(expected);
      }
    }
  });

  // WEB-I18N-003
  it('não têm texto vazio', () => {
    for (const file of LANGUAGE_FILES) {
      const empty = [...file.keys].filter(([, text]) => text.trim() === '').map(([key]) => key);
      expect(empty).withContext(file.code).toEqual([]);
    }
  });
});

describe('Idioma inicial', () => {
  // WEB-I18N-004
  it('segue o primeiro idioma do sistema que o Rastro tem', () => {
    expect(languageFromSystem(['pt-BR', 'en-US'])).toBe(UiLanguage.Portuguese);
    expect(languageFromSystem(['pt-PT'])).toBe(UiLanguage.Portuguese);
    expect(languageFromSystem(['es-MX'])).toBe(UiLanguage.Spanish);
    expect(languageFromSystem(['de-DE', 'es-ES'])).toBe(UiLanguage.Spanish);
    expect(languageFromSystem(['en-GB'])).toBe(UiLanguage.English);
  });

  // WEB-I18N-005
  it('cai no inglês quando o sistema está num idioma que o Rastro não tem', () => {
    expect(languageFromSystem(['de-DE', 'fr-FR'])).toBe(UiLanguage.English);
    expect(languageFromSystem([])).toBe(UiLanguage.English);
  });

  // WEB-I18N-006
  it('reconhece só os códigos dos três idiomas', () => {
    expect(languageFromCode('es')).toBe(UiLanguage.Spanish);
    expect(languageFromCode('pt-BR')).toBe(UiLanguage.Portuguese);
    expect(languageFromCode('zh-CN')).toBeNull();
    expect(languageFromCode(null)).toBeNull();
  });
});

describe('LanguageService', () => {
  afterEach(() => setCurrentLocale('pt-BR'));

  // WEB-I18N-007
  it('troca o texto, o lang da página e a formatação na hora', () => {
    TestBed.configureTestingModule({ providers: [provideRastroI18nTesting()] });
    const translate = TestBed.inject(TranslateService);
    const service = TestBed.inject(LanguageService);
    expect(translate.instant('theme.dark')).toBe('Escuro');

    void service.set(UiLanguage.English);

    expect(translate.instant('theme.dark')).toBe('Dark');
    expect(service.t('theme.dark')).toBe('Dark');
    expect(document.documentElement.lang).toBe('en-US');
    expect(currentLocale()).toBe('en-US');
    void service.set(UiLanguage.Portuguese);
    localStorage.removeItem('rastro.language');
  });

  // WEB-I18N-008
  it('formata o marcador ICU do texto', () => {
    TestBed.configureTestingModule({ providers: [provideRastroI18nTesting(UiLanguage.Spanish)] });
    const translate = TestBed.inject(TranslateService);
    expect(translate.instant('settings.appearance.paletteApplied', { name: 'Papel' })).toBe('Paleta aplicada: Papel.');
  });
});
