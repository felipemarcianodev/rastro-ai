import { Component, ElementRef, Injector, OnInit, afterNextRender, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { DialogDirective } from '../../core/dialog.directive';
import { LanguageService } from '../../core/i18n/i18n';
import { shortDate } from '../../core/format';
import { BOARD_MAX_LANES, BOARD_NAME_MAX, BoardDetail, SurveyStatus, SurveySummary } from '../../core/models';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';

// Criar um quadro (Fase 60): o nome e de 1 a 12 levantamentos prontos. A ordem em que se escolhe vira a ordem das
// faixas. Serve à lista de levantamentos (com a escolha já feita) e à tela dos quadros (escolhe aqui dentro).
@Component({
  selector: 'app-board-create-dialog',
  imports: [FormsModule, TranslatePipe, DialogDirective],
  template: `
    <div class="overlay" appDialog (click)="close()" (dialogClose)="close()">
      <section class="dialog is-form" role="dialog" aria-modal="true" aria-labelledby="board-create-title" (click)="$event.stopPropagation()">
        <header class="dialog-head">
          <div>
            <h2 id="board-create-title">{{ 'boards.create.title' | translate }}</h2>
            <p class="muted">{{ 'boards.create.intro' | translate: { max: max } }}</p>
          </div>
          <button type="button" class="btn btn-g btn-small" data-dialog-close (click)="close()">{{ 'common.close' | translate }}</button>
        </header>
        <div class="dialog-body">
          <form class="form-stack" (ngSubmit)="submit()" novalidate>
            <div class="field">
              <label class="field-label" for="board-create-name">{{ 'boards.create.name' | translate }}</label>
              <input id="board-create-name" class="input" name="name" [(ngModel)]="name" [maxLength]="nameMax" autocomplete="off" [attr.aria-invalid]="!!nameError()" [attr.aria-describedby]="nameError() ? 'board-create-name-error' : null" />
              @if (nameError(); as message) {
                <span id="board-create-name-error" class="field-error" role="alert">{{ message }}</span>
              }
            </div>
            <fieldset class="field pick-group" [attr.aria-describedby]="surveysError() ? 'board-create-surveys-error' : null">
              <legend class="field-label">{{ 'boards.create.surveys' | translate }} <span class="muted">{{ 'boards.create.selected' | translate: { count: chosen().length, max: max } }}</span></legend>
              @for (survey of ready(); track survey.id) {
                <label class="pick">
                  @if (positionOf(survey.id); as position) {
                    <span class="pick-position" [attr.aria-label]="'boards.create.position' | translate: { position: position }">{{ position }}</span>
                  }
                  <input type="checkbox" [checked]="isChosen(survey.id)" [disabled]="!isChosen(survey.id) && chosen().length >= max" (change)="toggle(survey.id, $any($event.target).checked)" />
                  <span class="pick-text">
                    <span class="code">{{ survey.entryPoint }}</span>
                    <span class="muted">{{ survey.repository }} · {{ shortDate(survey.createdAt) }}</span>
                  </span>
                </label>
              } @empty {
                <p class="muted">{{ 'boards.create.none' | translate }}</p>
              }
              @if (surveysError(); as message) {
                <span id="board-create-surveys-error" class="field-error" role="alert">{{ message }}</span>
              }
            </fieldset>
            @if (error(); as message) {
              <p class="field-error" role="alert">{{ message }}</p>
            }
            <button class="btn btn-p" type="submit" [disabled]="submitting() || ready().length === 0">{{ (submitting() ? 'boards.create.creating' : 'boards.create.submit') | translate }}</button>
          </form>
        </div>
      </section>
    </div>
  `,
})
export class BoardCreateDialogComponent implements OnInit {
  private readonly api = inject(RastroApi);
  private readonly i18n = inject(LanguageService);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly surveys = input.required<readonly SurveySummary[]>();
  readonly preselected = input<readonly string[]>([]);
  readonly created = output<BoardDetail>();
  readonly cancel = output<void>();

  protected name = '';
  protected readonly max = BOARD_MAX_LANES;
  protected readonly nameMax = BOARD_NAME_MAX;
  protected readonly shortDate = shortDate;
  // Só levantamento pronto entra no quadro (a API recusa os outros).
  protected readonly ready = computed(() => this.surveys().filter((s) => s.status === SurveyStatus.Completed));
  protected readonly chosen = signal<string[]>([]);
  protected readonly submitting = signal(false);
  protected readonly nameError = signal<string | null>(null);
  protected readonly surveysError = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  // A escolha feita na lista de levantamentos entra uma vez, ao abrir.
  ngOnInit(): void {
    const ids = new Set(this.ready().map((s) => s.id));
    this.chosen.set(this.preselected().filter((id) => ids.has(id)).slice(0, BOARD_MAX_LANES));
  }

  protected isChosen(id: string): boolean {
    return this.chosen().includes(id);
  }

  // Posição (1, 2, ...) que a levantamento escolhido terá no quadro; 0 quando não está escolhido.
  protected positionOf(id: string): number {
    return this.chosen().indexOf(id) + 1;
  }

  protected toggle(id: string, on: boolean): void {
    this.chosen.update((list) => (on ? (list.includes(id) ? list : [...list, id]) : list.filter((x) => x !== id)));
  }

  protected close(): void {
    if (!this.submitting()) this.cancel.emit();
  }

  protected submit(): void {
    this.error.set(null);
    const name = this.name.trim();
    this.nameError.set(name ? null : this.i18n.t('boards.create.errors.name'));
    this.surveysError.set(this.chosen().length >= 1 ? null : this.i18n.t('boards.create.errors.surveys', { max: BOARD_MAX_LANES }));
    if (this.nameError() || this.surveysError()) {
      // O foco vai ao primeiro campo com erro, que acaba de aparecer.
      afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>(this.nameError() ? '#board-create-name' : '.pick input')?.focus(), { injector: this.injector });
      return;
    }
    this.submitting.set(true);
    this.api.createBoard({ name, surveyIds: this.chosen() }).subscribe({
      next: (board) => {
        this.submitting.set(false);
        this.created.emit(board);
      },
      error: (error) => {
        this.submitting.set(false);
        this.error.set(apiErrorMessage(error));
      },
    });
  }
}
