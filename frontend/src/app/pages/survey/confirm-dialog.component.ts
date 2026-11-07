import { AfterViewInit, Component, ElementRef, computed, input, output, viewChild } from '@angular/core';
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
    <div class="overlay" appDialog (click)="cancel.emit()" (dialogClose)="cancel.emit()">
      <section class="dialog is-narrow" role="alertdialog" aria-modal="true" aria-labelledby="confirm-title" aria-describedby="confirm-text" (click)="$event.stopPropagation()">
        <h2 id="confirm-title">{{ title() }}</h2>
        <p id="confirm-text">{{ text() }}</p>
        <footer class="dialog-foot">
          <button #cancelButton type="button" class="btn btn-g" (click)="cancel.emit()" [disabled]="busy()">{{ cancelLabel() }}</button>
          <button type="button" [class]="'btn ' + toneClass()" (click)="confirm.emit()" [disabled]="busy()">{{ confirmLabel() }}</button>
        </footer>
      </section>
    </div>
  `,
})
export class ConfirmDialogComponent implements AfterViewInit {
  private readonly cancelButton = viewChild<ElementRef<HTMLButtonElement>>('cancelButton');
  readonly title = input.required<string>();
  readonly text = input.required<string>();
  readonly confirmLabel = input.required<string>();
  // Rótulo do caminho seguro; "Cancelar" salvo quando a pergunta pede outro (ex.: "Ficar").
  readonly cancelLabel = input('Cancelar');
  // Ação em andamento: os botões ficam parados até a resposta.
  readonly busy = input(false);
  readonly tone = input<ConfirmTone>(ConfirmTone.Danger);
  protected readonly toneClass = computed(() => CONFIRM_TONE_BUTTON[this.tone()]);
  readonly confirm = output<void>();
  readonly cancel = output<void>();

  // UX-G-L22: o foco inicial é sempre o "Cancelar" (o caminho seguro), sem depender da ordem do DOM.
  // O hook do componente roda depois do da diretiva do diálogo, que só escolhe o primeiro foco.
  ngAfterViewInit(): void {
    this.cancelButton()?.nativeElement.focus();
  }
}
