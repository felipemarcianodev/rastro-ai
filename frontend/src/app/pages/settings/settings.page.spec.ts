import { Location } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { EditorLinks } from '../../core/editor-links';
import { RepositoryItem } from '../../core/models';
import { SettingsPage } from './settings.page';

const repo = (name: string, extra: Partial<RepositoryItem> = {}): RepositoryItem => ({
  name,
  path: `C:\\codigo\\${name}`,
  hasGit: true,
  hasBuildCommand: false,
  hasTestCommand: false,
  ...extra,
});

function render(navigationId?: number) {
  const fakeLocation = { back: jasmine.createSpy('back'), getState: () => (navigationId === undefined ? null : { navigationId }) };
  TestBed.configureTestingModule({
    imports: [SettingsPage],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: Location, useValue: fakeLocation }],
  });
  const fixture = TestBed.createComponent(SettingsPage);
  const http = TestBed.inject(HttpTestingController);
  fixture.detectChanges();
  return { fixture, http, el: fixture.nativeElement as HTMLElement, fakeLocation };
}

const button = (el: HTMLElement, text: string) => Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes(text)) as HTMLButtonElement;

function type(el: HTMLElement, id: string, value: string): void {
  const input = el.querySelector('#' + id) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

function answerInitial(http: HttpTestingController, repos: RepositoryItem[]): void {
  http.match('/api/ai/status').forEach((r) => r.flush({}));
  http.expectOne('/api/repositories').flush(repos);
  http.match('/api/settings/ai').forEach((r) => r.error(new ProgressEvent('error')));
}

describe('SettingsPage: repositórios', () => {
  it('lista os repositórios com pasta e avisos', () => {
    const { fixture, http, el } = render();
    answerInitial(http, [repo('a-sistema', { hasGit: false }), repo('b-sistema', { hasBuildCommand: true, hasTestCommand: true })]);
    fixture.detectChanges();

    const rows = el.querySelectorAll('.repo');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('C:\\codigo\\a-sistema');
    expect(rows[0].textContent).toContain('sem .git: o levantamento fica sem o commit');
    expect(rows[0].textContent).toContain('rastro repo add');
    expect(rows[1].textContent).toContain('Build e testes configurados.');
    expect(rows[1].textContent).not.toContain('sem .git');
  });

  it('mostra o estado vazio', () => {
    const { fixture, http, el } = render();
    answerInitial(http, []);
    fixture.detectChanges();
    expect(el.textContent).toContain('Nenhum repositório cadastrado ainda');
  });

  it('adiciona um repositório, atualiza a lista e o "Abrir no VS Code"', async () => {
    const { fixture, http, el } = render();
    answerInitial(http, []);
    fixture.detectChanges();
    await fixture.whenStable();

    type(el, 'repo-name', 'meu-sistema');
    type(el, 'repo-path', 'C:\\codigo\\meu-sistema');
    fixture.detectChanges();
    (el.querySelector('form.repo-form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    const req = http.expectOne((r) => r.method === 'POST' && r.url === '/api/repositories');
    expect(req.request.body).toEqual({ name: 'meu-sistema', path: 'C:\\codigo\\meu-sistema' });
    req.flush(repo('meu-sistema'), { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    expect(el.querySelectorAll('.repo').length).toBe(1);
    expect(el.textContent).toContain('Repositório "meu-sistema" cadastrado.');
    expect(TestBed.inject(EditorLinks).link('meu-sistema', 'src/App.cs', 1)).toBe('vscode://file/C:/codigo/meu-sistema/src/App.cs:1');
  });

  it('recusa nome e pasta inválidos sem chamar a API', () => {
    const { fixture, http, el } = render();
    answerInitial(http, []);
    fixture.detectChanges();

    type(el, 'repo-name', '-ruim');
    type(el, 'repo-path', 'relativa');
    fixture.detectChanges();
    (el.querySelector('form.repo-form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(el.querySelector('#repo-name-error')?.textContent).toContain('Nome inválido');
    http.expectNone((r) => r.method === 'POST');
  });

  it('mostra o erro da API pelo código, junto do campo', async () => {
    const { fixture, http, el } = render();
    answerInitial(http, []);
    fixture.detectChanges();
    await fixture.whenStable();

    type(el, 'repo-name', 'meu-sistema');
    type(el, 'repo-path', 'C:\\nao\\existe');
    fixture.detectChanges();
    (el.querySelector('form.repo-form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    http.expectOne((r) => r.method === 'POST').flush({ code: 'REPOSITORY_FOLDER_NOT_FOUND' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(el.querySelector('#repo-path-error')?.textContent).toContain('Essa pasta não existe neste computador.');
  });

  it('remove só depois da confirmação e avisa que os levantamentos ficam', () => {
    const { fixture, http, el } = render();
    answerInitial(http, [repo('meu-sistema')]);
    fixture.detectChanges();

    button(el, 'Remover…').click();
    fixture.detectChanges();
    expect(el.querySelector('[role="alertdialog"]')?.textContent).toContain('levantamentos já feitos continuam guardados');
    http.expectNone((r) => r.method === 'DELETE');

    const confirm = Array.from(el.querySelectorAll('[role="alertdialog"] button')).find((b) => b.textContent?.trim() === 'Remover') as HTMLButtonElement;
    confirm.click();
    http.expectOne((r) => r.method === 'DELETE' && r.url === '/api/repositories/meu-sistema').flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    expect(el.querySelectorAll('.repo').length).toBe(0);
    expect(el.querySelector('[role="alertdialog"]')).toBeNull();
    expect(TestBed.inject(EditorLinks).folder('meu-sistema')).toBe('');
  });

  it('cancelar a remoção não chama a API', () => {
    const { fixture, http, el } = render();
    answerInitial(http, [repo('meu-sistema')]);
    fixture.detectChanges();

    button(el, 'Remover…').click();
    fixture.detectChanges();
    button(el, 'Cancelar').click();
    fixture.detectChanges();

    expect(el.querySelector('[role="alertdialog"]')).toBeNull();
    expect(el.querySelectorAll('.repo').length).toBe(1);
    http.expectNone((r) => r.method === 'DELETE');
  });
});

describe('SettingsPage: voltar', () => {
  it('volta à tela anterior quando a navegação veio de dentro do app', () => {
    const { fixture, http, el, fakeLocation } = render(3);
    answerInitial(http, []);
    fixture.detectChanges();
    button(el, 'Voltar').click();
    expect(fakeLocation.back).toHaveBeenCalled();
  });

  it('vai para a lista quando a página foi aberta direto', () => {
    const { fixture, http, el, fakeLocation } = render(1);
    answerInitial(http, []);
    fixture.detectChanges();
    const navigate = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
    button(el, 'Voltar').click();
    expect(fakeLocation.back).not.toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith('/');
  });
});
