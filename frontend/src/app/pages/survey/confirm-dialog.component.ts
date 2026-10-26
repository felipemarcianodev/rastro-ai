import { Component, input, output } from '@angular/core';
import { DialogDirective } from '../../core/dialog.directive';

// Confirmação de ação destrutiva (descartar rascunho). Cancelar é o caminho seguro e vem primeiro.
@Component({
  selector: 'app-confirm-dialog',
  imports: [DialogDirective],
  template: `
    <div class="overlay" appDialog (click)="cancel.emit()">
      <section class="dialog is-narrow" role="alertdialog" aria-modal="true" aria-labelledby="confirm-title" aria-describedby="confirm-text" (click)="$event.stopPropagation()">
        <h2 id="confirm-title">{{ title() }}</h2>
        <p id="confirm-text">{{ text() }}</p>
        <footer class="dialog-foot">
          <button type="button" class="btn btn-g" (click)="cancel.emit()" [disabled]="busy()">Cancelar</button>
          <button type="button" class="btn btn-danger" (click)="confirm.emit()" [disabled]="busy()">{{ confirmLabel() }}</button>
        </footer>
      </section>
    </div>
  `,
})
export class ConfirmDialogComponent {
  readonly title = input.required<string>();
  readonly text = input.required<string>();
  readonly confirmLabel = input.required<string>();
  // Ação em andamento: os botões ficam parados até a resposta.
  readonly busy = input(false);
  readonly confirm = output<void>();
  readonly cancel = output<void>();
}
