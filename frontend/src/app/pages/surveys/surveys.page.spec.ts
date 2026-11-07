import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { DESKTOP_EXTERNAL } from '../../core/desktop-bridge.service';
import { FakeDesktopExternal } from '../../core/desktop-bridge.service.spec';
import { LastRepositoryService } from '../../core/last-repository.service';
import { SurveyStatus, SurveySummary } from '../../core/models';
import { SurveysPage, matchesSearch } from './surveys.page';

const survey = (id: string, entryPoint: string, status: SurveyStatus): SurveySummary =>
  ({ id, entryPoint, status, repository: 'loja', solutionPath: 'backend/Loja.slnx', nodeCount: 3, unresolvedCount: 0, createdAt: '2026-10-16T10:00:00Z' }) as SurveySummary;

async function openNew(fixture: ComponentFixture<SurveysPage>): Promise<void> {
  // UX-G-N02: sem repositório o botão do topo some; o formulário ainda abre (ex.: volta de um levantamento que falhou).
  const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.new-survey');
  if (button) button.click();
  else (fixture.componentInstance as unknown as { openForm(): void }).openForm();
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
}

describe('SurveysPage', () => {
  it('busca sem acento e sem caixa, por todos os termos', () => {
    const item = survey('1', 'AprovacaoPedidoAppService.RegistrarPagamentoAsync', SurveyStatus.Completed);

    expect(matchesSearch(item, 'aprovação registrar')).toBeTrue();
    expect(matchesSearch(item, 'LOJA')).toBeTrue();
    expect(matchesSearch(item, 'alerta')).toBeFalse();
  });

  it('filtra por estado e conta cada chip', () => {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush([
      survey('1', 'A.Pronto', SurveyStatus.Completed),
      survey('2', 'B.Falhou', SurveyStatus.Failed),
      survey('3', 'C.Rodando', SurveyStatus.Running),
    ]);
    http.expectOne('/api/repositories').flush([]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    const chips = Array.from(el.querySelectorAll<HTMLButtonElement>('.chip'));
    expect(chips.map((c) => c.textContent?.replace(/\s+/g, ' ').trim())).toEqual(['Todos 3', 'Prontos 1', 'Em andamento 1', 'Com falha 1']);
    chips[3].click();
    fixture.detectChanges();

    const rows = Array.from(el.querySelectorAll('.row-entry')).map((r) => r.textContent);
    expect(rows).toEqual(['B.Falhou']);
    expect(chips[3].getAttribute('aria-pressed')).toBe('true');
    http.match(() => true).forEach((request) => request.flush([]));
  });

  it('vindo de um levantamento que falhou, preenche o formulário para corrigir', async () => {
    const retry = { repository: 'b-sistema', solutionPath: 'src/B.slnx', entryPoint: 'Loja.Api.Controllers.Reservar', maxDepth: 20 };
    const previous = history.state;
    history.replaceState({ ...previous, retry }, '');
    try {
      TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
      const fixture = TestBed.createComponent(SurveysPage);
      fixture.detectChanges();
      const http = TestBed.inject(HttpTestingController);
      http.expectOne('/api/surveys').flush([]);
      const repo = (name: string) => ({ name, path: `C:\\codigo\\${name}`, hasGit: true, hasBuildCommand: false, hasTestCommand: false });
      http.expectOne('/api/repositories').flush([repo('a-sistema'), repo('b-sistema')]);
      http.expectOne('/api/repositories/b-sistema/solutions').flush(['src/A.slnx', 'src/B.slnx']);
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;

      expect((el.querySelector('#repository') as HTMLSelectElement).value).toBe('b-sistema');
      expect((el.querySelector('#solution') as HTMLSelectElement).value).toBe('src/B.slnx');
      expect((el.querySelector('#entry') as HTMLInputElement).value).toBe('Loja.Api.Controllers.Reservar');
      expect((el.querySelector('#depth') as HTMLInputElement).value).toBe('20');
      http.match(() => true).forEach((request) => request.flush([]));
    } finally {
      history.replaceState(previous, '');
    }
  });

  it('o botão i de cada campo abre e fecha a explicação, também com Esc', async () => {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush([]);
    http.expectOne('/api/repositories').flush([]);
    fixture.detectChanges();
    await openNew(fixture);
    const el = fixture.nativeElement as HTMLElement;
    const toggles = Array.from(el.querySelectorAll<HTMLButtonElement>('.info-toggle'));
    const entry = toggles.find((b) => b.getAttribute('aria-label') === 'O que é Ponto de entrada')!;

    expect(toggles.length).toBe(4);
    // UX-G-L18: o resumo não se repete no title do botão i.
    expect(entry.title).toBe('');
    entry.click();
    fixture.detectChanges();
    expect(entry.getAttribute('aria-expanded')).toBe('true');
    expect(el.querySelector('#entry-info')?.textContent).toContain('PedidosController.Criar');

    entry.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(el.querySelector('#entry-info')).toBeNull();
    expect(entry.getAttribute('aria-expanded')).toBe('false');
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-G-L13
  it('semRepositorio_umVazioSoComUmCadastrar_semSelectDesabilitado', async () => {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush([]);
    http.expectOne('/api/repositories').flush([]);
    fixture.detectChanges();
    await openNew(fixture);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('#repository')).toBeNull();
    expect(el.querySelector('.field-help a')).toBeNull();
    const register = Array.from(el.querySelectorAll('.dialog button')).filter((b) => b.textContent?.includes('Cadastrar repositório'));
    expect(register.length).toBe(1);
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-H07
  it('queryParamRepositorio_nomeCadastrado_preSelecionaORepositorio', async () => {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    await TestBed.inject(Router).navigateByUrl('/?repositorio=pagamentos');
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush([]);
    const repo = (name: string) => ({ name, path: `C:\\codigo\\${name}`, hasGit: true, hasBuildCommand: false, hasTestCommand: false });
    http.expectOne('/api/repositories').flush([repo('pedidos'), repo('pagamentos')]);
    http.expectOne('/api/repositories/pagamentos/solutions').flush(['Pagamentos.slnx']);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect((el.querySelector('#repository') as HTMLSelectElement).value).toBe('pagamentos');
    expect((el.querySelector('#solution') as HTMLSelectElement).value).toBe('Pagamentos.slnx');
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-H06
  it('repositorioEscolhido_mostraBotaoSecundarioParaOMapaDoBanco', async () => {
    const { fixture, http, el } = await openForm(['Loja.slnx'], true);
    fixture.detectChanges();

    const link = el.querySelector<HTMLAnchorElement>('a.db-link');
    expect(link?.classList).toContain('btn');
    expect(link?.classList).toContain('btn-g');
    expect(link?.classList).toContain('btn-small');
    expect(link?.getAttribute('href')).toBe('/repositorios/loja-web/banco');
    expect(link?.textContent).toContain('Ver o mapa do banco de loja-web');
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-H10
  it('semRepositorioEscolhido_desabilitaLevantarOFluxo', async () => {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush([]);
    http.expectOne('/api/repositories').flush([]);
    fixture.detectChanges();
    await openNew(fixture);
    const submit = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button[type=submit]');

    expect(submit?.disabled).toBeTrue();
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-H12
  it('carregandoALista_mostraEsqueletoDeTresLinhas', () => {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);

    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.skeleton-row').length).toBe(3);
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UI-SYS-009
  it('formulario_costuraMarcada_enviaCrossRepositoriesComOProprioPorUltimo', async () => {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush([]);
    const repo = (name: string) => ({ name, path: `C:\codigo\${name}`, hasGit: true, hasBuildCommand: false, hasTestCommand: false });
    http.expectOne('/api/repositories').flush([repo('pedidos'), repo('pagamentos')]);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    await openNew(fixture);
    const el = fixture.nativeElement as HTMLElement;
    const select = el.querySelector('#repository') as HTMLSelectElement;
    select.value = 'pedidos';
    select.dispatchEvent(new Event('change'));
    http.expectOne('/api/repositories/pedidos/solutions').flush(['Pedidos.slnx']);
    await fixture.whenStable();
    fixture.detectChanges();

    const boxes = Array.from(el.querySelectorAll<HTMLInputElement>('.cross input[type=checkbox]'));
    const names = Array.from(el.querySelectorAll('.cross .check span')).map((s) => s.textContent);
    // Desligado por padrão, com os outros primeiro e o próprio por último.
    expect(boxes.every((b) => !b.checked)).toBeTrue();
    expect(names).toEqual(['pagamentos', 'o próprio repositório']);
    boxes.forEach((b) => {
      b.checked = true;
      b.dispatchEvent(new Event('change'));
    });
    const entry = el.querySelector('#entry') as HTMLInputElement;
    entry.value = 'PedidosController.Cancelar';
    entry.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const post = http.expectOne((r) => r.method === 'POST' && r.url === '/api/surveys');
    expect(post.request.body.crossRepositories).toEqual(['pagamentos', 'pedidos']);
    post.flush({ id: 'x' });
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UI-SYS-010
  it('formulario_semCosturaMarcada_naoEnviaCrossRepositories', async () => {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush([]);
    http.expectOne('/api/repositories').flush([{ name: 'pedidos', path: 'C:\codigo\pedidos', hasGit: true, hasBuildCommand: false, hasTestCommand: false }]);
    http.expectOne('/api/repositories/pedidos/solutions').flush(['Pedidos.slnx']);
    await fixture.whenStable();
    fixture.detectChanges();
    await openNew(fixture);
    const el = fixture.nativeElement as HTMLElement;
    const entry = el.querySelector('#entry') as HTMLInputElement;
    entry.value = 'PedidosController.Cancelar';
    entry.dispatchEvent(new Event('input'));
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const post = http.expectOne((r) => r.method === 'POST' && r.url === '/api/surveys');
    expect('crossRepositories' in post.request.body).toBeFalse();
    post.flush({ id: 'x' });
    http.match(() => true).forEach((request) => request.flush([]));
  });

  async function openForm(solutions: string[], angular: boolean) {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush([]);
    http.expectOne('/api/health').flush({ status: 'ok', version: '1', schemaVersion: 1, angular });
    http.expectOne('/api/repositories').flush([{ name: 'loja-web', path: 'C:\\codigo\\loja-web', hasGit: true, hasBuildCommand: false, hasTestCommand: false }]);
    http.expectOne('/api/repositories/loja-web/solutions').flush(solutions);
    await fixture.whenStable();
    fixture.detectChanges();
    await openNew(fixture);
    return { fixture, http, el: fixture.nativeElement as HTMLElement };
  }

  // UI-ANG-008
  it('formulario_caminhoTsconfig_mostraExemploEAjudaDeComponente', async () => {
    const { fixture, http, el } = await openForm(['tsconfig.app.json', 'Loja.slnx'], true);
    const entry = el.querySelector('#entry') as HTMLInputElement;

    expect(entry.placeholder).toBe('DetalhePedidoComponent.cancelar');
    expect(el.querySelector('#entry-help')?.textContent).toContain('Componente.metodo: o método que o clique da tela chama.');
    const select = el.querySelector('#solution') as HTMLSelectElement;
    select.value = 'Loja.slnx';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(entry.placeholder).toBe('PedidosController.Criar');
    expect(el.querySelector('#entry-help')?.textContent).toContain('Classe.Metodo');
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UI-ANG-009
  it('formulario_semNodeERepositorioSemSolution_avisaQueAngularPrecisaDoNode', async () => {
    const { http, el } = await openForm([], false);

    expect(el.querySelector('#node-notice')?.textContent).toContain('Fluxos Angular precisam do Node instalado nesta máquina.');
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UI-ANG-010
  it('formulario_semNodeMasRepositorioDotNet_naoAvisa', async () => {
    const { fixture, http, el } = await openForm(['Loja.slnx'], false);

    expect(el.querySelector('#node-notice')).toBeNull();
    // Digitar um ponto de entrada de componente justifica o aviso.
    const entry = el.querySelector('#entry') as HTMLInputElement;
    entry.value = 'DetalhePedidoComponent.cancelar';
    entry.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(el.querySelector('#node-notice')).not.toBeNull();
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UI-ANG-011
  it('formulario_comNode_naoAvisa', async () => {
    const { http, el } = await openForm([], true);

    expect(el.querySelector('#node-notice')).toBeNull();
    http.match(() => true).forEach((request) => request.flush([]));
  });
});

describe('SurveysPage: primeiro uso e cadastro de repositório', () => {
  const repo = (name: string) => ({ name, path: `C:\codigo\${name}`, hasGit: true, hasBuildCommand: false, hasTestCommand: false });

  function open(repos: ReturnType<typeof repo>[]) {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush([]);
    http.expectOne('/api/repositories').flush(repos);
    fixture.detectChanges();
    return { fixture, http, el: fixture.nativeElement as HTMLElement };
  }

  const text = (el: HTMLElement, selector: string, label: string) =>
    Array.from(el.querySelectorAll<HTMLButtonElement>(selector)).find((b) => b.textContent?.includes(label));

  // UX-H09
  it('semRepositorio_mostraPassosNumeradosComBotaoELinkDaIa', () => {
    const { el, http } = open([]);

    expect(el.querySelectorAll('.first-use ol.steps li').length).toBe(2);
    expect(text(el, '.first-use button', 'Cadastrar repositório')).toBeDefined();
    expect(el.querySelector('.first-use a')?.getAttribute('href')).toBe('/configuracoes#ia');
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-H09
  it('comRepositorioSemLevantamento_mantemOTextoDeHoje', () => {
    const { el, http } = open([repo('loja')]);

    expect(el.querySelector('.first-use')).toBeNull();
    expect(el.querySelector('.empty')?.textContent).toContain('Nenhum levantamento ainda.');
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-H10
  it('dialogoCadastrar_salva_escolheORepositorioNoFormulario', async () => {
    const { fixture, http, el } = open([]);
    text(el, '.first-use button', 'Cadastrar repositório')!.click();
    fixture.detectChanges();
    // O ngModel escreve o valor inicial depois do primeiro ciclo: digitar antes disso perde o texto.
    await fixture.whenStable();

    expect(el.querySelector('[role="dialog"]')).not.toBeNull();
    const name = el.querySelector('#register-name') as HTMLInputElement;
    const path = el.querySelector('#register-path') as HTMLInputElement;
    name.value = 'loja';
    name.dispatchEvent(new Event('input'));
    path.value = 'C:\\codigo\\loja';
    path.dispatchEvent(new Event('input'));
    (el.querySelector('[role="dialog"] form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const post = http.expectOne('/api/repositories');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ name: 'loja', path: 'C:\\codigo\\loja' });
    post.flush(repo('loja'));
    http.expectOne('/api/repositories/loja/solutions').flush(['Loja.slnx']);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('#register-title')).toBeNull();
    expect((el.querySelector('#repository') as HTMLSelectElement).value).toBe('loja');
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-H10
  it('dialogoCadastrar_erroDaApi_apareceNoCampoPeloCodigo', async () => {
    const { fixture, http, el } = open([]);
    text(el, '.first-use button', 'Cadastrar repositório')!.click();
    fixture.detectChanges();
    // O ngModel escreve o valor inicial depois do primeiro ciclo: digitar antes disso perde o texto.
    await fixture.whenStable();
    const name = el.querySelector('#register-name') as HTMLInputElement;
    const path = el.querySelector('#register-path') as HTMLInputElement;
    name.value = 'loja';
    name.dispatchEvent(new Event('input'));
    path.value = 'C:\\codigo\\loja';
    path.dispatchEvent(new Event('input'));
    (el.querySelector('[role="dialog"] form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    http.expectOne('/api/repositories').flush({ code: 'REPOSITORY_EXISTS', message: 'x' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(el.querySelector('[role="dialog"]')).not.toBeNull();
    expect(el.querySelector('#register-name-error')).not.toBeNull();
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-H10
  it('dialogoCadastrar_esc_fechaSemCadastrar', () => {
    const { fixture, http, el } = open([]);
    text(el, '.first-use button', 'Cadastrar repositório')!.click();
    fixture.detectChanges();

    el.querySelector('.overlay')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();

    expect(el.querySelector('[role="dialog"]')).toBeNull();
    http.expectNone((r) => r.method === 'POST');
    http.match(() => true).forEach((request) => request.flush([]));
  });
});

describe('SurveysPage: tela inicial por repositório', () => {
  const repo = (name: string) => ({ name, path: `C:\\codigo\\${name}`, hasGit: true, hasBuildCommand: false, hasTestCommand: false });
  const survey = (id: string, repository: string, solutionPath: string, createdAt: string) =>
    ({ id, entryPoint: 'A.Metodo', status: SurveyStatus.Completed, repository, solutionPath, nodeCount: 3, unresolvedCount: 0, createdAt }) as SurveySummary;

  function open(surveys: SurveySummary[]) {
    localStorage.removeItem('rastro.lastRepository');
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush(surveys);
    http.expectOne('/api/repositories').flush([repo('loja'), repo('pedidos')]);
    fixture.detectChanges();
    return { fixture, http, el: fixture.nativeElement as HTMLElement };
  }

  // UX-H08
  it('cartaoDeRepositorio_temOsTresLinksCertosEContagemEStack', () => {
    const { el, http } = open([
      survey('1', 'loja', 'old/Loja.slnx', '2026-10-01T10:00:00Z'),
      survey('2', 'loja', 'src/Loja.slnx', '2026-10-02T10:00:00Z'),
    ]);

    const cards = Array.from(el.querySelectorAll('.repo-card'));
    expect(cards.length).toBe(2);
    expect(cards[0].textContent).toContain('2 levantamentos');
    expect(cards[0].querySelector('.repo-stack')?.textContent).toContain('src/Loja.slnx');
    expect(cards[1].textContent).toContain('Nenhum levantamento');
    expect(cards[1].querySelector('.repo-stack')).toBeNull();
    const actions = cards[0].querySelectorAll('.action-grid > *');
    expect(actions.length).toBe(3);
    expect(actions[0].textContent).toContain('Levantar fluxo');
    expect(actions[1].getAttribute('href')).toBe('/repositorios/loja/banco');
    expect(actions[2].getAttribute('href')).toBe('/repositorios/loja/banco?aba=consulta');
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-H21
  it('levantarFluxo_abreOFormularioComORepositorioELembraOUltimo', async () => {
    const { fixture, http, el } = open([]);
    expect(el.querySelector('#repository')).toBeNull();

    el.querySelectorAll<HTMLButtonElement>('.repo-card .action-grid button')[1].click();
    http.expectOne('/api/repositories/pedidos/solutions').flush(['Pedidos.slnx']);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect((el.querySelector('#repository') as HTMLSelectElement).value).toBe('pedidos');
    expect(TestBed.inject(LastRepositoryService).name()).toBe('pedidos');
    expect(localStorage.getItem('rastro.lastRepository')).toBe('pedidos');
    http.match(() => true).forEach((request) => request.flush([]));
    localStorage.removeItem('rastro.lastRepository');
  });

  // UX-H22
  // UX-G-L24
  it('linhaDoLevantamento_temSoAbrir', () => {
    const { el, http } = open([survey('abc-1', 'loja', 'Loja.slnx', '2026-10-02T10:00:00Z')]);

    const links = Array.from(el.querySelectorAll<HTMLAnchorElement>('.row-actions a'));
    expect(links.map((a) => a.textContent?.trim())).toEqual(['Abrir']);
    expect(links[0].getAttribute('href')).toBe('/surveys/abc-1');
    http.match(() => true).forEach((request) => request.flush([]));
  });
});

describe('SurveysPage: Procurar pasta na janela própria', () => {
  async function openRegister(external: FakeDesktopExternal | null) {
    TestBed.configureTestingModule({
      imports: [SurveysPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: DESKTOP_EXTERNAL, useValue: external }],
    });
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush([]);
    http.expectOne('/api/repositories').flush([]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    Array.from(el.querySelectorAll<HTMLButtonElement>('.first-use button')).find((b) => b.textContent?.includes('Cadastrar repositório'))!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    return { fixture, http, el };
  }

  const browse = (el: HTMLElement) =>
    Array.from(el.querySelectorAll<HTMLButtonElement>('[role="dialog"] button')).find((b) => b.textContent?.includes('Procurar'));

  // UI-JAN-005
  it('noNavegador_naoMostraOProcurar', async () => {
    const { el, http } = await openRegister(null);

    expect(el.querySelector('#register-path')).not.toBeNull();
    expect(browse(el)).toBeUndefined();
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UI-JAN-006
  it('naJanela_procurar_preencheACaminhoEscolhido', async () => {
    const external = new FakeDesktopExternal();
    const { fixture, el, http } = await openRegister(external);

    browse(el)!.click();
    external.pick('C:\codigo\loja');
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();

    expect((el.querySelector('#register-path') as HTMLInputElement).value).toBe('C:\codigo\loja');
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-G-L02
  it('listaDeRepositorios_comErro_mostraTentarDeNovoERecarrega', () => {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('.skeleton-card').length).toBe(3);
    http.expectOne('/api/surveys').flush([]);
    http.expectOne('/api/repositories').flush({ code: 'X' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    const retry = el.querySelector<HTMLButtonElement>('.page > .notice.is-error .retry');
    expect(retry).not.toBeNull();
    expect(el.querySelectorAll('.skeleton-card').length).toBe(0);
    retry!.click();
    http.expectOne('/api/repositories').flush([]);
    fixture.detectChanges();
    expect(el.querySelector('.page > .notice.is-error')).toBeNull();
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-G-L02
  it('semLevantamentos_textoNaoDizAcima', () => {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush([]);
    http.expectOne('/api/repositories').flush([{ name: 'loja', path: 'C:\\codigo\\loja', hasGit: true, hasBuildCommand: false, hasTestCommand: false }]);
    http.expectOne('/api/repositories/loja/solutions').flush([]);
    fixture.detectChanges();
    const empty = (fixture.nativeElement as HTMLElement).querySelector('.empty')!;
    expect(empty.textContent).toContain('Nenhum levantamento ainda.');
    expect(empty.textContent).not.toContain('acima');
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-G-L09
  it('linhaDaLista_abrirDescreveOResumoQueFicaEscondidoDoLeitor', () => {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush([survey('s1', 'A.Pronto', SurveyStatus.Completed)]);
    http.expectOne('/api/repositories').flush([]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const open = Array.from(el.querySelectorAll('.row-actions a')).find((a) => a.textContent?.trim() === 'Abrir')!;
    const id = open.getAttribute('aria-describedby')!;
    expect(id).toBe('row-summary-s1');
    // UX-G-F04: a contagem fala em passos, como no cabeçalho do fluxo.
    expect(el.querySelector('#' + id)?.textContent).toContain('3 passos');
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-G-L16
  it('dialogoCadastrar_mostraARegraDoNome', async () => {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush([]);
    http.expectOne('/api/repositories').flush([]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    el.querySelector<HTMLButtonElement>('.first-use button')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(el.querySelector('#register-name-help')?.textContent).toContain('começando por letra ou número');
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-G-L18
  it('ajudaDosCampos_viemDepoisDoControleESemTitleNosBotoesI', async () => {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush([]);
    http.expectOne('/api/repositories').flush([{ name: 'loja', path: 'C:\\codigo\\loja', hasGit: true, hasBuildCommand: false, hasTestCommand: false }]);
    http.expectOne('/api/repositories/loja/solutions').flush(['Loja.slnx']);
    fixture.detectChanges();
    await openNew(fixture);
    const el = fixture.nativeElement as HTMLElement;
    for (const field of ['repository', 'solution', 'entry', 'depth']) {
      const control = el.querySelector('#' + field)!;
      const help = el.querySelector('#' + field + '-help')!;
      expect(control.compareDocumentPosition(help) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    }
    expect(Array.from(el.querySelectorAll('.info-toggle')).every((b) => !b.hasAttribute('title'))).toBeTrue();
    http.match(() => true).forEach((request) => request.flush([]));
  });
});

describe('SurveysPage: diálogo Novo levantamento', () => {
  async function openWithRepo() {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(SurveysPage);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush([]);
    http.expectOne('/api/repositories').flush([{ name: 'loja', path: 'C:\codigo\loja', hasGit: true, hasBuildCommand: false, hasTestCommand: false }]);
    http.expectOne('/api/repositories/loja/solutions').flush(['Loja.slnx']);
    fixture.detectChanges();
    await openNew(fixture);
    return { fixture, http, el: fixture.nativeElement as HTMLElement };
  }

  // UX-G-L03
  it('foco inicial vai ao primeiro campo e nao ao Fechar', async () => {
    const { fixture, http, el } = await openWithRepo();

    expect(document.activeElement).toBe(el.querySelector('#repository'));
    expect((document.activeElement as HTMLElement).hasAttribute('data-dialog-close')).toBeFalse();
    http.match(() => true).forEach((request) => request.flush([]));
    fixture.nativeElement.remove();
  });

  // UX-G-L04
  it('clique no fundo com dado digitado nao fecha; Esc e Fechar fecham', async () => {
    const { fixture, http, el } = await openWithRepo();
    const overlay = () => el.querySelector<HTMLElement>('.overlay')!;

    overlay().click();
    fixture.detectChanges();
    expect(el.querySelector('[role="dialog"]')).toBeNull();

    await openNew(fixture);
    const entry = el.querySelector<HTMLInputElement>('#entry')!;
    entry.value = 'PedidosController.Criar';
    entry.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    overlay().click();
    fixture.detectChanges();
    expect(el.querySelector('[role="dialog"]')).not.toBeNull();

    overlay().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(el.querySelector('[role="dialog"]')).toBeNull();

    await openNew(fixture);
    const depth = el.querySelector<HTMLInputElement>('#depth')!;
    depth.value = '8';
    depth.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    overlay().click();
    fixture.detectChanges();
    expect(el.querySelector('[role="dialog"]')).not.toBeNull();
    el.querySelector<HTMLButtonElement>('[data-dialog-close]')!.click();
    fixture.detectChanges();
    expect(el.querySelector('[role="dialog"]')).toBeNull();
    http.match(() => true).forEach((request) => request.flush([]));
    fixture.nativeElement.remove();
  });
});

// UX-G-B25
describe('SurveysPage: chegada pelo mapa do banco', () => {
  it('com origem=banco, o formulário oferece "Voltar ao banco" do repositório', async () => {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    await TestBed.inject(Router).navigateByUrl('/?repositorio=pagamentos&origem=banco');
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush([]);
    const repo = (name: string) => ({ name, path: `C:\codigo\${name}`, hasGit: true, hasBuildCommand: false, hasTestCommand: false });
    http.expectOne('/api/repositories').flush([repo('pagamentos')]);
    http.match(() => true).forEach((request) => request.flush([]));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const back = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('.dialog a')).find((a) => a.textContent?.trim() === 'Voltar ao banco');
    expect(back?.getAttribute('href')).toBe('/repositorios/pagamentos/banco');
  });
});

describe('SurveysPage: Fase C do levantamento completo', () => {
  const repo = (name: string) => ({ name, path: `C:\\codigo\\${name}`, hasGit: true, hasBuildCommand: false, hasTestCommand: false });

  async function render(repos: ReturnType<typeof repo>[], surveys: SurveySummary[]) {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush(surveys);
    http.expectOne('/api/repositories').flush(repos);
    if (repos.length === 1) http.expectOne(`/api/repositories/${repos[0].name}/solutions`).flush(['Loja.slnx']);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, http, el: fixture.nativeElement as HTMLElement };
  }

  // UX-G-N02
  it('semRepositorio_mostraSoOBlocoDePrimeiroUsoComH1DeBoasVindas', async () => {
    const { el, http } = await render([], []);

    expect(el.querySelector('h1')?.textContent?.trim()).toBe('Boas-vindas ao Rastro');
    expect(el.querySelector('.new-survey')).toBeNull();
    expect(el.querySelector('#recent-title')).toBeNull();
    expect(el.querySelector('.first-use')).not.toBeNull();
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-G-L14
  it('primeiroUso_ia_viraNotaAbaixoDosDoisPassos', async () => {
    const { el, http } = await render([], []);

    const steps = Array.from(el.querySelectorAll('.first-use ol.steps li')).map((li) => li.textContent?.trim());
    expect(steps).toEqual(['Cadastre a pasta do código que o Rastro vai ler.', 'Escolha o método de entrada e levante o primeiro fluxo.']);
    const note = el.querySelector('.first-use .ai-note') as HTMLElement;
    expect(note.textContent?.replace(/\s+/g, ' ').trim()).toBe('A IA é opcional: o Rastro funciona sem ela. Para ligar, abra as Configurações.');
    expect(note.querySelector('a')?.getAttribute('href')).toBe('/configuracoes#ia');
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-G-L14
  it('depoisDoCadastro_oAvisoApareceNoFormularioQueAbre', async () => {
    const { fixture, http, el } = await render([], []);
    Array.from(el.querySelectorAll<HTMLButtonElement>('.first-use button')).find((b) => b.textContent?.includes('Cadastrar repositório'))!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    const name = el.querySelector('#register-name') as HTMLInputElement;
    const path = el.querySelector('#register-path') as HTMLInputElement;
    name.value = 'loja';
    name.dispatchEvent(new Event('input'));
    path.value = 'C:\\codigo\\loja';
    path.dispatchEvent(new Event('input'));
    (el.querySelector('[role="dialog"] form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    http.expectOne('/api/repositories').flush(repo('loja'));
    http.expectOne('/api/repositories/loja/solutions').flush(['Loja.slnx']);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('#create-title')).not.toBeNull();
    expect(el.querySelector('.dialog-head .note-ok')?.textContent?.trim()).toBe('Repositório "loja" cadastrado.');
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-G-L01, UX-G-L11
  it('comRepositorio_h1Levantamentos_doisH2_eUmUnicoBotaoPrimario', async () => {
    const { el, http } = await render([repo('loja'), repo('estoque')], [survey('1', 'A.Pronto', SurveyStatus.Completed)]);

    expect(el.querySelector('h1')?.textContent?.trim()).toBe('Levantamentos');
    const h2 = Array.from(el.querySelectorAll('h2')).map((h) => h.textContent?.trim()).filter((t) => t === 'Repositórios' || t === 'Levantamentos recentes');
    expect(h2).toEqual(['Repositórios', 'Levantamentos recentes']);
    const primaries = Array.from(el.querySelectorAll('.page .btn-p'));
    expect(primaries.length).toBe(1);
    expect(primaries[0].textContent?.trim()).toBe('Novo levantamento');
    const card = Array.from(el.querySelectorAll<HTMLButtonElement>('.repo-card button')).find((b) => b.textContent?.trim() === 'Levantar fluxo')!;
    expect(card.classList.contains('btn-g')).toBeTrue();
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-G-L01
  it('formulario_botaoDeEnvioDiz_LevantarFluxo', async () => {
    const { fixture, http, el } = await render([repo('loja')], []);
    await openNew(fixture);

    const submit = el.querySelector('form button[type=submit]') as HTMLButtonElement;
    expect(submit.textContent?.trim()).toBe('Levantar fluxo');
    expect(submit.classList.contains('btn-p')).toBeTrue();
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-G-L17
  it('busca_eCampoDaSolution_usamOsNomesNovos', async () => {
    const { fixture, http, el } = await render([repo('loja')], [survey('1', 'A.Pronto', SurveyStatus.Completed)]);

    expect((el.querySelector('#search') as HTMLInputElement).placeholder).toBe('Buscar por ponto de entrada, repositório ou projeto');
    await openNew(fixture);
    expect(el.querySelector('label[for=solution]')?.textContent?.trim()).toBe('Solution ou tsconfig');
    http.match(() => true).forEach((request) => request.flush([]));
  });

  // UX-G-N04
  it('comUmRepositorioSo_seguirChamadasHttpViraUmCheckboxSemGrupo', async () => {
    const { fixture, http, el } = await render([repo('loja')], []);
    await openNew(fixture);

    expect(el.querySelector('fieldset.cross')).toBeNull();
    expect(el.querySelectorAll('.cross input[type=checkbox]').length).toBe(1);
    expect(el.querySelector('.cross .check span')?.textContent?.trim()).toBe('Seguir as chamadas HTTP dentro do próprio repositório');
    http.match(() => true).forEach((request) => request.flush([]));
  });
});
