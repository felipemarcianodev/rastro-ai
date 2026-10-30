import { TestBed } from '@angular/core/testing';
import { ConfirmDialogComponent, ConfirmTone } from './confirm-dialog.component';

describe('ConfirmDialogComponent', () => {
  it('mostra a contagem e só confirma pelo botão de descartar', () => {
    TestBed.configureTestingModule({ imports: [ConfirmDialogComponent] });
    const fixture = TestBed.createComponent(ConfirmDialogComponent);
    fixture.componentRef.setInput('title', 'Descartar o rascunho?');
    fixture.componentRef.setInput('text', 'Apaga 7 mudanças.');
    fixture.componentRef.setInput('confirmLabel', 'Descartar 7 mudanças');
    const events: string[] = [];
    fixture.componentInstance.confirm.subscribe(() => events.push('confirm'));
    fixture.componentInstance.cancel.subscribe(() => events.push('cancel'));
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('[role="alertdialog"]')?.textContent).toContain('Apaga 7 mudanças.');
    const [cancel, confirm] = Array.from(el.querySelectorAll('button'));
    expect(cancel.textContent).toContain('Cancelar');
    cancel.click();
    confirm.click();
    expect(events).toEqual(['cancel', 'confirm']);
  });

  // WEB-CONF-002
  it('tom_padraoEPerigo_primarioUsaOBotaoPrimario', () => {
    TestBed.configureTestingModule({ imports: [ConfirmDialogComponent] });
    const fixture = TestBed.createComponent(ConfirmDialogComponent);
    fixture.componentRef.setInput('title', 'T');
    fixture.componentRef.setInput('text', 'x');
    fixture.componentRef.setInput('confirmLabel', 'Ok');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const confirm = () => el.querySelectorAll('button')[1];

    expect(confirm().classList.contains('btn-danger')).toBeTrue();
    fixture.componentRef.setInput('tone', ConfirmTone.Primary);
    fixture.detectChanges();
    expect(confirm().classList.contains('btn-p')).toBeTrue();
    expect(confirm().classList.contains('btn-danger')).toBeFalse();
    expect(confirm().classList.contains('btn')).toBeTrue();
  });
});
