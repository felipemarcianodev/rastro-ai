import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { DrawioBaseline } from '../../core/graph/drawio-merge';
import { LayoutDirection } from '../../core/graph/layout';
import { DrawioSource } from '../../core/models';
import { DRAWIO_ACTION_LOAD, DRAWIO_LOAD_TIMEOUT_MS, DRAWIO_PATH, DRAWIO_SAVE_DEBOUNCE_MS, DRAWIO_SAVED_MS, DrawioViewComponent } from './drawio-view.component';

@Component({
  imports: [DrawioViewComponent],
  template: `<app-drawio-view [xml]="xml()" [surveyId]="surveyId()" [source]="source()" [baseline]="baseline()" (selectNode)="picked.push($event)" (changed)="changes.push($event)" (directionChange)="directions.push($event)" (reorganized)="reorganized = reorganized + 1" />`,
})
class HostComponent {
  readonly xml = signal('<mxfile>um</mxfile>');
  readonly picked: string[] = [];
  readonly changes: string[] = [];
  readonly directions: LayoutDirection[] = [];
  readonly surveyId = signal<string | null>(null);
  readonly source = signal(DrawioSource.Graph);
  readonly baseline = signal<DrawioBaseline | null>(null);
  reorganized = 0;
}

function setup() {
  TestBed.configureTestingModule({ imports: [HostComponent], providers: [provideHttpClient(), provideHttpClientTesting()] });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const iframe = el.querySelector('iframe')!;
  const view = fixture.debugElement.children[0].componentInstance as DrawioViewComponent & { post: (message: Record<string, unknown>) => void };
  const posted: Record<string, unknown>[] = [];
  spyOn(view, 'post').and.callFake((message) => void posted.push(message));
  // Mensagem como o editor a manda: texto JSON, da janela do iframe, na mesma origem.
  const send = (data: unknown, init: { origin?: string; source?: MessageEventSource | null } = {}) => {
    window.dispatchEvent(new MessageEvent('message', { data: typeof data === 'string' ? data : JSON.stringify(data), origin: init.origin ?? window.location.origin, source: 'source' in init ? init.source : iframe.contentWindow }));
    fixture.detectChanges();
  };
  return { fixture, el, iframe, host: fixture.componentInstance, view, posted, send, http: TestBed.inject(HttpTestingController) };
}

// Um .drawio como o editor o devolve no autosave: balões do código (rastroId), uma ligação gerada e células do usuário.
function drawing(options: { b1?: { x: number; y: number; text?: string }; b2?: boolean; extra?: string } = {}): string {
  const b1 = options.b1 ?? { x: 40, y: 40 };
  const node = (id: string, rastroId: string, label: string, x: number, y: number) =>
    `<UserObject id="${id}" label="${label.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;')}" rastroId="${rastroId}" rastroNode="m0"><mxCell style="rounded=1" vertex="1" parent="1"><mxGeometry x="${x}" y="${y}" width="220" height="88" as="geometry"/></mxCell></UserObject>`;
  return (
    '<mxfile><diagram id="rastro" name="x"><mxGraphModel><root><mxCell id="0"/><mxCell id="1" parent="0"/>' +
    node('n0', 'b1', b1.text ?? 'Pedido recebido', b1.x, b1.y) +
    (options.b2 === false ? '' : node('n1', 'b2', 'Pedido válido', 40, 200)) +
    '<mxCell id="e0" value="" edge="1" parent="1" source="n0" target="n1"><mxGeometry relative="1" as="geometry"/></mxCell>' +
    (options.extra ?? '') +
    '</root></mxGraphModel></diagram></mxfile>'
  );
}

