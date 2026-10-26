import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DialogDirective } from './dialog.directive';

@Component({
  imports: [DialogDirective],
  template: `
    <button id="opener" type="button" (click)="open.set(true)">Abrir</button>
    <p id="fundo">Fundo</p>
    @if (open()) {
      <div class="overlay" appDialog>
        <section role="dialog" aria-modal="true">
          <button type="button" class="btn-icon" (click)="open.set(false)">×</button>
          <button id="primeiro" type="button">Primeiro</button>
          <button id="ultimo" type="button">Último</button>
        </section>
      </div>
    }
  `,
})
class HostComponent {
  readonly open = signal(false);
}

describe('DialogDirective', () => {
  function setup() {
    TestBed.configureTestingModule({ imports: [HostComponent] });
    const fixture = TestBed.createComponent(HostComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const opener = el.querySelector('#opener') as HTMLButtonElement;
    opener.focus();
    opener.click();
    fixture.detectChanges();
    return { fixture, el, opener };
  }

  it('leva o foco para dentro, deixa o fundo inert e devolve o foco a quem abriu', () => {
    const { fixture, el, opener } = setup();

    expect(document.activeElement?.id).toBe('primeiro');
    expect((el.querySelector('#fundo') as HTMLElement).inert).toBeTrue();

    fixture.componentInstance.open.set(false);
    fixture.detectChanges();

    expect((el.querySelector('#fundo') as HTMLElement).inert).toBeFalse();
    expect(document.activeElement).toBe(opener);
    fixture.nativeElement.remove();
  });

  it('Tab no último volta ao primeiro e Shift+Tab no primeiro vai ao último', () => {
    const { el, fixture } = setup();
    const overlay = el.querySelector('.overlay') as HTMLElement;
    const last = el.querySelector('#ultimo') as HTMLElement;

    last.focus();
    overlay.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
    expect(document.activeElement?.classList.contains('btn-icon')).toBeTrue();

    overlay.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true }));
    expect(document.activeElement).toBe(last);
    fixture.nativeElement.remove();
  });
});
