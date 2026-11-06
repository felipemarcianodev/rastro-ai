import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { DbSchema, DbUsageReport } from '../../core/database/db-schema';
import { DbOperation, FindingKind, FindingSeverity } from '../../core/models';
import { lojaSchema } from '../../core/database/loja-schema.spec';
import { DatabasePage } from './database.page';

const usage: DbUsageReport = {
  uses: [
    { target: 'pedidos', operation: DbOperation.Read, surveyId: 's1', entryPoint: 'PedidosController.Listar', nodeId: 'm:listar', nodeLabel: 'PedidoRepositorio.AbertosAsync', location: { file: 'Loja.Api/Pedidos.cs', line: 10 }, surveyCreatedAt: '2026-11-06T10:00:00Z' },
    { target: 'pedidos', operation: DbOperation.Insert, surveyId: 's2', entryPoint: 'PedidosController.Fechar', nodeId: 'm:fechar', nodeLabel: 'PedidosController.Fechar', surveyCreatedAt: '2026-11-06T10:00:00Z' },
    { target: 'sp_baixar_estoque', operation: DbOperation.Execute, surveyId: 's2', entryPoint: 'PedidosController.Fechar', nodeId: 'm:fechar', nodeLabel: 'PedidosController.Fechar', surveyCreatedAt: '2026-11-06T10:00:00Z' },
  ],
  findings: [
    { target: 'pedidos', kind: FindingKind.QueryInLoop, severity: FindingSeverity.Warning, message: 'PedidosController.Listar consulta pedidos dentro de um laço.', surveyId: 's1', entryPoint: 'PedidosController.Listar', nodeId: 'm:listar' },
  ],
};

