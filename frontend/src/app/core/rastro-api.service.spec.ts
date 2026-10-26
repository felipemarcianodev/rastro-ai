import { HttpErrorResponse } from '@angular/common/http';
import { apiErrorMessage } from './rastro-api.service';

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
});
