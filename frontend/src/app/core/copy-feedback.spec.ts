import { fakeAsync, tick } from '@angular/core/testing';
import { CopyFeedback } from './copy-feedback';

describe('CopyFeedback', () => {
  it('mostra Copiado e volta ao rótulo depois de 2 s', fakeAsync(() => {
    spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());
    const feedback = new CopyFeedback();

    feedback.copy('texto');
    tick();
    expect(feedback.label('Copiar')).toBe('Copiado');
    tick(2000);
    expect(feedback.label('Copiar')).toBe('Copiar');
  }));

  it('avisa quando o navegador recusa a cópia', fakeAsync(() => {
    spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.reject(new Error('negado')));
    const feedback = new CopyFeedback();

    feedback.copy('texto');
    tick();
    expect(feedback.label('Copiar')).toBe('Não deu para copiar');
    tick(2000);
  }));
});