const NOTE = '<mxCell id="abc-1" value="Atenção" style="text;html=1" vertex="1" parent="1"><mxGeometry x="300" y="120" width="120" height="30" as="geometry"/></mxCell>';
const BASELINE: DrawioBaseline = {
  ids: new Set(['b1', 'b2']),
  positions: new Map([['b1', { x: 40, y: 40 }], ['b2', { x: 40, y: 200 }]]),
  texts: new Map([['b1', 'Pedido recebido'], ['b2', 'Pedido válido']]),
  shift: { x: 40, y: 30 },
};

describe('DrawioViewComponent (Fase 64)', () => {
  // WEB-DRIO-001
  it('iframe_temTituloEUrlDeMesmaOrigemComIdiomaETemaNoModoEmbed', () => {
    const { iframe } = setup();

    const url = new URL(iframe.getAttribute('src')!, window.location.origin);
    expect(url.origin).toBe(window.location.origin);
    expect(url.pathname).toBe(DRAWIO_PATH);
    for (const [name, value] of Object.entries({ embed: '1', proto: 'json', offline: '1', noSaveBtn: '1', noExitBtn: '1', saveAndExit: '0', configure: '0', libraries: '0' })) {
      expect(url.searchParams.get(name)).withContext(name).toBe(value);
    }
    expect(['en', 'pt-br', 'es']).toContain(url.searchParams.get('lang')!);
    expect(iframe.title).not.toBe('');
  });

  // WEB-DRIO-002
  it('init_mandaOLoadComOXmlRecebido', () => {
    const { posted, send, host, fixture } = setup();

    send({ event: 'init' });
    expect(posted).toEqual([{ action: DRAWIO_ACTION_LOAD, xml: '<mxfile>um</mxfile>', autosave: 1 }]);

    // O input mudou com o editor já aberto: manda o load de novo.
    host.xml.set('<mxfile>dois</mxfile>');
    fixture.detectChanges();
    expect(posted.length).toBe(2);
    expect(posted[1]['xml']).toBe('<mxfile>dois</mxfile>');
  });

  // WEB-DRIO-003
  it('mensagem_soDaMesmaOrigemEDoProprioIframe', () => {
    const { posted, send, host, view } = setup();

    send({ event: 'init' }, { origin: 'https://exemplo.com' });
    send({ event: 'init' }, { source: window });
    send({ event: 'init' }, { source: null });
    send({ event: 'rastro-select', rastroNode: 'x' }, { origin: 'https://exemplo.com' });
    send({ event: 'autosave', xml: '<mxfile>de fora</mxfile>' }, { source: window });
    send('isto não é json');
    send('"texto"');
    send({ semEvento: true });

    expect(posted).toEqual([]);
    expect(host.picked).toEqual([]);
    expect(host.changes).toEqual([]);
    expect(view.exportDrawio()).toBe('<mxfile>um</mxfile>');
  });

  // WEB-DRIO-004
  it('rastroSelect_emiteOSelectNodeComONoDoRastro', () => {
    const { host, send } = setup();

    send({ event: 'rastro-select', rastroId: 'b2', rastroNode: 'm0' });
    send({ event: 'rastro-select', rastroId: 'seta' });

    expect(host.picked).toEqual(['m0']);
  });

  // WEB-DRIO-005
  it('autosave_guardaOXmlParaExportarEEmiteChanged', () => {
    const { host, send, view } = setup();

    send({ event: 'autosave', xml: '<mxfile>movido</mxfile>' });

    expect(view.exportDrawio()).toBe('<mxfile>movido</mxfile>');
    expect(host.changes).toEqual(['<mxfile>movido</mxfile>']);
  });

  // WEB-DRIO-006
  it('carregando_ateOLoad_eDepoisSomeOAviso', () => {
    const { el, send } = setup();

    expect(el.querySelector('.drawio-status')?.getAttribute('role')).toBe('status');
    send({ event: 'init' });
    send({ event: 'load' });

    expect(el.querySelector('.drawio-status')).toBeNull();
  });

  // WEB-DRIO-007
  it('semResposta_mostraErroComTentarDeNovo', fakeAsync(() => {
    const { fixture, el } = setup();

    tick(DRAWIO_LOAD_TIMEOUT_MS);
    fixture.detectChanges();

    const notice = el.querySelector('.drawio-status.is-error')!;
    expect(notice.getAttribute('role')).toBe('alert');
    expect(notice.querySelector('button')).not.toBeNull();

    notice.querySelector('button')!.click();
    fixture.detectChanges();
    expect(el.querySelector('.drawio-status.is-error')).toBeNull();
    expect(el.querySelector('.drawio-status')?.getAttribute('role')).toBe('status');
    tick(DRAWIO_LOAD_TIMEOUT_MS);
  }));

  // WEB-DRIO-008
  it('barraDeDesenhos_temODrawioMarcadoEEmiteATroca', () => {
    const { el, host } = setup();

    const pressed = [...el.querySelectorAll<HTMLButtonElement>('.drawing-switcher-item')].filter((b) => b.getAttribute('aria-pressed') === 'true');
    expect(pressed.length).toBe(1);
    expect(pressed[0]).toBe(el.querySelector<HTMLButtonElement>('.drawing-switcher-item:last-child')!);
    el.querySelector<HTMLButtonElement>('.drawing-switcher-item')!.click();

    expect(host.directions.length).toBe(1);
  });

  // WEB-DRIO-030 (Fase 65)
  it('autosave_grava_depoisDe800ms_umaSoVezComOUltimoXml', fakeAsync(() => {
    const { fixture, host, send, http, el } = setup();
    host.surveyId.set('s1');
    host.source.set(DrawioSource.Map);
    fixture.detectChanges();

    send({ event: 'autosave', xml: drawing({ b1: { x: 50, y: 50 } }) });
    tick(DRAWIO_SAVE_DEBOUNCE_MS - 1);
    http.expectNone(() => true);
    send({ event: 'autosave', xml: drawing({ b1: { x: 70, y: 90 }, extra: NOTE }) });
    tick(DRAWIO_SAVE_DEBOUNCE_MS - 1);
    http.expectNone(() => true);
    tick(1);

    const put = http.expectOne((r) => r.method === 'PUT' && r.url === '/api/surveys/s1/drawio/1');
    expect(put.request.body.cells).toEqual([
      { rastroId: 'b1', x: 70, y: 90, width: 220, height: 88 },
      { rastroId: 'b2', x: 40, y: 200, width: 220, height: 88 },
    ]);
    expect(put.request.body.annotations).toContain('Atenção');
    expect(put.request.body.annotations).not.toContain('rastroId');
    put.flush(null);
    fixture.detectChanges();
    expect(el.querySelector('.drawio-saved')!.getAttribute('role')).toBe('status');
    expect(el.querySelector('.drawio-saved')!.textContent!.trim()).not.toBe('');
    tick(DRAWIO_SAVED_MS);
    fixture.detectChanges();
    expect(el.querySelector('.drawio-saved')!.textContent!.trim()).toBe('');
    http.verify();
  }));

  // WEB-DRIO-031 (Fase 65)
  it('autosave_semLevantamento_naoGrava', fakeAsync(() => {
    const { send, http } = setup();

    send({ event: 'autosave', xml: drawing({ b1: { x: 50, y: 50 } }) });
    tick(DRAWIO_SAVE_DEBOUNCE_MS * 2);

    http.expectNone(() => true);
  }));

  // WEB-DRIO-032 (Fase 65)
  it('autosave_igualAoCarregado_naoGrava', fakeAsync(() => {
    const { fixture, host, send, http } = setup();
    host.surveyId.set('s1');
    host.xml.set(drawing());
    fixture.detectChanges();
    send({ event: 'init' });

    send({ event: 'autosave', xml: drawing() });
    tick(DRAWIO_SAVE_DEBOUNCE_MS);

    http.expectNone(() => true);
  }));

  // WEB-DRIO-033 (Fase 65)
  it('erroAoGravar_mostraOAvisoDeErroQueFicaAteFechar', fakeAsync(() => {
    const { fixture, host, send, http, el } = setup();
    host.surveyId.set('s1');
    fixture.detectChanges();

    send({ event: 'autosave', xml: drawing({ b1: { x: 50, y: 50 } }) });
    tick(DRAWIO_SAVE_DEBOUNCE_MS);
    http.expectOne((r) => r.method === 'PUT').flush({ code: 'DRAWIO_LAYOUT_INVALID' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    const toast = el.querySelector('.toast.is-error')!;
    expect(toast.getAttribute('role')).toBe('alert');
    tick(DRAWIO_SAVED_MS * 4);
    fixture.detectChanges();
    expect(el.querySelector('.toast.is-error')).not.toBeNull();
    toast.querySelector<HTMLButtonElement>('button')!.click();
    fixture.detectChanges();
    expect(el.querySelector('.toast.is-error')).toBeNull();
  }));

  // WEB-DRIO-034 (Fase 65)
  it('origemVisaoDeNegocio_moverGravaAPosicaoNaVisao_eEditarOTextoGravaOTextoDoUsuario', fakeAsync(() => {
    const { fixture, host, send, http } = setup();
    host.surveyId.set('s1');
    host.source.set(DrawioSource.Business);
    host.baseline.set(BASELINE);
    fixture.detectChanges();

    send({ event: 'autosave', xml: drawing({ b1: { x: 143, y: 137, text: 'Pedido <b>aceito</b><br><span style="font-family:Consolas,monospace">Loja · a.cs:1</span>' } }) });
    tick(DRAWIO_SAVE_DEBOUNCE_MS);

    http.expectOne((r) => r.method === 'PUT' && r.url === '/api/surveys/s1/drawio/2').flush(null);
    const layout = http.expectOne((r) => r.method === 'PUT' && r.url === '/api/surveys/s1/business/layout');
    // 143 - 40 = 103 e 137 - 30 = 107, encaixados na grade de 10.
    expect(layout.request.body).toEqual({ steps: [{ stepId: 'b1', x: 100, y: 110 }], edges: [] });
    layout.flush(null);
    const label = http.expectOne((r) => r.method === 'PUT' && r.url === '/api/surveys/s1/business/steps/b1/label');
    expect(label.request.body).toEqual({ text: 'Pedido aceito' });
    label.flush({});

    // O mesmo estado de novo: nada mais a gravar.
    send({ event: 'autosave', xml: drawing({ b1: { x: 143, y: 137, text: 'Pedido <b>aceito</b>' } }) });
    tick(DRAWIO_SAVE_DEBOUNCE_MS);
    http.expectNone((r) => r.url.includes('/business/'));
    http.verify();
  }));

  // WEB-DRIO-035 (Fase 65)
  it('origemDoGrafo_moverEEditar_naoTocamNaVisaoDeNegocio', fakeAsync(() => {
    const { fixture, host, send, http } = setup();
    host.surveyId.set('s1');
    host.baseline.set(BASELINE);
    fixture.detectChanges();

    send({ event: 'autosave', xml: drawing({ b1: { x: 143, y: 137, text: 'Outro texto' } }) });
    tick(DRAWIO_SAVE_DEBOUNCE_MS);

    http.expectOne((r) => r.method === 'PUT' && r.url === '/api/surveys/s1/drawio/0').flush(null);
    http.expectNone((r) => r.url.includes('/business/'));
  }));

  // WEB-DRIO-036 (Fase 65)
  it('balaoApagadoNoEditor_naoApagaNada_eAvisaQueOBalaoVolta', fakeAsync(() => {
    const { fixture, host, send, http, el } = setup();
    host.surveyId.set('s1');
    host.baseline.set(BASELINE);
    fixture.detectChanges();

    send({ event: 'autosave', xml: drawing({ b2: false }) });
    tick(DRAWIO_SAVE_DEBOUNCE_MS);
    const put = http.expectOne((r) => r.method === 'PUT');
    expect(put.request.body.cells.map((c: { rastroId: string }) => c.rastroId)).toEqual(['b1']);
    put.flush(null);
    fixture.detectChanges();

    expect(el.querySelector('.toast[role="status"]')).not.toBeNull();
    http.expectNone((r) => r.method === 'DELETE');

    send({ event: 'autosave', xml: drawing() });
    tick(DRAWIO_SAVE_DEBOUNCE_MS);
    http.expectOne((r) => r.method === 'PUT').flush(null);
    fixture.detectChanges();
    expect(el.querySelector('.toast[role="status"]')).toBeNull();
  }));

  // WEB-DRIO-037 (Fase 65)
  it('reorganizar_pedeConfirmacao_apagaOGuardadoEAvisaAPagina', () => {
    const { fixture, host, el, http } = setup();
    host.surveyId.set('s1');
    host.source.set(DrawioSource.Business);
    fixture.detectChanges();
    const reorganize = [...el.querySelectorAll<HTMLButtonElement>('.drawio-bar button')].find((b) => !b.classList.contains('drawing-switcher-item'))!;

    reorganize.click();
    fixture.detectChanges();
    expect(el.querySelector('app-confirm-dialog')).not.toBeNull();
    http.expectNone((r) => r.method === 'DELETE');
    el.querySelector<HTMLButtonElement>('app-confirm-dialog .btn-g')!.click();
    fixture.detectChanges();
    expect(el.querySelector('app-confirm-dialog')).toBeNull();
    http.expectNone((r) => r.method === 'DELETE');

    reorganize.click();
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('app-confirm-dialog .btn-danger')!.click();
    http.expectOne((r) => r.method === 'DELETE' && r.url === '/api/surveys/s1/drawio/2').flush(null);
    fixture.detectChanges();

    expect(host.reorganized).toBe(1);
    expect(el.querySelector('app-confirm-dialog')).toBeNull();
  });

  // WEB-DRIO-038 (Fase 65)
  it('reorganizar_comErro_naoAvisaAPagina_eMostraOErro', () => {
    const { fixture, host, el, http } = setup();
    host.surveyId.set('s1');
    fixture.detectChanges();
    [...el.querySelectorAll<HTMLButtonElement>('.drawio-bar button')].find((b) => !b.classList.contains('drawing-switcher-item'))!.click();
    fixture.detectChanges();

    el.querySelector<HTMLButtonElement>('app-confirm-dialog .btn-danger')!.click();
    http.expectOne((r) => r.method === 'DELETE').flush(null, { status: 500, statusText: 'Erro' });
    fixture.detectChanges();

    expect(host.reorganized).toBe(0);
    expect(el.querySelector('.toast.is-error')).not.toBeNull();
  });

  // WEB-DRIO-039 (Fase 65)
  it('reload_mandaOLoadMesmoComOMesmoXml', () => {
    const { host, send, view, posted, fixture } = setup();
    send({ event: 'init' });
    host.xml.set('<mxfile>um</mxfile>');
    fixture.detectChanges();
    expect(posted.length).toBe(1);

    view.reload('<mxfile>um</mxfile>');

    expect(posted.length).toBe(2);
    expect(posted[1]['xml']).toBe('<mxfile>um</mxfile>');
  });

  // WEB-DRIO-040 (Fase 65)
  it('aoSair_gravaOQueFicouPendente', fakeAsync(() => {
    const { fixture, host, send, http } = setup();
    host.surveyId.set('s1');
    fixture.detectChanges();

    send({ event: 'autosave', xml: drawing({ b1: { x: 55, y: 55 } }) });
    fixture.destroy();

    http.expectOne((r) => r.method === 'PUT' && r.url === '/api/surveys/s1/drawio/0').flush(null);
  }));
});
