import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SurveyStatus, SurveySummary } from '../../core/models';
import { SurveysPage, matchesSearch } from './surveys.page';

const survey = (id: string, entryPoint: string, status: SurveyStatus): SurveySummary =>
  ({ id, entryPoint, status, repository: 'loja', solutionPath: 'backend/Loja.slnx', nodeCount: 3, unresolvedCount: 0, createdAt: '2026-10-16T10:00:00Z' }) as SurveySummary;

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

  it('o botão i de cada campo abre e fecha a explicação, também com Esc', () => {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush([]);
    http.expectOne('/api/repositories').flush([]);
    fixture.detectChanges();
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

  it('sem repositório, leva às Configurações em vez de mandar para o terminal', () => {
    TestBed.configureTestingModule({ imports: [SurveysPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const fixture = TestBed.createComponent(SurveysPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/surveys').flush([]);
    http.expectOne('/api/repositories').flush([]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    const link = el.querySelector<HTMLAnchorElement>('.field-help a');
    expect(link?.getAttribute('href')).toBe('/configuracoes#editor');
    expect(el.textContent).not.toContain('recarregue');
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
});
