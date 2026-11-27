import { Component, signal } from '@angular/core';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { LayoutDirection } from '../../core/graph/layout';
import { DRAWIO_ACTION_LOAD, DRAWIO_LOAD_TIMEOUT_MS, DRAWIO_PATH, DrawioViewComponent } from './drawio-view.component';

@Component({
  imports: [DrawioViewComponent],
  template: `<app-drawio-view [xml]="xml()" (selectNode)="picked.push($event)" (changed)="changes.push($event)" (directionChange)="directions.push($event)" />`,
})
class HostComponent {
  readonly xml = signal('<mxfile>um</mxfile>');
  readonly picked: string[] = [];
  readonly changes: string[] = [];
  readonly directions: LayoutDirection[] = [];
}

function setup() {
  TestBed.configureTestingModule({ imports: [HostComponent] });
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
  return { fixture, el, iframe, host: fixture.componentInstance, view, posted, send };
}

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
});
