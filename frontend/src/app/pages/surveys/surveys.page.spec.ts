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
  (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.new-survey')!.click();
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
    expect(entry.title).toContain('Classe.Metodo');
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

  it('sem repositório, leva às Configurações em vez de mandar para o terminal', async () => {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush([]);
    http.expectOne('/api/repositories').flush([]);
    fixture.detectChanges();
    await openNew(fixture);
    const el = fixture.nativeElement as HTMLElement;

    const link = el.querySelector<HTMLAnchorElement>('.field-help a');
    expect(link?.getAttribute('href')).toBe('/configuracoes#editor');
    expect(el.textContent).not.toContain('recarregue');
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

    expect(el.querySelectorAll('.first-use ol.steps li').length).toBe(3);
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
  it('linhaDoLevantamento_temAbrirEBancoDesteFluxoComOId', () => {
    const { el, http } = open([survey('abc-1', 'loja', 'Loja.slnx', '2026-10-02T10:00:00Z')]);

    const links = Array.from(el.querySelectorAll<HTMLAnchorElement>('.row-actions a'));
    expect(links.map((a) => a.textContent?.trim())).toEqual(['Abrir', 'Banco deste fluxo']);
    expect(links[0].getAttribute('href')).toBe('/surveys/abc-1');
    expect(links[1].getAttribute('href')).toBe('/repositorios/loja/banco?levantamento=abc-1');
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
});
