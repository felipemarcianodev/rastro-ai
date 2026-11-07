import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DialogDirective } from './dialog.directive';

@Component({
  imports: [DialogDirective],
  template: `
    <div class="overlay" appDialog>
      <section role="dialog" aria-modal="true">
        <button id="primeiro" type="button">Primeiro</button>
        <button id="fixo" type="button" style="position: fixed; bottom: 0; right: 0">Fixo</button>
      </section>
    </div>
  `,
})
class FixedHostComponent {}

describe('DialogDirective com elemento position: fixed', () => {
  // UX-G-L25
  it('mantém o elemento fixo no ciclo do Tab (offsetParent é null nele)', () => {
    TestBed.configureTestingModule({ imports: [FixedHostComponent] });
    const fixture = TestBed.createComponent(FixedHostComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const fixo = el.querySelector('#fixo') as HTMLButtonElement;
    fixo.focus();
    const event = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
    fixo.dispatchEvent(event);
    expect(event.defaultPrevented).toBeTrue();
    expect(document.activeElement).toBe(el.querySelector('#primeiro'));
    fixture.destroy();
    el.remove();
  });
});
