import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { DbSchema, DbUsageReport } from '../../core/database/db-schema';
import { DB_SURVEY_ORIGIN_PARAM, DB_TAB_QUERY_PARAM, HOME_FROM_DB_PARAM, HOME_FROM_DB_VALUE } from '../../core/database/query-analysis';
import { DbOperation, FindingKind, FindingSeverity } from '../../core/models';
import { lojaSchema } from '../../core/database/loja-schema.spec';
import { LastRepositoryService } from '../../core/last-repository.service';
import { DatabasePage, LIST_ANNOUNCE_DELAY_MS } from './database.page';

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
  http.expectOne('/api/repositories/loja/connection').flush({ configured: false, server: null, database: null, updatedAt: null });
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
const buttonByText = (el: HTMLElement, text: string) => [...el.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent!.trim() === text);
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
  it('a busca filtra a lista e escurece no mapa o que não casa', fakeAsync(() => {
    const { fixture, el } = render();
    const search = el.querySelector<HTMLInputElement>('#db-search')!;

    search.value = 'cpf';
    search.dispatchEvent(new Event('input'));
    // UX-FIM-B10: a lista só refiltra depois do atraso de 150 ms.
    fixture.detectChanges();
    expect(el.querySelectorAll('.db-side .db-item').length).toBe(8);
    tick(150);
    fixture.detectChanges();

    expect([...el.querySelectorAll('.db-side .db-item')].map((b) => b.textContent!.trim())).toEqual(['clientes']);
    expect(card(el, 'clientes').classList).not.toContain('is-dim');
    expect(card(el, 'pedidos').classList).toContain('is-dim');
    // UX-FIM-B10: o bloco fora do filtro sai da ordem de Tab; o que casa continua nela.
    expect(card(el, 'pedidos').getAttribute('tabindex')).toBe('-1');
    expect(card(el, 'clientes').hasAttribute('tabindex')).toBeFalse();
    tick(LIST_ANNOUNCE_DELAY_MS);
  }));

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
    const [minus, reset, , plus] = [...el.querySelectorAll<HTMLButtonElement>('.db-zoom-tools button')];
    expect(reset.textContent!.trim()).toBe('100%');

    minus.click();
    minus.click();
    fixture.detectChanges();
    expect(el.querySelector<HTMLElement>('.db-canvas')!.style.transform).toBe('scale(0.5)');
    // O mínimo caiu para 25% (UX-G-B07): o limite é coberto no teste do "Ajustar".
    expect(minus.disabled).toBeFalse();
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

  // WEB-DBV-012 (B06)
  it('sem banco no código, oferece analisar uma consulta mesmo assim e conferir a pasta', () => {
    const { fixture, el } = render({ ...lojaSchema(), tables: [], views: [], procedures: [], divergences: [], findings: [], notes: [] });
    const actions = el.querySelector('.page-state-actions')!;
    const check = actions.querySelector<HTMLAnchorElement>('a')!;
    expect(check.textContent).toContain('Conferir a pasta do repositório');
    expect(check.getAttribute('href')).toBe('/configuracoes#editor');

    const query = [...actions.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent!.includes('Analisar uma consulta mesmo assim'))!;
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    query.click();
    fixture.detectChanges();
    expect(navigate).toHaveBeenCalledWith([], jasmine.objectContaining({ queryParams: { aba: DB_TAB_QUERY_PARAM } }));
  });

  // WEB-DBV-013 (B07)
  it('no erro de carga, mantém as abas (UX-G-B03), esconde o banco vivo e leva às configurações', () => {
    const { el } = render(null);
    expect(el.querySelectorAll('[role="tab"]').length).toBe(2);
    expect([...el.querySelectorAll('.page-bar button')].some((b) => b.textContent!.includes('banco vivo'))).toBeFalse();
    expect(el.querySelector('a[href="/configuracoes"]')!.textContent).toContain('configurações');
  });

  // WEB-DBV-014 (B04)
  it('o "←" volta ao levantamento de origem; sem origem, à tela inicial', () => {
    const none = render();
    const back = none.el.querySelector<HTMLAnchorElement>('.page-back')!;
    expect(back.getAttribute('href')).toBe('/');
    expect(back.getAttribute('aria-label')).toBe('Voltar aos levantamentos');
    TestBed.resetTestingModule();

    const withOrigin = render();
    withOrigin.fixture.componentRef.setInput(DB_SURVEY_ORIGIN_PARAM, 's42');
    withOrigin.fixture.detectChanges();
    const link = withOrigin.el.querySelector<HTMLAnchorElement>('.page-back')!;
    expect(link.getAttribute('href')).toBe('/surveys/s42');
    expect(link.getAttribute('aria-label')).toBe('Voltar ao levantamento de origem');
  });

  // WEB-DBV-015 (B26)
  it('o bloco mostra o tipo curto, com o tipo completo no title', () => {
    const schema = lojaSchema();
    schema.tables[0].columns[1].type = 'character varying(120)';
    schema.tables[0].columns[2].type = 'timestamp with time zone';
    const { el } = render(schema);

    const types = [...card(el, 'clientes').querySelectorAll<HTMLElement>('.db-row-type')];
    expect(types[1].textContent!.trim()).toBe('varchar(120)');
    expect(types[1].title).toBe('character varying(120)');
    expect(types[2].textContent!.trim()).toBe('timestamptz');
    expect(types[2].title).toBe('timestamp with time zone');
  });

  // WEB-DBV-016
  it('com repositório inexistente na URL, o seletor não mostra outro repositório', () => {
    TestBed.configureTestingModule({
      imports: [DatabasePage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(DatabasePage);
    fixture.componentRef.setInput('nome', 'fantasma');
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/repositories').flush([{ name: 'loja', path: 'C:/loja' }, { name: 'blog', path: 'C:/blog' }]);
    http.expectOne('/api/repositories/fantasma/connection').flush(null, { status: 404, statusText: 'Not Found' });
    http.expectOne('/api/repositories/fantasma/schema').flush({ code: 'REPOSITORY_NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    http.expectOne('/api/repositories/fantasma/schema/usage').flush({ code: 'X' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('#db-repo')).toBeNull();
    expect(el.querySelector('.page-bar-meta')!.textContent).toContain('fantasma');
  });

  // WEB-UXC-001 (B05)
  it('cada aba tem uma linha que explica o que ela faz', () => {
    const { el } = render();

    expect(el.querySelector('#db-tab-map .db-tab-hint')!.textContent).toBe('Tabelas, chaves e índices lidos do código');
    expect(el.querySelector('#db-tab-query .db-tab-hint')!.textContent).toBe('Cole um SQL e veja os gargalos, sem executar');
  });

  // WEB-UXC-002 (B09, B17)
  it('a promessa é uma frase só e o botão diz que exporta o mapa', () => {
    const { el } = render();

    expect(el.querySelector('.db-promise')!.textContent).toBe('Lido do código. Nenhum banco é consultado, a menos que você conecte um banco vivo, só de leitura.');
    const exportButton = [...el.querySelectorAll<HTMLButtonElement>('.page-bar-side button')].find((b) => b.textContent!.trim() === 'Exportar o mapa');
    expect(exportButton).toBeDefined();
  });

  // WEB-UXC-003
  it('abrir um repositório que existe o guarda como o último usado', () => {
    localStorage.removeItem('rastro.lastRepository');
    render();

    expect(TestBed.inject(LastRepositoryService).name()).toBe('loja');
  });

  // UX-FIM-B17
  it('a marca de chave do bloco sai do enum (PK cheia, FK com borda) e o nulo divergente do painel é marcado por booleano', () => {
    const schema = lojaSchema();
    const status = schema.tables.find((t) => t.name === 'pedidos')!.columns.find((c) => c.name === 'status')!;
    status.nullable = null;
    const { fixture, el } = render(schema);

    const keys = [...card(el, 'pedidos').querySelectorAll('.db-key')];
    expect(keys.map((k) => k.textContent!.trim())).toEqual(['PK', 'FK', '·']);
    expect(keys[0].classList).toContain('is-pk');
    expect(keys[1].classList).toContain('is-fk');
    expect(keys[1].classList).not.toContain('is-pk');
    expect(card(el, 'itens_pedido').querySelector('.db-key')!.classList).toContain('is-pk');

    card(el, 'pedidos').click();
    fixture.detectChanges();
    const cells = [...el.querySelectorAll('.db-fields td.db-divergent')].map((td) => td.textContent!.trim());
    expect(cells).toContain('divergente');
  });

  // UX-FIM-B08
  it('escolher uma tabela pela lista não move o foco do item da lista', fakeAsync(() => {
    const { fixture, el } = render();
    const item = el.querySelector<HTMLButtonElement>('.db-side .db-item[data-item="0:pedidos"]')!;
    item.focus();

    item.click();
    fixture.detectChanges();
    tick();

    expect(card(el, 'pedidos').classList).toContain('is-selected');
    expect(document.activeElement).toBe(item);
  }));

  // UX-FIM-B08
  it('escolher uma tabela pelo mapa continua focando o bloco ao fechar o painel', fakeAsync(() => {
    const { fixture, el } = render();
    card(el, 'pedidos').click();
    fixture.detectChanges();

    el.querySelector<HTMLButtonElement>('.db-panel button')!.click();
    fixture.detectChanges();
    tick();

    expect(document.activeElement).toBe(card(el, 'pedidos'));
  }));

  // UX-FIM-B09
  it('fechar o painel de uma view ou procedure devolve o foco ao item da lista', fakeAsync(() => {
    const { fixture, el } = render();
    for (const name of ['vw_pedidos_por_cliente', 'sp_baixar_estoque']) {
      const item = [...el.querySelectorAll<HTMLButtonElement>('.db-side .db-item')].find((b) => b.textContent!.includes(name))!;
      item.click();
      fixture.detectChanges();
      el.querySelector<HTMLButtonElement>('.db-panel button')!.click();
      fixture.detectChanges();
      tick();

      expect(el.querySelector('.db-panel')).toBeNull();
      expect(document.activeElement).toBe(item);
    }
  }));

  // UX-FIM-B11
  it('abaixo de 900px o painel rola para a vista ao escolher, e respeita movimento reduzido', fakeAsync(() => {
    const queries: string[] = [];
    spyOn(window, 'matchMedia').and.callFake((q: string) => {
      queries.push(q);
      return { matches: true } as MediaQueryList;
    });
    const scroll = spyOn(HTMLElement.prototype, 'scrollIntoView');
    const { fixture, el } = render();

    card(el, 'pedidos').click();
    fixture.detectChanges();
    tick();

    expect(scroll).toHaveBeenCalledOnceWith({ block: 'nearest', behavior: 'auto' });
    expect(queries.some((q) => q.includes('prefers-reduced-motion'))).toBeTrue();
  }));

  // UX-FIM-B11
  it('em tela larga o painel não rola a página', fakeAsync(() => {
    spyOn(window, 'matchMedia').and.returnValue({ matches: false } as MediaQueryList);
    const scroll = spyOn(HTMLElement.prototype, 'scrollIntoView');
    const { fixture, el } = render();

    card(el, 'pedidos').click();
    fixture.detectChanges();
    tick();

    expect(scroll).not.toHaveBeenCalled();
  }));

  // UX-FIM-B12
  it('o Esc só fecha o painel do mapa com a aba do mapa ativa', fakeAsync(() => {
    const { fixture, el } = render();
    card(el, 'pedidos').click();
    fixture.detectChanges();
    const escape = () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

    fixture.componentRef.setInput('aba', DB_TAB_QUERY_PARAM);
    fixture.detectChanges();
    escape();
    fixture.detectChanges();
    expect(el.querySelector('.db-panel')).not.toBeNull();

    fixture.componentRef.setInput('aba', undefined);
    fixture.detectChanges();
    escape();
    fixture.detectChanges();
    tick();
    expect(el.querySelector('.db-panel')).toBeNull();
  }));

  // UX-FIM-B12
  it('o menu Exportar devolve o foco ao botão ao fechar por Esc e por escolha', fakeAsync(() => {
    const { fixture, el } = render();
    const exportButton = el.querySelector<HTMLButtonElement>('#db-export-button')!;

    exportButton.click();
    fixture.detectChanges();
    expect(el.querySelector('#db-export-menu')).not.toBeNull();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    tick();
    expect(el.querySelector('#db-export-menu')).toBeNull();
    expect(document.activeElement).toBe(exportButton);

    exportButton.click();
    fixture.detectChanges();
    const mermaid = [...el.querySelectorAll<HTMLButtonElement>('#db-export-menu button')].find((b) => b.textContent!.includes('Copiar como Mermaid'))!;
    mermaid.focus();
    mermaid.click();
    fixture.detectChanges();
    tick(5000);
    expect(document.activeElement).toBe(exportButton);
  }));

  // UX-G-B02
  it('clicar em "A view X lê esta tabela" leva o foco ao título do painel novo', fakeAsync(() => {
    const { fixture, el } = render();
    card(el, 'pedidos').click();
    fixture.detectChanges();

    const link = [...el.querySelectorAll<HTMLButtonElement>('.db-panel .source-link')].find((b) => b.textContent!.includes('A view vw_pedidos_por_cliente'))!;
    link.focus();
    link.click();
    fixture.detectChanges();
    tick();

    const title = el.querySelector<HTMLElement>('.db-panel .db-panel-head h2')!;
    expect(title.textContent).toContain('vw_pedidos_por_cliente');
    expect(document.activeElement).toBe(title);
  }));

  // UX-G-B06
  it('os rótulos das setas ficam escondidos do leitor de tela e as relações vão no rótulo do bloco', () => {
    const { el } = render();

    const labels = [...el.querySelectorAll('.db-label')];
    expect(labels.length).toBeGreaterThan(0);
    expect(labels.every((l) => l.getAttribute('aria-hidden') === 'true')).toBeTrue();
    const aria = card(el, 'pedidos').getAttribute('aria-label')!;
    expect(aria).toContain('aponta para clientes');
    expect(aria).toContain('referenciada por');
    expect(aria).toContain('pagamentos');
  });

  // UX-G-B12
  it('o recorte desabilitado explica com texto visível e "Mostrar tudo" tira o recorte', () => {
    const { fixture, el } = render();
    const hops = el.querySelector<HTMLSelectElement>('#db-hops')!;
    expect(hops.disabled).toBeTrue();
    expect(el.querySelector('#db-hops-help')!.textContent).toContain('Escolha uma tabela para recortar o mapa nela.');
    expect(hops.getAttribute('aria-describedby')).toBe('db-hops-help');
    expect(buttonByText(el, 'Mostrar tudo')).toBeUndefined();

    card(el, 'pagamentos').click();
    fixture.detectChanges();
    hops.value = '1';
    hops.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(el.querySelector('#db-hops-help')).toBeNull();
    expect(cards(el).length).toBe(2);

    buttonByText(el, 'Mostrar tudo')!.click();
    fixture.detectChanges();
    expect(cards(el).length).toBe(6);
    expect(buttonByText(el, 'Mostrar tudo')).toBeUndefined();
  });

  // UX-G-B13
  it('Home e End vão à primeira e à última aba', fakeAsync(() => {
    const { fixture, el } = render();
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    const mapTab = el.querySelector<HTMLElement>('#db-tab-map')!;

    mapTab.dispatchEvent(new KeyboardEvent('keydown', { key: 'End', cancelable: true }));
    fixture.detectChanges();
    tick();
    expect(navigate).toHaveBeenCalledWith([], jasmine.objectContaining({ queryParams: { aba: DB_TAB_QUERY_PARAM } }));
    expect(document.activeElement).toBe(el.querySelector('#db-tab-query'));

    fixture.componentRef.setInput('aba', DB_TAB_QUERY_PARAM);
    fixture.detectChanges();
    navigate.calls.reset();
    el.querySelector<HTMLElement>('#db-tab-query')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', cancelable: true }));
    fixture.detectChanges();
    tick();
    expect(navigate).toHaveBeenCalledWith([], jasmine.objectContaining({ queryParams: { aba: null } }));
    expect(document.activeElement).toBe(mapTab);
  }));

  // UX-G-B18
  it('a lista usa content-visibility e o anúncio da contagem espera mais que o filtro', fakeAsync(() => {
    const { fixture, el } = render();
    expect(getComputedStyle(el.querySelector('.db-item')!).contentVisibility).toBe('auto');
    expect(getComputedStyle(card(el, 'pedidos')).contentVisibility).toBe('auto');
    const status = () => el.querySelector('.db-side [role="status"]')!.textContent!.trim();
    expect(status()).toBe('8 itens encontrados');

    const search = el.querySelector<HTMLInputElement>('#db-search')!;
    search.value = 'cpf';
    search.dispatchEvent(new Event('input'));
    tick(150);
    fixture.detectChanges();
    expect(el.querySelectorAll('.db-side .db-item').length).toBe(1);
    expect(status()).toBe('8 itens encontrados');

    tick(LIST_ANNOUNCE_DELAY_MS);
    fixture.detectChanges();
    expect(status()).toBe('1 itens encontrados');
  }));

  // UX-G-B19
  it('a carga do esquema usa o mesmo estado de carga das outras telas', () => {
    TestBed.configureTestingModule({
      imports: [DatabasePage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(DatabasePage);
    fixture.componentRef.setInput('nome', 'loja');
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    const state = el.querySelector('.page-state')!;
    expect(state.querySelector('svg.walking')).not.toBeNull();
    expect(state.textContent).toContain('Lendo o esquema do banco no código do repositório…');
    http.expectOne('/api/repositories').flush([]);
    http.expectOne('/api/repositories/loja/connection').flush({ configured: false, server: null, database: null, updatedAt: null });
    http.expectOne('/api/repositories/loja/schema').flush(lojaSchema());
    http.expectOne('/api/repositories/loja/schema/usage').flush({ uses: [], findings: [] });
  });

  // UX-G-B20
  it('o cabeçalho da tabela de campos fica fixo e Nulo e Chave se explicam no title', () => {
    const { fixture, el } = render();
    card(el, 'pedidos').click();
    fixture.detectChanges();

    const heads = [...el.querySelectorAll<HTMLElement>('.db-fields th')];
    expect(getComputedStyle(heads[0]).position).toBe('sticky');
    expect(heads[2].title).toContain('nulo');
    expect(heads[3].title).toContain('Chave primária');
  });

  // UX-G-B21
  it('o bloco esmaecido troca a opacidade pela cor de apoio', fakeAsync(() => {
    const { fixture, el } = render();
    const search = el.querySelector<HTMLInputElement>('#db-search')!;
    search.value = 'cpf';
    search.dispatchEvent(new Event('input'));
    tick(LIST_ANNOUNCE_DELAY_MS);
    fixture.detectChanges();

    const dim = card(el, 'pedidos');
    expect(dim.classList).toContain('is-dim');
    expect(getComputedStyle(dim).opacity).toBe('1');
    expect(getComputedStyle(dim).color).toBe(getComputedStyle(dim.querySelector('.db-row-type')!).color);
  }));

  // UX-G-B01
  it('escolher a tabela pelo mapa leva o foco ao título do painel e "Fechar" devolve o foco ao cartão', fakeAsync(() => {
    const { fixture, el } = render();
    card(el, 'pedidos').focus();
    card(el, 'pedidos').click();
    fixture.detectChanges();
    tick();
    expect(document.activeElement).toBe(el.querySelector('.db-panel-head h2'));
    expect(document.activeElement!.textContent).toContain('pedidos');

    buttonByText(el, 'Fechar')!.click();
    fixture.detectChanges();
    tick();
    expect(document.activeElement).toBe(card(el, 'pedidos'));
  }));

  // UX-G-B03
  it('com erro ao ler o esquema, as abas ficam e a análise de consulta funciona sem esquema', () => {
    const { fixture, http, el } = render(null);
    expect(el.querySelector('[role="alert"]')!.textContent).toBeTruthy();
    expect(el.querySelectorAll('[role="tab"]').length).toBe(2);
    expect(buttonByText(el, 'Analisar uma consulta mesmo assim')).toBeDefined();

    fixture.componentRef.setInput('aba', DB_TAB_QUERY_PARAM);
    fixture.detectChanges();
    const analysis = el.querySelector<HTMLElement>('app-query-analysis')!;
    expect(analysis.hidden).toBeFalse();
    expect(el.querySelector('.page-state')).toBeNull();
    expect(el.querySelector('.db-schema-off')!.textContent).toContain('O esquema do banco não foi lido');

    const box = el.querySelector<HTMLTextAreaElement>('#qa-sql')!;
    box.value = 'SELECT 1';
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('.qa-actions .btn')!.click();
    http.expectOne('/api/repositories/loja/schema/analyze').flush({ dialect: 0, statements: 1, findings: [], tables: [], error: null });
    fixture.detectChanges();
    expect(el.querySelector('#qa-result-title')).not.toBeNull();
  });

  // UX-G-B07
  it('o zoom desce até 25% e "Ajustar" cabe o mapa na largura do contêiner', () => {
    const { fixture, el } = render();
    const zoomOut = el.querySelector<HTMLButtonElement>('button[aria-label="Diminuir o mapa"]')!;
    for (let i = 0; i < 3; i++) {
      zoomOut.click();
      fixture.detectChanges();
    }
    expect(el.querySelector('.db-zoom-tools')!.textContent).toContain('25%');
    expect(zoomOut.disabled).toBeTrue();

    buttonByText(el, 'Ajustar')!.click();
    fixture.detectChanges();
    const container = el.querySelector<HTMLElement>('.db-scroll')!.clientWidth;
    const mapWidth = parseFloat(el.querySelector<HTMLElement>('.db-canvas')!.style.width);
    const expected = Math.min(1, Math.max(0.25, Math.floor((container / mapWidth) * 100) / 100));
    expect(el.querySelector<HTMLElement>('.db-canvas')!.style.transform).toBe(`scale(${expected})`);
  });

  // UX-G-B14
  it('o menu Exportar abre com o foco no primeiro item, anda pelas setas e fecha ao sair com Tab', fakeAsync(() => {
    const { fixture, el } = render();
    el.querySelector<HTMLButtonElement>('#db-export-button')!.click();
    fixture.detectChanges();
    tick();
    const items = [...el.querySelectorAll<HTMLButtonElement>('#db-export-menu button')];
    const menu = el.querySelector<HTMLElement>('#db-export-menu')!;
    expect(document.activeElement).toBe(items[0]);

    menu.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(items[1]);
    menu.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true, cancelable: true }));
    menu.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(items[items.length - 1]);

    items[1].dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: items[2] }));
    fixture.detectChanges();
    expect(el.querySelector('#db-export-menu')).not.toBeNull();
    items[items.length - 1].dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: document.body }));
    fixture.detectChanges();
    expect(el.querySelector('#db-export-menu')).toBeNull();
  }));

  // UX-G-B23
  it('"Ver no mapa" abre o painel com "Voltar à análise", que volta à consulta com o resultado intacto', fakeAsync(() => {
    const { fixture, http, el } = render();
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    fixture.componentRef.setInput('aba', DB_TAB_QUERY_PARAM);
    fixture.detectChanges();
    const box = el.querySelector<HTMLTextAreaElement>('#qa-sql')!;
    box.value = 'SELECT * FROM pedidos';
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('.qa-actions .btn')!.click();
    http.expectOne('/api/repositories/loja/schema/analyze').flush({
      dialect: 0, statements: 1, findings: [], tables: [{ name: 'pedidos', alias: null, inSchema: true }], error: null,
    });
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('.qa-tables .source-link')!.click();
    fixture.detectChanges();
    fixture.componentRef.setInput('aba', undefined);
    fixture.detectChanges();

    const back = buttonByText(el.querySelector<HTMLElement>('.db-panel')!, 'Voltar à análise')!;
    expect(back).toBeDefined();
    navigate.calls.reset();
    back.click();
    fixture.detectChanges();
    tick();
    expect(navigate).toHaveBeenCalledWith([], jasmine.objectContaining({ queryParams: { aba: DB_TAB_QUERY_PARAM } }));
    fixture.componentRef.setInput('aba', DB_TAB_QUERY_PARAM);
    fixture.detectChanges();
    expect(el.querySelector('#qa-result-title')).not.toBeNull();
    expect(buttonByText(el, 'Voltar à análise')).toBeUndefined();
  }));

  // UX-G-B23
  it('abrir uma tabela pelo mapa, sem vir da análise, não mostra "Voltar à análise"', () => {
    const { fixture, el } = render();
    card(el, 'pedidos').click();
    fixture.detectChanges();
    expect(buttonByText(el, 'Voltar à análise')).toBeUndefined();
  });

  // UX-G-B25
  it('"Levantar o fluxo deste repositório" leva o repositório e a origem banco para a tela inicial', () => {
    const { fixture, el } = render(lojaSchema(), undefined, { uses: [], findings: [] });
    card(el, 'pedidos').click();
    fixture.detectChanges();
    const link = [...el.querySelectorAll<HTMLAnchorElement>('.db-panel a')].find((a) => a.textContent!.includes('Levantar o fluxo deste repositório'))!;
    expect(link.getAttribute('href')).toBe(`/?repositorio=loja&${HOME_FROM_DB_PARAM}=${HOME_FROM_DB_VALUE}`);
  });
});
