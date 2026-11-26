import { Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { Observable, first, switchMap, throwError, timer } from 'rxjs';
import { AiStatusService } from '../../core/ai-status.service';
import { DialogDirective } from '../../core/dialog.directive';
import { LanguageService } from '../../core/i18n/i18n';
import { laneLayout, laneRunning, laneUpdates, legendKinds, moveLane, orderedLanes, glossaryEntries } from '../../core/graph/board';
import { BUSINESS_KIND_GLYPH, BUSINESS_KIND_LABEL_KEY, BUSINESS_KIND_SLUG } from '../../core/graph/business';
import {
  AiUse,
  BOARD_GLOSSARY_MAX,
  BOARD_MAX_LANES,
  BOARD_NAME_MAX,
  BOARD_TITLE_MAX,
  BoardDetail,
  BoardLane,
  BoardUpdateRequest,
  RemovedStep,
  SurveyDetail,
  SurveyStatus,
  SurveySummary,
  aiCan,
  isFinished,
} from '../../core/models';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';
import { ConfirmDialogComponent } from '../survey/confirm-dialog.component';
import { BusinessViewComponent } from '../survey/business-view.component';

// Intervalo de leitura enquanto algum levantamento do quadro está rodando.
export const BOARD_POLL_MS = 2500;
// Quanto tempo o retorno curto de uma ação fica na tela.
export const BOARD_FEEDBACK_MS = 4000;

// O levantamento novo da faixa terminou com falha: a faixa fica com o antigo.
class ResurveyFailedError extends Error {}

// Faixas que a pessoa quer tirar ou levantar de novo: o diálogo guarda qual.
interface Replaced {
  title: string;
  steps: RemovedStep[];
}

// Tela de um quadro de fluxos (Fase 60): um levantamento por faixa, lado a lado, cada faixa com a visão de negócio
// do levantamento. A posição dos balões é guardada por quadro; a ordem, o título, o glossário e o nome também.
@Component({
  selector: 'app-board-page',
  imports: [RouterLink, TranslatePipe, FormsModule, DialogDirective, BusinessViewComponent, ConfirmDialogComponent],
  templateUrl: './board.page.html',
  styleUrl: './board.page.scss',
  host: { class: 'board-screen' },
})
export class BoardPage {
  private readonly api = inject(RastroApi);
  private readonly router = inject(Router);
  private readonly i18n = inject(LanguageService);
  private readonly ai = inject(AiStatusService);
  private readonly destroyRef = inject(DestroyRef);

  readonly id = input.required<string>();

  protected readonly board = signal<BoardDetail | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly lanes = computed(() => {
    const board = this.board();
    return board ? orderedLanes(board) : [];
  });
  protected readonly layouts = computed(() => {
    const board = this.board();
    return new Map((board?.lanes ?? []).map((lane) => [lane.surveyId, laneLayout(board!.layout, lane.surveyId)]));
  });
  // Os levantamentos das faixas prontas (a visão de negócio precisa do grafo, das notas e dos links dele).
  protected readonly details = signal<ReadonlyMap<string, SurveyDetail>>(new Map());
  protected readonly detailFailed = signal<ReadonlySet<string>>(new Set());
  protected readonly legend = computed(() => legendKinds(this.lanes()));
  protected readonly canSuggest = computed(() => aiCan(this.ai.status(), AiUse.Suggestions) && this.lanes().some((l) => l.flow !== null));
  protected readonly canAdd = computed(() => this.lanes().length < BOARD_MAX_LANES);

  protected readonly renaming = signal(false);
  protected nameDraft = '';
  protected glossaryDraft = '';
  protected readonly glossaryCount = computed(() => glossaryEntries(this.glossaryText()).length);
  private readonly glossaryText = signal('');
  protected readonly glossaryDirty = computed(() => this.glossaryText() !== (this.board()?.glossary ?? ''));
  protected readonly saving = signal(false);
  protected readonly suggesting = signal(false);
  protected readonly feedback = signal<{ text: string; error: boolean } | null>(null);
  private feedbackTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly replacing = signal<ReadonlySet<string>>(new Set());
  protected readonly replaced = signal<Replaced | null>(null);
  protected readonly removing = signal<BoardLane | null>(null);
  protected readonly confirmingDelete = signal(false);
  protected readonly deleting = signal(false);
  protected readonly dragFrom = signal<number | null>(null);

  protected readonly addOpen = signal(false);
  protected readonly candidates = signal<SurveySummary[] | null>(null);
  protected readonly addError = signal<string | null>(null);
  protected addChoice = '';

  protected readonly kindSlug = BUSINESS_KIND_SLUG;
  protected readonly kindGlyph = BUSINESS_KIND_GLYPH;
  protected readonly kindLabel = BUSINESS_KIND_LABEL_KEY;
  protected readonly SurveyStatus = SurveyStatus;
  protected readonly maxLanes = BOARD_MAX_LANES;
  protected readonly nameMax = BOARD_NAME_MAX;
  protected readonly titleMax = BOARD_TITLE_MAX;
  protected readonly glossaryMax = BOARD_GLOSSARY_MAX;
  protected readonly laneRunning = laneRunning;
  private pollTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    this.destroyRef.onDestroy(() => {
      clearTimeout(this.pollTimer);
      clearTimeout(this.feedbackTimer);
    });
    // Outro quadro (a rota muda o id sem recriar a tela) começa limpo.
    effect(() => {
      this.id();
      untracked(() => {
        this.board.set(null);
        this.details.set(new Map());
        this.detailFailed.set(new Set());
        this.load(true);
      });
    });
  }

  protected load(initial = false): void {
    clearTimeout(this.pollTimer);
    this.api.board(this.id()).subscribe({
      next: (board) => {
        this.error.set(null);
        this.applyBoard(board, initial);
      },
      error: (error) => {
        // A leitura de acompanhamento que falha não derruba o quadro que já está na tela.
        if (initial || !this.board()) this.error.set(apiErrorMessage(error));
        else this.schedulePoll();
      },
    });
  }

  private applyBoard(board: BoardDetail, resetDrafts: boolean): void {
    this.board.set(board);
    if (resetDrafts || !this.glossaryDirty()) {
      this.glossaryDraft = board.glossary ?? '';
      this.glossaryText.set(this.glossaryDraft);
    }
    this.loadDetails(board);
    this.schedulePoll();
  }

  // Busca o levantamento de cada faixa pronta que ainda não veio.
  private loadDetails(board: BoardDetail): void {
    for (const lane of board.lanes) {
      if (lane.status !== SurveyStatus.Completed || this.details().has(lane.surveyId)) continue;
      this.fetchDetail(lane.surveyId);
    }
  }

  protected fetchDetail(surveyId: string): void {
    this.detailFailed.update((set) => {
      const next = new Set(set);
      next.delete(surveyId);
      return next;
    });
    this.api.survey(surveyId).subscribe({
      next: (detail) => this.details.update((map) => new Map(map).set(surveyId, detail)),
      error: () => this.detailFailed.update((set) => new Set(set).add(surveyId)),
    });
  }

  // Enquanto algum levantamento do quadro roda, lê o quadro de novo até todos terminarem.
  private schedulePoll(): void {
    clearTimeout(this.pollTimer);
    if (this.lanes().some((lane) => !isFinished(lane.status))) this.pollTimer = setTimeout(() => this.load(), BOARD_POLL_MS);
  }

  // ---- Nome, glossário e títulos ----

  protected startRename(): void {
    this.nameDraft = this.board()?.name ?? '';
    this.renaming.set(true);
  }

  protected cancelRename(): void {
    this.renaming.set(false);
  }

  protected commitRename(): void {
    const name = this.nameDraft.trim();
    this.renaming.set(false);
    if (!name || name === this.board()?.name) return;
    this.update({ name });
  }

  protected onGlossary(event: Event): void {
    this.glossaryText.set((event.target as HTMLTextAreaElement).value);
  }

  protected saveGlossary(): void {
    const text = this.glossaryText().trim();
    this.update({ glossary: text ? text : null }, 'board.glossary.saved');
  }

  protected renameLane(lane: BoardLane, event: Event): void {
    const input = event.target as HTMLInputElement;
    const title = input.value.trim();
    if (!title) {
      input.value = lane.title;
      return;
    }
    if (title === lane.title) return;
    this.update({ lanes: this.lanes().map((l) => (l.surveyId === lane.surveyId ? { ...l, title } : l)) });
  }

  // ---- Ordem das faixas ----

  protected move(index: number, delta: number): void {
    const next = moveLane(this.lanes(), index, delta);
    if (next.every((lane, i) => lane === this.lanes()[i])) return;
    this.update({ lanes: next });
  }

  protected dragStart(event: DragEvent, index: number): void {
    this.dragFrom.set(index);
    event.dataTransfer?.setData('text/plain', String(index));
    if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
  }

  protected dragOver(event: DragEvent): void {
    if (this.dragFrom() !== null) event.preventDefault();
  }

  protected drop(event: DragEvent, index: number): void {
    event.preventDefault();
    const from = this.dragFrom();
    this.dragFrom.set(null);
    if (from !== null && from !== index) this.move(from, index - from);
  }

  protected dragEnd(): void {
    this.dragFrom.set(null);
  }

  // Grava nome, glossário, ordem e títulos de uma vez (a rota é uma só) e passa a mostrar o que o servidor devolveu.
  private update(change: { name?: string; glossary?: string | null; lanes?: readonly BoardLane[] }, doneKey?: string): void {
    const board = this.board();
    if (!board) return;
    const request: BoardUpdateRequest = {
      name: change.name ?? board.name,
      glossary: change.glossary === undefined ? board.glossary : change.glossary,
      lanes: laneUpdates(change.lanes ?? this.lanes()),
    };
    this.saving.set(true);
    this.api.updateBoard(board.id, request).subscribe({
      next: (updated) => {
        this.saving.set(false);
        this.applyBoard(updated, change.glossary !== undefined);
        if (doneKey) this.say(doneKey);
      },
      error: (error) => {
        this.saving.set(false);
        this.sayError(error);
      },
    });
  }

  // ---- Tirar e adicionar faixa ----

  protected askRemove(lane: BoardLane): void {
    this.removing.set(lane);
  }

  protected confirmRemove(): void {
    const lane = this.removing();
    this.removing.set(null);
    if (!lane) return;
    this.update({ lanes: this.lanes().filter((l) => l.surveyId !== lane.surveyId) });
  }

  protected openAdd(): void {
    this.addError.set(null);
    this.candidates.set(null);
    this.addOpen.set(true);
    this.api.surveys().subscribe({
      next: (surveys) => {
        const used = new Set(this.lanes().map((l) => l.surveyId));
        const free = surveys.filter((s) => s.status === SurveyStatus.Completed && !used.has(s.id));
        this.candidates.set(free);
        this.addChoice = free[0]?.id ?? '';
      },
      error: (error) => this.addError.set(apiErrorMessage(error)),
    });
  }

  protected closeAdd(): void {
    this.addOpen.set(false);
  }

  protected submitAdd(): void {
    const board = this.board();
    if (!board || !this.addChoice) return;
    this.saving.set(true);
    this.api.addBoardLane(board.id, this.addChoice).subscribe({
      next: (updated) => {
        this.saving.set(false);
        this.addOpen.set(false);
        this.applyBoard(updated, false);
      },
      error: (error) => {
        this.saving.set(false);
        this.addError.set(apiErrorMessage(error));
      },
    });
  }

  // ---- Levantar de novo ----

  // Cria o levantamento novo com o mesmo pedido, espera ficar pronto e troca a faixa para ele. Os passos que não
  // existem mais no código novo saem do desenho, e a tela mostra quais foram.
  protected resurvey(lane: BoardLane): void {
    const board = this.board();
    if (!board || this.replacing().has(lane.surveyId)) return;
    this.setReplacing(lane.surveyId, true);
    this.api
      .survey(lane.surveyId)
      .pipe(
        switchMap((old) =>
          this.api.createSurvey({
            repository: old.survey.repository,
            solutionPath: old.survey.solutionPath,
            entryPoint: old.survey.entryPoint,
            maxDepth: old.survey.maxDepth,
            ...(old.survey.crossRepositories?.length ? { crossRepositories: old.survey.crossRepositories } : {}),
          }),
        ),
        switchMap((created) => this.untilFinished(created.id)),
        switchMap((done): Observable<{ board: BoardDetail; removedSteps: RemovedStep[] }> =>
          done.survey.status === SurveyStatus.Completed ? this.api.replaceBoardLane(board.id, lane.surveyId, done.survey.id) : throwError(() => new ResurveyFailedError()),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => {
          this.setReplacing(lane.surveyId, false);
          this.applyBoard(result.board, false);
          this.replaced.set({ title: lane.title, steps: result.removedSteps });
        },
        error: (error) => {
          this.setReplacing(lane.surveyId, false);
          if (error instanceof ResurveyFailedError) this.say('board.resurveyFailed', true);
          else this.sayError(error);
        },
      });
  }

  private untilFinished(surveyId: string): Observable<SurveyDetail> {
    return timer(0, BOARD_POLL_MS).pipe(
      switchMap(() => this.api.survey(surveyId)),
      first((detail) => isFinished(detail.survey.status)),
    );
  }

  private setReplacing(surveyId: string, on: boolean): void {
    this.replacing.update((set) => {
      const next = new Set(set);
      if (on) next.add(surveyId);
      else next.delete(surveyId);
      return next;
    });
  }

  protected dismissReplaced(): void {
    this.replaced.set(null);
  }

  // ---- Sugerir textos (com o glossário) ----

  protected suggest(): void {
    const board = this.board();
    if (!board || this.suggesting()) return;
    this.suggesting.set(true);
    this.api.suggestBoardLabels(board.id).subscribe({
      next: (result) => {
        this.suggesting.set(false);
        this.say(result.proposed > 0 ? 'business.feedback.suggested' : 'business.feedback.suggestedNone', false, { count: result.proposed, skipped: result.skipped });
        this.load();
      },
      error: (error) => {
        this.suggesting.set(false);
        this.sayError(error);
      },
    });
  }

  // ---- Excluir o quadro ----

  protected deleteBoard(): void {
    const board = this.board();
    if (!board) return;
    this.deleting.set(true);
    this.api.deleteBoard(board.id).subscribe({
      next: () => {
        this.deleting.set(false);
        this.confirmingDelete.set(false);
        this.router.navigate(['/boards']);
      },
      error: (error) => {
        this.deleting.set(false);
        this.confirmingDelete.set(false);
        this.sayError(error);
      },
    });
  }

  // ---- Retorno curto ----

  private say(key: string, isError = false, params?: Record<string, unknown>): void {
    this.showFeedback({ text: this.i18n.t(key, params), error: isError });
  }

  private sayError(error: unknown): void {
    this.showFeedback({ text: apiErrorMessage(error), error: true });
  }

  private showFeedback(feedback: { text: string; error: boolean }): void {
    this.feedback.set(feedback);
    clearTimeout(this.feedbackTimer);
    this.feedbackTimer = setTimeout(() => this.feedback.set(null), BOARD_FEEDBACK_MS);
  }

  protected dismissFeedback(): void {
    clearTimeout(this.feedbackTimer);
    this.feedback.set(null);
  }
}
