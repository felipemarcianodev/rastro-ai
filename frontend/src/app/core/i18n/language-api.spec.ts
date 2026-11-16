import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { LanguageService, syncWithServer } from './i18n';
import { LanguageApi } from './language-api.service';
import { UI_LANGUAGE_CODE, UiLanguage } from './language';

const URL = '/api/settings/language';

describe('Idioma gravado no servidor', () => {
  let http: HttpTestingController;
  let service: LanguageService;
  let api: LanguageApi;

  beforeEach(() => {
    localStorage.removeItem('rastro.language');
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(LanguageService);
    api = TestBed.inject(LanguageApi);
  });

  afterEach(() => localStorage.removeItem('rastro.language'));

  // WEB-I18N-020
  it('na abertura, usa o idioma gravado quando é diferente do atual', async () => {
    const done = new Promise<void>((resolve) => syncWithServer(service, api, UiLanguage.Portuguese).subscribe(() => resolve()));

    http.expectOne(URL).flush({ language: UiLanguage.Spanish, effective: UiLanguage.Spanish });
    await done;

    expect(service.language()).toBe(UiLanguage.Spanish);
    expect(localStorage.getItem('rastro.language')).toBe(UI_LANGUAGE_CODE[UiLanguage.Spanish]);
    http.expectNone(URL);
  });

  // WEB-I18N-021
  it('na abertura, sem idioma gravado ou com a API fora do ar, mantém o atual', async () => {
    const first = new Promise<void>((resolve) => syncWithServer(service, api, UiLanguage.Portuguese).subscribe(() => resolve()));
    http.expectOne(URL).flush({ language: null, effective: UiLanguage.English });
    await first;
    expect(service.language()).toBe(UiLanguage.Portuguese);

    const second = new Promise<void>((resolve) => syncWithServer(service, api, UiLanguage.Portuguese).subscribe(() => resolve()));
    http.expectOne(URL).flush(null, { status: 500, statusText: 'Erro' });
    await second;
    expect(service.language()).toBe(UiLanguage.Portuguese);
  });

  // WEB-I18N-022
  it('set() troca a tela e grava no servidor com PUT', async () => {
    await service.set(UiLanguage.English);

    const put = http.expectOne(URL);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ language: UiLanguage.English });
    put.flush({ language: UiLanguage.English, effective: UiLanguage.English });
    expect(service.language()).toBe(UiLanguage.English);
    expect(service.saveFailed()).toBeFalse();
  });

  // WEB-I18N-023
  it('falha ao gravar não desfaz a troca na tela e liga o aviso', async () => {
    await service.set(UiLanguage.Spanish);

    http.expectOne(URL).flush(null, { status: 500, statusText: 'Erro' });

    expect(service.language()).toBe(UiLanguage.Spanish);
    expect(service.saveFailed()).toBeTrue();
  });
});
