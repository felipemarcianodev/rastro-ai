import { Component, ElementRef, Injector, OnInit, afterNextRender, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { DialogDirective } from '../../core/dialog.directive';
import { LanguageService } from '../../core/i18n/i18n';
import { BOARD_NAME_MAX, BoardDetail, BoardSummary } from '../../core/models';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';

// Valor da opção "Criar um quadro novo" na lista de quadros.
export const NEW_BOARD_CHOICE = 'new';

// "Adicionar a um quadro" (Fase 60), na tela do levantamento: escolhe um quadro que já existe (a faixa entra no fim)
// ou cria um quadro novo só com este levantamento.
@Component({
  selector: 'app-add-to-board-dialog',
  imports: [FormsModule, RouterLink, TranslatePipe, DialogDirective],
  template: `
    <div class="overlay" appDialog (click)="close()" (dialogClose)="close()">
      <section class="dialog is-narrow" role="dialog" aria-modal="true" aria-labelledby="add-board-title" (click)="$event.stopPropagation()">
        <h2 id="add-board-title">{{ 'boards.addTo.title' | translate }}</h2>
        @if (done(); as board) {
          <p class="note-ok" role="status">{{ 'boards.addTo.done' | translate: { name: board.name } }}</p>
          <footer class="dialog-foot">
            <button type="button" class="btn btn-g" (click)="cancel.emit()">{{ 'common.close' | translate }}</button>
            <a class="btn btn-p" [routerLink]="['/boards', board.id]" (click)="cancel.emit()">{{ 'boards.addTo.open' | translate }}</a>
          </footer>
        } @else {
          <p class="muted">{{ 'boards.addTo.intro' | translate: { entryPoint: entryPoint() } }}</p>
          @if (loadError(); as message) {
            <div class="notice is-error" role="alert">
              <p>{{ message }}</p>
              <button type="button" class="btn btn-g btn-small" (click)="load()">{{ 'common.retry' | translate }}</button>
            </div>
          } @else if (boards() === null) {
            <p class="muted" role="status">{{ 'boards.loading' | translate }}</p>
          } @else {
            <form class="form-stack" (ngSubmit)="submit()" novalidate>
              <div class="field">
                <label class="field-label" for="add-board-choice">{{ 'boards.addTo.choose' | translate }}</label>
                <select id="add-board-choice" class="input" name="choice" [(ngModel)]="choice">
                  @for (board of boards(); track board.id) {
                    <option [value]="board.id">{{ board.name }}</option>
                  }
                  <option [value]="newChoice">{{ 'boards.addTo.newOption' | translate }}</option>
                </select>
                @if (boards()!.length === 0) {
                  <span class="field-help">{{ 'boards.addTo.noBoards' | translate }}</span>
                }
              </div>
              @if (choice === newChoice) {
                <div class="field">
                  <label class="field-label" for="add-board-name">{{ 'boards.addTo.newName' | translate }}</label>
                  <input id="add-board-name" class="input" name="name" [(ngModel)]="name" [maxLength]="nameMax" autocomplete="off" [attr.aria-invalid]="!!nameError()" [attr.aria-describedby]="nameError() ? 'add-board-name-error' : null" />
                  @if (nameError(); as message) {
                    <span id="add-board-name-error" class="field-error" role="alert">{{ message }}</span>
                  }
                </div>
              }
              @if (error(); as message) {
                <p class="field-error" role="alert">{{ message }}</p>
              }
              <footer class="dialog-foot">
                <button type="button" class="btn btn-g" (click)="close()" [disabled]="submitting()">{{ 'common.cancel' | translate }}</button>
                <button type="submit" class="btn btn-p" [disabled]="submitting()">{{ submitLabel() | translate: { name: chosenName() } }}</button>
              </footer>
            </form>
          }
        }
      </section>
    </div>
  `,
})
export class AddToBoardDialogComponent implements OnInit {
  private readonly api = inject(RastroApi);
  private readonly i18n = inject(LanguageService);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly surveyId = input.required<string>();
  readonly entryPoint = input.required<string>();
  readonly cancel = output<void>();

  protected readonly boards = signal<BoardSummary[] | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly nameError = signal<string | null>(null);
  protected readonly done = signal<BoardDetail | null>(null);
  protected readonly newChoice = NEW_BOARD_CHOICE;
  protected readonly nameMax = BOARD_NAME_MAX;
  protected choice = NEW_BOARD_CHOICE;
  protected name = '';

  ngOnInit(): void {
    this.load();
  }

  // O botão diz o destino: "Adicionar a "{nome}"" ou "Criar quadro e adicionar", para não enviar ao quadro errado.
  protected chosenName(): string {
    return this.boards()?.find((b) => b.id === this.choice)?.name ?? '';
  }

  protected submitLabel(): string {
    if (this.submitting()) return 'boards.addTo.adding';
    return this.choice === NEW_BOARD_CHOICE ? 'boards.addTo.submitNew' : 'boards.addTo.submitTo';
  }

  protected load(): void {
    this.loadError.set(null);
    this.api.boards().subscribe({
      next: (boards) => {
        this.boards.set(boards);
        this.choice = boards[0]?.id ?? NEW_BOARD_CHOICE;
      },
      error: (error) => this.loadError.set(apiErrorMessage(error)),
    });
  }

  protected close(): void {
    if (!this.submitting()) this.cancel.emit();
  }

  protected submit(): void {
    this.error.set(null);
    const creating = this.choice === NEW_BOARD_CHOICE;
    const name = this.name.trim();
    this.nameError.set(creating && !name ? this.i18n.t('boards.create.errors.name') : null);
    if (this.nameError()) {
      afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('#add-board-name')?.focus(), { injector: this.injector });
      return;
    }
    this.submitting.set(true);
    const request = creating ? this.api.createBoard({ name, surveyIds: [this.surveyId()] }) : this.api.addBoardLane(this.choice, this.surveyId());
    request.subscribe({
      next: (board) => {
        this.submitting.set(false);
        this.done.set(board);
        // O botão de enviar sumiu com o formulário: o foco vai para "Abrir o quadro".
        afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('a.btn-p')?.focus(), { injector: this.injector });
      },
      error: (error) => {
        this.submitting.set(false);
        this.error.set(apiErrorMessage(error));
      },
    });
  }
}