function render(schema: DbSchema | null = lojaSchema(), tabela?: string, report: DbUsageReport | null = usage) {
  TestBed.configureTestingModule({
    imports: [DatabasePage],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });
  const fixture = TestBed.createComponent(DatabasePage);
  fixture.componentRef.setInput('nome', 'loja');
  if (tabela) fixture.componentRef.setInput('tabela', tabela);
  const http = TestBed.inject(HttpTestingController);
  fixture.detectChanges();
  http.expectOne('/api/repositories').flush([{ name: 'loja', path: 'C:/loja' }]);
  const request = http.expectOne('/api/repositories/loja/schema');
  if (schema) request.flush(schema);
  else request.flush({ code: 'REPOSITORY_NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
  const used = http.expectOne('/api/repositories/loja/schema/usage');
  if (report) used.flush(report);
  else used.flush({ code: 'X' }, { status: 500, statusText: 'Erro' });
  fixture.detectChanges();
  return { fixture, http, el: fixture.nativeElement as HTMLElement };
}

const cards = (el: HTMLElement) => [...el.querySelectorAll<HTMLButtonElement>('.db-card')];
const card = (el: HTMLElement, key: string) => el.querySelector<HTMLButtonElement>(`.db-card[data-table="${key}"]`)!;

describe('DatabasePage (Fase 38)', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  // WEB-DBV-001
  it('mostra o mapa, a lista e o selo do achado, com a aba de consulta ao lado', () => {
    const { el } = render();

    expect(cards(el).length).toBe(6);
    expect(card(el, 'pagamentos').textContent).toContain('FK sem índice');
    expect(card(el, 'clientes').querySelectorAll('.db-personal').length).toBe(2);
    expect(el.querySelectorAll('.db-side .db-item').length).toBe(8);
    expect(el.textContent).toContain('Nenhum banco é consultado');
    expect(el.querySelector('#db-tab-query')!.textContent).toContain('Analisar consulta');
    expect(el.querySelector('.db-panel')).toBeNull();
  });

  // WEB-DBV-002
  it('clicar na tabela abre o painel com campos, índices, relações e a divergência entre fontes', () => {
    const { fixture, el } = render();

    card(el, 'pedidos').click();
    fixture.detectChanges();

    const panel = el.querySelector('.db-panel')!;
    expect(card(el, 'pedidos').getAttribute('aria-pressed')).toBe('true');
    expect(panel.textContent).toContain('Divergência entre fontes: tipo de status');
    expect(panel.textContent).toContain('IX_pedidos_cliente_id');
    expect(panel.textContent).toContain('Aponta para clientes por cliente_id (N para 1)');
    expect(panel.textContent).toContain('pagamentos.pedido_id aponta para esta tabela (1 para N)');
    expect(panel.textContent).toContain('A view vw_pedidos_por_cliente lê esta tabela');
    expect(el.querySelectorAll('.db-link.is-lit').length).toBe(3);

    // Uma relação leva à outra tabela; Fechar devolve o mapa inteiro.
    [...panel.querySelectorAll<HTMLButtonElement>('.source-link')].find((b) => b.textContent!.includes('clientes por'))!.click();
    fixture.detectChanges();
    expect(card(el, 'clientes').classList).toContain('is-selected');
    el.querySelector<HTMLButtonElement>('.db-panel .db-panel-head .btn')!.click();
    fixture.detectChanges();
    expect(el.querySelector('.db-panel')).toBeNull();
  });

  // WEB-DBV-003
  it('o achado e a tabela de ligação aparecem no painel', () => {
    const { fixture, el } = render();

    card(el, 'itens_pedido').click();
    fixture.detectChanges();

    const panel = el.querySelector('.db-panel')!;
    expect(panel.textContent).toContain('Tabela de ligação: pedidos N para N produtos');
    expect(panel.querySelector('.db-finding')!.textContent).toContain('arquivo .sql banco/produtos.sql:2 cria a FK produto_id sem índice');
  });

  // WEB-DBV-004
  it('recorta o mapa na tabela escolhida e nas vizinhas até N saltos', () => {
    const { fixture, el } = render();
    const hops = el.querySelector<HTMLSelectElement>('#db-hops')!;
    expect(hops.disabled).toBeTrue();

    card(el, 'pagamentos').click();
    fixture.detectChanges();
    hops.value = '1';
    hops.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(cards(el).map((c) => c.dataset['table']).sort()).toEqual(['pagamentos', 'pedidos']);
    expect(el.textContent).toContain('4 tabelas fora do recorte');
  });

  // WEB-DBV-005
  it('a busca filtra a lista e escurece no mapa o que não casa', () => {
    const { fixture, el } = render();
    const search = el.querySelector<HTMLInputElement>('#db-search')!;

    search.value = 'cpf';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect([...el.querySelectorAll('.db-side .db-item')].map((b) => b.textContent!.trim())).toEqual(['clientes']);
    expect(card(el, 'clientes').classList).not.toContain('is-dim');
    expect(card(el, 'pedidos').classList).toContain('is-dim');
  });

  // WEB-DBV-006
  it('a view escolhida acende as tabelas que ela lê', () => {
    const { fixture, el } = render();

    [...el.querySelectorAll<HTMLButtonElement>('.db-item')].find((b) => b.textContent!.includes('vw_pedidos_por_cliente'))!.click();
    fixture.detectChanges();

    expect(cards(el).filter((c) => c.classList.contains('is-lit')).map((c) => c.dataset['table']).sort()).toEqual(['clientes', 'pedidos']);
    expect(el.querySelector('.db-panel')!.textContent).toContain('Colunas que devolve');
  });

  // WEB-DBV-008
  it('o zoom muda a escala do mapa com limites, e o tipo divergente fica marcado no bloco', () => {
    const { fixture, el } = render();
    const [minus, reset, plus] = [...el.querySelectorAll<HTMLButtonElement>('.db-zoom-tools button')];
    expect(reset.textContent!.trim()).toBe('100%');

    minus.click();
    minus.click();
    fixture.detectChanges();
    expect(el.querySelector<HTMLElement>('.db-canvas')!.style.transform).toBe('scale(0.5)');
    expect(minus.disabled).toBeTrue();
    reset.click();
    plus.click();
    fixture.detectChanges();
    expect(reset.textContent!.trim()).toBe('125%');

    const status = [...card(el, 'pedidos').querySelectorAll('.db-row-type')].find((t) => t.textContent!.includes('divergente'))!;
    expect(status.classList).toContain('is-divergent');
  });

  // WEB-DBV-009
  it('o painel da tabela lista os fluxos que a usam, com o link para o passo, e os achados dos fluxos', () => {
    const { fixture, el } = render();
    expect(card(el, 'pedidos').textContent).toContain('1 achado');

    card(el, 'pedidos').click();
    fixture.detectChanges();

    const panel = el.querySelector('.db-panel')!;
    expect(panel.textContent).toContain('Fluxos que usam esta tabela');
    const links = [...panel.querySelectorAll<HTMLAnchorElement>('.db-access a')];
    expect(links.map((a) => a.textContent!.trim())).toEqual(['PedidosController.Listar › PedidoRepositorio.AbertosAsync', 'PedidosController.Fechar › PedidosController.Fechar']);
    expect(links[0].getAttribute('href')).toBe('/surveys/s1?passo=m:listar');
    expect([...panel.querySelectorAll('.db-op')].map((o) => o.textContent!.trim())).toEqual(['lê', 'inclui']);
    expect(panel.textContent).toContain('Consulta dentro de laço (N+1)');

    // Tabela sem uso: diz que levantamento antigo precisa ser refeito.
    card(el, 'produtos').click();
    fixture.detectChanges();
    expect(el.querySelector('.db-panel')!.textContent).toContain('Levantamentos feitos antes desta versão do Rastro não têm essa informação');
  });

  // WEB-DBV-010
  it('abre com a tabela de ?tabela= escolhida, e a procedure mostra quem a chama', () => {
    const { fixture, el } = render(lojaSchema(), 'dbo.pagamentos');
    expect(card(el, 'pagamentos').classList).toContain('is-selected');
    expect(document.activeElement).not.toBe(card(el, 'pagamentos'));

    [...el.querySelectorAll<HTMLButtonElement>('.db-item')].find((b) => b.textContent!.includes('sp_baixar_estoque'))!.click();
    fixture.detectChanges();
    expect(el.querySelector('.db-panel')!.textContent).toContain('Fluxos que chamam esta procedure');
    expect(el.querySelector('.db-panel .db-op')!.textContent!.trim()).toBe('executa');
  });

  // WEB-DBV-011
  it('sem os usos, o mapa e o painel continuam e o painel avisa', () => {
    const { fixture, el } = render(lojaSchema(), undefined, null);

    card(el, 'pedidos').click();
    fixture.detectChanges();
    expect(el.querySelector('.db-panel')!.textContent).toContain('Não deu para buscar os fluxos que usam este item');
    expect(el.querySelector('.db-panel')!.textContent).toContain('IX_pedidos_cliente_id');
  });

  // WEB-DBV-007
  it('repositório sem banco no código e repositório desconhecido têm estado próprio', () => {
    const empty = render({ ...lojaSchema(), tables: [], views: [], procedures: [], divergences: [], findings: [], notes: [] });
    expect(empty.el.textContent).toContain('Nenhum banco encontrado no código de loja');
    expect(empty.el.querySelector('.popover')).toBeNull();
    TestBed.resetTestingModule();

    const missing = render(null);
    expect(missing.el.querySelector('[role="alert"]')).not.toBeNull();
    expect(missing.el.textContent).toContain('Tentar de novo');
  });
});
