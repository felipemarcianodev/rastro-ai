import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { LayoutDirection } from '../../core/graph/layout';
import { DRAWING_LABEL_KEY, DRAWING_SWITCHER_ORDER, DRAWING_TITLE_KEY, DrawingSwitcherComponent } from './drawing-switcher.component';

@Component({
  imports: [DrawingSwitcherComponent],
  template: `<app-drawing-switcher [active]="active()" [restricted]="restricted()" [vertical]="vertical()" (pick)="picked.push($event)" />`,
})
class HostComponent {
  readonly active = signal(LayoutDirection.Horizontal);
  readonly restricted = signal(false);
  readonly vertical = signal(false);
  readonly picked: LayoutDirection[] = [];
}

function setup() {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const buttons = () => [...el.querySelectorAll<HTMLButtonElement>('.drawing-switcher-item')];
  return { fixture, el, host: fixture.componentInstance, buttons };
}

describe('DrawingSwitcherComponent', () => {
  // WEB-DRSW-001
  it('barra_temUmBotaoPorTipoNaOrdemEComAtualMarcada', () => {
    const { buttons, host } = setup();

    expect(buttons().length).toBe(6);
    expect(DRAWING_SWITCHER_ORDER.length).toBe(Object.keys(DRAWING_LABEL_KEY).length);
    expect(DRAWING_SWITCHER_ORDER.length).toBe(Object.keys(DRAWING_TITLE_KEY).length);
    expect(buttons().map((b) => b.getAttribute('aria-pressed'))).toEqual(['false', 'true', 'false', 'false', 'false', 'false']);
    expect(buttons().every((b) => b.getAttribute('aria-label') !== null && b.title !== '')).toBeTrue();
    expect(host.picked).toEqual([]);
  });

  // WEB-DRSW-002
  it('clique_emiteOTipoEscolhido_eNaoEmiteOAtual', () => {
    const { buttons, host } = setup();

    buttons()[DRAWING_SWITCHER_ORDER.indexOf(LayoutDirection.Horizontal)].click();
    buttons()[DRAWING_SWITCHER_ORDER.indexOf(LayoutDirection.Tree)].click();
    buttons()[DRAWING_SWITCHER_ORDER.indexOf(LayoutDirection.Consolidated)].click();

    expect(host.picked).toEqual([LayoutDirection.Tree, LayoutDirection.Consolidated]);
  });

  // WEB-DRSW-003
  it('tabindex_ficaSoNoBotaoAtivoEAcompanhaATroca', () => {
    const { buttons, host, fixture } = setup();
    expect(buttons().map((b) => b.getAttribute('tabindex'))).toEqual(['-1', '0', '-1', '-1', '-1', '-1']);

    host.active.set(LayoutDirection.Map);
    fixture.detectChanges();

    expect(buttons().map((b) => b.getAttribute('tabindex'))).toEqual(['-1', '-1', '-1', '0', '-1', '-1']);
  });

  // WEB-DRSW-004
  it('setas_andamEntreOsBotoesSemAtivar_eDaVoltaNasPontas', () => {
    const { buttons, host } = setup();
    const press = (key: string) => document.activeElement!.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
    buttons()[1].focus();

    press('ArrowRight');
    expect(document.activeElement).toBe(buttons()[2]);
    press('ArrowLeft');
    press('ArrowLeft');
    expect(document.activeElement).toBe(buttons()[0]);
    press('ArrowLeft');
    expect(document.activeElement).toBe(buttons()[5]);
    press('ArrowRight');
    expect(document.activeElement).toBe(buttons()[0]);
    expect(host.picked).toEqual([]);
  });

  // WEB-DRSW-005
  it('restrita_desligaOsDesenhosForaDoCanvas_eAsSetasPulam', () => {
    const { buttons, host, fixture } = setup();
    host.restricted.set(true);
    fixture.detectChanges();

    expect(buttons().map((b) => b.disabled)).toEqual([false, false, false, true, true, true]);
    buttons()[DRAWING_SWITCHER_ORDER.indexOf(LayoutDirection.Map)].click();
    expect(host.picked).toEqual([]);
    buttons()[2].focus();
    buttons()[2].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(buttons()[0]);
  });

  // WEB-DRSW-006
  it('vertical_marcaAClasseDeColuna', () => {
    const { el, host, fixture } = setup();
    expect(el.querySelector('.drawing-switcher')!.classList.contains('is-vertical')).toBeFalse();
    host.vertical.set(true);
    fixture.detectChanges();
    expect(el.querySelector('.drawing-switcher')!.classList.contains('is-vertical')).toBeTrue();
  });
});
