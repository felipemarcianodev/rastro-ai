import { Component, computed, input, output } from '@angular/core';
import { DialogDirective } from '../../core/dialog.directive';

// Tom da confirmação: Danger para ação destrutiva (padrão), Primary para ação segura.
export enum ConfirmTone {
  Danger = 0,
  Primary = 1,
}

export const CONFIRM_TONE_BUTTON: Record<ConfirmTone, string> = {
  [ConfirmTone.Danger]: 'btn-danger',
  [ConfirmTone.Primary]: 'btn-p',
};

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
          <button type="button" [class]="'btn ' + toneClass()" (click)="confirm.emit()" [disabled]="busy()">{{ confirmLabel() }}</button>
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
  readonly tone = input<ConfirmTone>(ConfirmTone.Danger);
  protected readonly toneClass = computed(() => CONFIRM_TONE_BUTTON[this.tone()]);
  readonly confirm = output<void>();
  readonly cancel = output<void>();
}
