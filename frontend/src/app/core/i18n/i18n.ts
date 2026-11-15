import { HttpInterceptorFn } from '@angular/common/http';
import { EnvironmentProviders, Injectable, Provider, Type, inject, makeEnvironmentProviders, provideAppInitializer, signal } from '@angular/core';
import {
  TranslateLoader,
  TranslateService,
  TranslationObject,
  provideTranslateCompiler,
  provideTranslateLoader,
  provideTranslateService,
} from '@ngx-translate/core';
import { TranslateMessageFormatCompiler } from 'ngx-translate-messageformat-compiler';
import { Observable, catchError, firstValueFrom, from, map, of, switchMap } from 'rxjs';
import { LanguageApi } from './language-api.service';
import { FALLBACK_LANGUAGE, UI_LANGUAGE_CODE, UiLanguage, languageFromCode, languageFromSystem } from './language';
import { registerTranslator, setCurrentLocale } from './locale';

// Cada idioma é um pedaço próprio do pacote da tela, servido pela API local: a tela carrega só
// o idioma ativo e nada sai da máquina.
const TRANSLATION_CHUNKS: Record<UiLanguage, () => Promise<{ default: TranslationObject }>> = {
  [UiLanguage.English]: () => import('../../../i18n/en-US.json'),
  [UiLanguage.Portuguese]: () => import('../../../i18n/pt-BR.json'),
  [UiLanguage.Spanish]: () => import('../../../i18n/es.json'),
};

const LANGUAGE_KEY = 'rastro.language';

@Injectable()
class ChunkTranslateLoader implements TranslateLoader {
  getTranslation(code: string): Observable<TranslationObject> {
    const language = languageFromCode(code) ?? FALLBACK_LANGUAGE;
    return from(TRANSLATION_CHUNKS[language]()).pipe(map((chunk) => chunk.default));
  }
}

// Idioma escolhido antes neste navegador; senão, o do sistema operacional (ADR-009).
export function initialLanguage(): UiLanguage {
  const stored = languageFromCode(read(LANGUAGE_KEY));
  if (stored !== null) return stored;
  return languageFromSystem(typeof navigator === 'undefined' ? [] : navigator.languages ?? [navigator.language]);
}

@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly translate = inject(TranslateService);
  private readonly api = inject(LanguageApi);
  readonly language = signal<UiLanguage>(languageFromCode(this.translate.getCurrentLang()) ?? FALLBACK_LANGUAGE);
  // A troca vale na tela mesmo quando o servidor não grava; este aviso diz que a escolha não ficou no config.json.
  readonly saveFailed = signal(false);

  constructor() {
    this.apply(this.language());
  }

  code(): string {
    return UI_LANGUAGE_CODE[this.language()];
  }

  // O sinal só muda quando os textos do idioma novo já chegaram: quem lê t() num computed nunca vê a chave.
  // A escolha também é gravada no servidor (config.json), que vale para a CLI e o MCP. A falha ao gravar
  // não desfaz a troca na tela: só liga o aviso.
  set(language: UiLanguage): Promise<void> {
    return this.switchTo(language).then(() => this.save(language));
  }

  // Idioma que veio do servidor na abertura: troca a tela sem gravar de volta.
  applyFromServer(language: UiLanguage): Promise<void> {
    return this.switchTo(language);
  }

  private switchTo(language: UiLanguage): Promise<void> {
    store(LANGUAGE_KEY, UI_LANGUAGE_CODE[language]);
    return new Promise((resolve) => {
      this.translate.use(UI_LANGUAGE_CODE[language]).subscribe(() => {
        this.apply(language);
        this.language.set(language);
        resolve();
      });
    });
  }

  private save(language: UiLanguage): void {
    if (!this.api.available) return;
    this.saveFailed.set(false);
    this.api.set(language).subscribe({ error: () => this.saveFailed.set(true) });
  }

  // Texto traduzido para montar no TypeScript. Dentro de computed, refaz o texto quando o idioma muda.
  t(key: string, params?: Record<string, unknown>): string {
    this.language();
    return this.translate.instant(key, params) as string;
  }

  private apply(language: UiLanguage): void {
    const code = UI_LANGUAGE_CODE[language];
    setCurrentLocale(code);
    registerTranslator((key, params) => this.translate.instant(key, params) as string);
    if (typeof document !== 'undefined') document.documentElement.lang = code;
  }
}

// A API responde no idioma da tela (mensagens e prosa gerada).
export const languageInterceptor: HttpInterceptorFn = (request, next) => {
  const code = inject(TranslateService).getCurrentLang();
  return next(code ? request.clone({ setHeaders: { 'Accept-Language': code } }) : request);
};

export function translateProviders(loader: Type<TranslateLoader>, language: UiLanguage): Provider[] {
  return provideTranslateService({
    loader: provideTranslateLoader(loader),
    compiler: provideTranslateCompiler(TranslateMessageFormatCompiler),
    lang: UI_LANGUAGE_CODE[language],
  });
}

// O idioma gravado no servidor vale sobre o atalho do navegador (localStorage). API fora do ar não impede a tela.
export function syncWithServer(service: LanguageService, api: LanguageApi, current: UiLanguage): Observable<void> {
  if (!api.available) return of(undefined);
  return api.get().pipe(
    catchError(() => of(null)),
    switchMap((saved) => (saved?.language != null && saved.language !== current ? from(service.applyFromServer(saved.language)) : of(undefined))),
  );
}

// A tela só desenha depois que o idioma ativo chegou: nenhuma chave aparece no lugar do texto.
export function provideRastroI18n(): EnvironmentProviders {
  const language = initialLanguage();
  return makeEnvironmentProviders([
    translateProviders(ChunkTranslateLoader, language),
    provideAppInitializer(() => {
      const service = inject(LanguageService);
      const api = inject(LanguageApi);
      return firstValueFrom(
        inject(TranslateService)
          .use(UI_LANGUAGE_CODE[language])
          .pipe(switchMap(() => syncWithServer(service, api, language))),
        { defaultValue: undefined },
      );
    }),
  ]);
}

function read(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function store(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Armazenamento bloqueado (aba privada): a escolha vale só nesta visita.
  }
}
