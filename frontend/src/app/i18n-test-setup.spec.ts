import { TestBed } from '@angular/core/testing';
import { provideRastroI18nTesting, registerStaticTranslator } from './core/i18n/i18n.testing';

// Todo teste roda com a tradução em português de verdade: os textos que os testes já conferem
// continuam valendo, e uma chave sem tradução aparece como a própria chave e quebra o teste.
const withTranslation = (): void => {
  TestBed.configureTestingModule({ providers: [provideRastroI18nTesting()] });
};

// Vários specs reiniciam o TestBed no meio do teste para trocar provedores; a tradução volta junto.
const reset = TestBed.resetTestingModule.bind(TestBed);
TestBed.resetTestingModule = () => {
  const result = reset();
  withTranslation();
  return result;
};

// Um beforeEach fora de describe vale para todos os specs (Jasmine). O tradutor estático cobre o
// teste de função pura, que não sobe o LanguageService.
beforeEach(() => {
  registerStaticTranslator();
  withTranslation();
});
