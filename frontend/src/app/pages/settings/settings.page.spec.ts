import { Location } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { DESKTOP_EXTERNAL } from '../../core/desktop-bridge.service';
import { FakeDesktopExternal } from '../../core/desktop-bridge.service.spec';
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
    answerInitial(http, [repo('a-sistema', { hasGit: false }), repo('b-sistema', { hasBuildCommand: true, hasTestCommand: true, buildCommand: 'dotnet build', testCommand: 'dotnet test' })]);
    fixture.detectChanges();

    const rows = el.querySelectorAll('.repo');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('C:\\codigo\\a-sistema');
    expect(rows[0].textContent).toContain('sem .git: o levantamento fica sem o commit');
    expect(rows[0].textContent).toContain('sem comando');
    expect(rows[1].textContent).toContain('dotnet build');
    expect(rows[1].textContent).not.toContain('sem .git');
  });

  // UX-H07
  it('cada repositório tem Mapa do banco e Novo levantamento com o repositório na URL', () => {
    const { fixture, http, el } = render();
    answerInitial(http, [repo('a-sistema'), repo('b-sistema')]);
    fixture.detectChanges();

    const links = Array.from(el.querySelectorAll('.repo')[1].querySelectorAll<HTMLAnchorElement>('a.btn'));
    expect(links.map((a) => a.textContent?.trim())).toEqual(['Mapa do banco', 'Novo levantamento']);
    expect(links[0].getAttribute('href')).toBe('/repositorios/b-sistema/banco');
    expect(links[1].getAttribute('href')).toBe('/?repositorio=b-sistema');
  });

  // UX-H24
  it('erro de carga da configuração oferece Tentar de novo', () => {
    const { fixture, http, el } = render();
    answerInitial(http, []);
    fixture.detectChanges();
    expect(el.querySelector('.notice.is-error')).not.toBeNull();

    button(el, 'Tentar de novo').click();
    http.expectOne('/api/settings/ai');
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

describe('SettingsPage: build e testes', () => {
  const edit = (el: HTMLElement) => button(el, 'Editar build e testes');
  const tick = () => new Promise((r) => setTimeout(r));

  // UX-FIM-C01
  it('abre a edição com o foco no Build, salva pelo PUT e anuncia Comandos salvos', async () => {
    const { fixture, http, el } = render();
    answerInitial(http, [repo('loja')]);
    fixture.detectChanges();

    edit(el).click();
    fixture.detectChanges();
    await tick();
    expect(document.activeElement?.id).toBe('build-loja');
    expect(el.querySelector('label[for="build-loja"]')?.textContent).toBe('Build');
    expect(el.querySelector('#build-loja')?.getAttribute('aria-describedby')).toBe('commands-help-loja');
    expect(el.querySelector('#commands-help-loja')?.textContent).toContain('sem shell');

    type(el, 'build-loja', ' dotnet build ');
    type(el, 'test-loja', '');
    fixture.detectChanges();
    button(el, 'Salvar comandos').click();
    const req = http.expectOne((r) => r.method === 'PUT' && r.url === '/api/repositories/loja/commands');
    expect(req.request.body).toEqual({ buildCommand: 'dotnet build', testCommand: null });
    req.flush(repo('loja', { hasBuildCommand: true, buildCommand: 'dotnet build' }));
    fixture.detectChanges();
    await tick();

    expect(el.querySelector('.commands-edit')).toBeNull();
    expect(el.querySelector('.note-ok')?.textContent).toContain('Comandos salvos');
    expect(el.querySelector('.repo')?.textContent).toContain('dotnet build');
    expect(document.activeElement?.id).toBe('edit-commands-loja');
  });

  // UX-FIM-C01
  it('erro 400 mantém a edição aberta com a mensagem do código', () => {
    const { fixture, http, el } = render();
    answerInitial(http, [repo('loja')]);
    fixture.detectChanges();

    edit(el).click();
    fixture.detectChanges();
    button(el, 'Salvar comandos').click();
    http.expectOne((r) => r.method === 'PUT').flush({ code: 'REPOSITORY_COMMAND_INVALID' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(el.querySelector('.commands-edit')).not.toBeNull();
    const alert = el.querySelector('.commands-edit .field-error');
    expect(alert?.getAttribute('role')).toBe('alert');
    expect(alert?.textContent).toContain('O comando precisa caber numa linha');
  });

  // UX-FIM-C01
  it('Cancelar fecha sem chamar a API e devolve o foco ao botão do repositório', async () => {
    const { fixture, http, el } = render();
    answerInitial(http, [repo('loja')]);
    fixture.detectChanges();

    edit(el).click();
    fixture.detectChanges();
    button(el, 'Cancelar').click();
    fixture.detectChanges();
    await tick();

    expect(el.querySelector('.commands-edit')).toBeNull();
    http.expectNone((r) => r.method === 'PUT');
    expect(document.activeElement?.id).toBe('edit-commands-loja');
  });
});

describe('SettingsPage: voltar', () => {
  // A3
  it('o voltar é a seta da barra de página, com o título ao lado', () => {
    const { fixture, http, el } = render(3);
    answerInitial(http, []);
    fixture.detectChanges();
    const bar = el.querySelector('.page-bar')!;
    expect(bar.querySelector('.page-back')!.getAttribute('aria-label')).toBe('Voltar');
    expect(bar.querySelector('h1')!.textContent).toBe('Configurações');
    expect(el.querySelector('.head h1')).toBeNull();
  });

  it('volta à tela anterior quando a navegação veio de dentro do app', () => {
    const { fixture, http, el, fakeLocation } = render(3);
    answerInitial(http, []);
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('.page-bar .page-back')!.click();
    expect(fakeLocation.back).toHaveBeenCalled();
  });

  it('vai para a lista quando a página foi aberta direto', () => {
    const { fixture, http, el, fakeLocation } = render(1);
    answerInitial(http, []);
    fixture.detectChanges();
    const navigate = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
    el.querySelector<HTMLButtonElement>('.page-bar .page-back')!.click();
    expect(fakeLocation.back).not.toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith('/');
  });
});

describe('SettingsPage: índice de seções', () => {
  // UX-H16
  it('mostraOIndiceComAsQuatroSecoesCadaUmaComId', () => {
    const { fixture, http, el } = render();
    http.match('/api/ai/status').forEach((r) => r.flush({}));
    http.expectOne('/api/repositories').flush([]);
    http.expectOne('/api/settings/ai').flush({
      enabled: false, provider: 0, claudeModel: '', claudeHasKey: false, openAiModel: '', openAiHasKey: false, geminiModel: '', geminiHasKey: false,
      dailyTokenBudget: 0, useFillHoles: true, useDescribeRules: true, useSuggestions: true, useContextSuggestions: true, updatedAt: '2026-11-06T10:00:00Z',
    });
    fixture.detectChanges();

    const links = Array.from(el.querySelectorAll<HTMLAnchorElement>('nav.toc a'));
    expect(links.map((a) => a.textContent?.trim())).toEqual(['Repositórios', 'Modo agente (MCP)', 'Inteligência artificial', 'Aparência']);
    for (const link of links) expect(el.querySelector(link.getAttribute('href')!)).not.toBeNull();
  });
});

describe('SettingsPage: aparência', () => {
  beforeEach(() => {
    localStorage.removeItem('rastro.theme');
    localStorage.removeItem('rastro.palette');
  });

  // UX-H04
  it('o índice ganha o item Aparência e a seção troca a paleta', () => {
    const { fixture, http, el } = render();
    answerInitial(http, []);
    fixture.detectChanges();

    expect(Array.from(el.querySelectorAll('.toc a')).map((a) => a.textContent?.trim())).toContain('Aparência');
    const select = el.querySelector('#aparencia select') as HTMLSelectElement;
    select.value = 'nautica';
    select.dispatchEvent(new Event('change'));
    expect(document.documentElement.getAttribute('data-palette')).toBe('nautica');
    select.value = 'papel';
    select.dispatchEvent(new Event('change'));
    expect(document.documentElement.hasAttribute('data-palette')).toBeFalse();
  });

  it('o tema tem três opções em grupo de rádio e a escolha vale na hora', () => {
    const { fixture, http, el } = render();
    answerInitial(http, []);
    fixture.detectChanges();

    const radios = Array.from(el.querySelectorAll<HTMLInputElement>('#aparencia input[type=radio][name=theme]'));
    expect(radios.map((r) => r.closest('label')?.textContent?.trim())).toEqual(['Do sistema', 'Claro', 'Escuro']);
    expect(radios[0].checked).toBeTrue();
    radios[2].click();
    fixture.detectChanges();
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    radios[0].click();
    expect(document.documentElement.hasAttribute('data-theme')).toBeFalse();
  });
});

describe('SettingsPage: Procurar pasta na janela própria', () => {
  function renderWith(external: FakeDesktopExternal | null) {
    const fakeLocation = { back: jasmine.createSpy('back'), getState: () => null };
    TestBed.configureTestingModule({
      imports: [SettingsPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: Location, useValue: fakeLocation },
        { provide: DESKTOP_EXTERNAL, useValue: external },
      ],
    });
    const fixture = TestBed.createComponent(SettingsPage);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    answerInitial(http, []);
    fixture.detectChanges();
    return { fixture, http, el: fixture.nativeElement as HTMLElement };
  }

  // UI-JAN-007
  it('noNavegador_naoMostraOProcurar', () => {
    const { el } = renderWith(null);

    expect(el.querySelector('#repo-path')).not.toBeNull();
    expect(button(el, 'Procurar')).toBeUndefined();
  });

  // UI-JAN-008
  it('naJanela_procurar_preencheOCampo_eCancelarNaoMexe', async () => {
    const external = new FakeDesktopExternal();
    const { fixture, el } = renderWith(external);
    await fixture.whenStable();

    button(el, 'Procurar').click();
    external.pick(null);
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
    expect((el.querySelector('#repo-path') as HTMLInputElement).value).toBe('');

    button(el, 'Procurar').click();
    expect(JSON.parse(external.sent[1]).requestId).toBe('pasta-2');
    external.pick('C:\codigo\loja');
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
    expect((el.querySelector('#repo-path') as HTMLInputElement).value).toBe('C:\codigo\loja');
  });
});
