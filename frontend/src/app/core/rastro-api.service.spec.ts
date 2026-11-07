import { HttpErrorResponse } from '@angular/common/http';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RastroApi, apiErrorMessage } from './rastro-api.service';

describe('RastroApi: comandos do repositório', () => {
  // UX-FIM-C01
  it('setRepositoryCommands faz PUT em /commands com o nome codificado e os dois comandos', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const api = TestBed.inject(RastroApi);
    const http = TestBed.inject(HttpTestingController);
    let result: unknown;
    api.setRepositoryCommands('meu sistema', 'dotnet build', null).subscribe((r) => (result = r));

    const req = http.expectOne('/api/repositories/meu%20sistema/commands');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ buildCommand: 'dotnet build', testCommand: null });
    req.flush({ name: 'meu sistema' });
    expect(result).toEqual({ name: 'meu sistema' });
  });
});

describe('apiErrorMessage', () => {
  // Fase 17: a tela é do cliente, que não sabe o que é "a API"; ele sabe que rodou o comando rastro.
  it('sem resposta, fala do Rastro e do comando, nunca da API', () => {
    const message = apiErrorMessage(new HttpErrorResponse({ status: 0 }));

    expect(message).toContain('O Rastro não respondeu');
    expect(message).toContain('rastro');
    expect(message).not.toContain('API');
  });

  it('erro sem código conhecido também não cita a API', () => {
    expect(apiErrorMessage(new HttpErrorResponse({ status: 500 }))).not.toContain('API');
  });

  it('erro com código estável usa a mensagem do código', () => {
    const error = new HttpErrorResponse({ status: 409, error: { code: 'REPOSITORY_EXISTS' } });

    expect(apiErrorMessage(error)).toBe('Já existe um repositório com esse nome.');
  });

  // UX-DBA-06
  it('AI_PROVIDER_ERROR mostra o motivo do title (chave inválida, limite) e, sem ele, a mensagem do código', () => {
    const withTitle = new HttpErrorResponse({ status: 502, error: { code: 'AI_PROVIDER_ERROR', title: 'A chave de API foi recusada pelo provedor.' } });
    const withoutTitle = new HttpErrorResponse({ status: 502, error: { code: 'AI_PROVIDER_ERROR' } });

    expect(apiErrorMessage(withTitle)).toBe('A chave de API foi recusada pelo provedor.');
    expect(apiErrorMessage(withoutTitle)).toBe('O provedor de IA não respondeu. Tente de novo em instantes.');
  });
});
