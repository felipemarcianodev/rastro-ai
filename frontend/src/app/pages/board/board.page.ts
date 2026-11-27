import { Component, DestroyRef, ElementRef, Injector, afterNextRender, computed, effect, inject, input, signal, untracked, viewChild, viewChildren } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { Observable, catchError, first, forkJoin, map, of, switchMap, throwError, timer } from 'rxjs';
import { AiStatusService } from '../../core/ai-status.service';
import { CopyFeedback } from '../../core/copy-feedback';
import { DialogDirective } from '../../core/dialog.directive';
import { DrawioStatusService } from '../../core/drawio-status.service';
import { downloadBlob, fileSlug, svgToPng } from '../../core/download';
import { LanguageService } from '../../core/i18n/i18n';
import { laneLayout, laneRunning, laneUpdates, legendKinds, moveLane, orderedLanes, glossaryEntries } from '../../core/graph/board';
import { BUSINESS_KIND_GLYPH, BUSINESS_KIND_LABEL_KEY, BUSINESS_KIND_SLUG } from '../../core/graph/business';
import { BusinessExportData, ExportLane, boardNoteLines, boardToDrawio, businessToMermaid, businessToSvg, readBusinessColors } from '../../core/graph/business-export';
import { DRAWIO_MIME } from '../../core/graph/drawio';
import { BoardDrawioBase, boardDrawioBase, boardDrawioChanges, boardKey } from '../../core/graph/drawio-board';
import { applyDrawioLayout } from '../../core/graph/drawio-merge';
import { hostLinkResolver } from '../../core/graph/link-template';
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
import { BOARDS_DELETED_STATE } from '../boards/boards.page';
import { ConfirmDialogComponent } from '../survey/confirm-dialog.component';
import { BusinessViewComponent } from '../survey/business-view.component';
import { DrawioViewComponent } from '../survey/drawio-view.component';

// Intervalo de leitura enquanto algum levantamento do quadro está rodando.
export const BOARD_POLL_MS = 2500;
// Quanto tempo o retorno curto de uma ação que deu certo fica na tela (o erro fica até a pessoa fechar).
export const BOARD_FEEDBACK_MS = 4000;
// Prefixo do nome dos arquivos exportados do quadro.
const BOARD_EXPORT_PREFIX = 'rastro-quadro-';

// Aviso de uma ação que gravou: a chave do texto e os parâmetros.
interface Done {
  key: string;
  params?: Record<string, unknown>;
}

// O que a tela de uma faixa entrega para exportar (o BusinessViewComponent, ou o que o substitua nos testes).
interface LaneExporter {
  exportData(): BusinessExportData | null;
}

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
  imports: [RouterLink, TranslatePipe, FormsModule, DialogDirective, BusinessViewComponent, DrawioViewComponent, ConfirmDialogComponent],
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
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly laneViews = viewChildren<LaneExporter>('laneView');
  private readonly drawioFrame = viewChild(DrawioViewComponent);
  private readonly drawioStatus = inject(DrawioStatusService);

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
  // A contagem segue o que está salvo; o rascunho aparece como "Alterações não salvas".
  protected readonly glossaryCount = computed(() => glossaryEntries(this.board()?.glossary ?? '').length);
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
  protected readonly dragOverIndex = signal<number | null>(null);
  // Só as faixas já desenhadas entram na exportação.
  protected readonly hasDrawn = computed(() => this.laneViews().length > 0);

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
    this.drawioStatus.load();
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
        this.error.set(null);
        this.replaced.set(null);
        this.replacing.set(new Set());
        this.removing.set(null);
        this.addOpen.set(false);
        this.confirmingDelete.set(false);
        this.renaming.set(false);
        this.drawioXml.set(null);
        this.dismissFeedback();
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

  // Leva o foco a um elemento depois da próxima renderização (o elemento pode nascer ou mudar agora).
  private focusAfterRender(find: () => HTMLElement | null | undefined): void {
    afterNextRender(() => find()?.focus(), { injector: this.injector });
  }

  private byId(id: string): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(`#${id}`);
  }

  protected startRename(): void {
    if (this.saving()) return;
    this.nameDraft = this.board()?.name ?? '';
    this.renaming.set(true);
    this.focusAfterRender(() => {
      const field = this.byId('board-name') as HTMLInputElement | null;
      field?.select();
      return field;
    });
  }

  protected cancelRename(): void {
    this.renaming.set(false);
    this.focusAfterRender(() => this.byId('board-rename-btn'));
  }

  protected commitRename(): void {
    const name = this.nameDraft.trim();
    this.renaming.set(false);
    this.focusAfterRender(() => this.byId('board-rename-btn'));
    if (!name || name === this.board()?.name) return;
    this.update({ name }, { key: 'board.renamed' });
  }

  protected onGlossary(event: Event): void {
    this.glossaryText.set((event.target as HTMLTextAreaElement).value);
  }

  protected saveGlossary(): void {
    const text = this.glossaryText().trim();
    this.update({ glossary: text ? text : null }, { key: 'board.glossary.saved' });
  }

  protected renameLane(lane: BoardLane, event: Event): void {
    const input = event.target as HTMLInputElement;
    const title = input.value.trim();
    if (!title) {
      input.value = lane.title;
      return;
    }
    if (title === lane.title) return;
    this.update({ lanes: this.lanes().map((l) => (l.surveyId === lane.surveyId ? { ...l, title } : l)) }, { key: 'board.lane.retitled', params: { title } });
  }

  // ---- Ordem das faixas ----

  // Com `refocus` (setas da faixa), o foco volta à seta da mesma direção na faixa que andou, ou à oposta se ela ficou
  // na ponta. O botão fica ativo durante a gravação (o foco não cai) e a guarda daqui impede a segunda gravação.
  protected move(index: number, delta: number, refocus = false): void {
    if (this.saving()) return;
    const lane = this.lanes()[index];
    const next = moveLane(this.lanes(), index, delta);
    if (!lane || next.every((l, i) => l === this.lanes()[i])) return;
    const position = next.findIndex((l) => l.surveyId === lane.surveyId) + 1;
    this.update({ lanes: next }, { key: 'board.lane.moved', params: { title: lane.title, position, total: next.length } }, () => {
      if (refocus) this.focusMove(lane.surveyId, delta);
    });
  }

  private focusMove(surveyId: string, delta: number): void {
    this.focusAfterRender(() => {
      const section = Array.from(this.host.nativeElement.querySelectorAll<HTMLElement>('.lane')).find((el) => el.dataset['laneId'] === surveyId);
      const arrow = (side: string) => section?.querySelector<HTMLButtonElement>(`[data-move="${side}"]`);
      const same = arrow(delta < 0 ? 'left' : 'right');
      return same && !same.disabled ? same : arrow(delta < 0 ? 'right' : 'left');
    });
  }

  protected dragStart(event: DragEvent, index: number): void {
    this.dragFrom.set(index);
    event.dataTransfer?.setData('text/plain', String(index));
    if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
  }

  // A faixa sobre a qual o arraste está fica destacada (o destino da soltura).
  protected dragOver(event: DragEvent, index: number): void {
    if (this.dragFrom() === null) return;
    event.preventDefault();
    this.dragOverIndex.set(index);
  }

  protected dragLeave(event: DragEvent, index: number): void {
    const into = event.relatedTarget;
    if (this.dragOverIndex() === index && !(into instanceof Node && (event.currentTarget as HTMLElement).contains(into))) this.dragOverIndex.set(null);
  }

  protected drop(event: DragEvent, index: number): void {
    event.preventDefault();
    const from = this.dragFrom();
    this.dragFrom.set(null);
    this.dragOverIndex.set(null);
    if (from !== null && from !== index) this.move(from, index - from);
  }

  protected dragEnd(): void {
    this.dragFrom.set(null);
    this.dragOverIndex.set(null);
  }

  // Grava nome, glossário, ordem e títulos de uma vez (a rota é uma só) e passa a mostrar o que o servidor devolveu.
  private update(change: { name?: string; glossary?: string | null; lanes?: readonly BoardLane[] }, done?: Done, after?: (updated: BoardDetail) => void): void {
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
        if (done) this.say(done.key, false, done.params);
        after?.(updated);
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
    const index = this.lanes().findIndex((l) => l.surveyId === lane.surveyId);
    // O foco vai para a faixa que ocupou o lugar da tirada; sem faixa, para "Adicionar levantamento".
    this.update({ lanes: this.lanes().filter((l) => l.surveyId !== lane.surveyId) }, { key: 'board.lane.removed', params: { title: lane.title } }, (updated) => {
      this.focusAfterRender(() => {
        const titles = this.host.nativeElement.querySelectorAll<HTMLElement>('.lane-title');
        return titles[Math.min(index, updated.lanes.length - 1)] ?? this.byId('board-add-btn');
      });
    });
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
        this.say('board.lane.added');
        this.focusAfterRender(() => this.byId('board-add-btn'));
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
    this.say('board.lane.resurveyStarted', false, { title: lane.title });
    // O botão some e a faixa passa a mostrar o estado: o foco vai para ele.
    this.focusAfterRender(() => this.laneStatus(lane.surveyId));
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

  private laneStatus(surveyId: string): HTMLElement | undefined {
    return Array.from(this.host.nativeElement.querySelectorAll<HTMLElement>('[data-lane-status]')).find((el) => el.dataset['laneStatus'] === surveyId);
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
        // A lista de quadros mostra o aviso de exclusão (o estado da navegação leva o nome).
        void this.router.navigate(['/boards'], { state: { [BOARDS_DELETED_STATE]: board.name } });
      },
      error: (error) => {
        this.deleting.set(false);
        this.confirmingDelete.set(false);
        this.sayError(error);
      },
    });
  }

  // ---- Exportar (Fase 61) ----

  protected readonly exportOpen = signal(false);
  protected readonly menuCopy = new CopyFeedback();

  protected toggleExport(): void {
    if (this.exportOpen()) {
      this.closeExport();
      return;
    }
    this.exportOpen.set(true);
    this.focusAfterRender(() => this.host.nativeElement.querySelector<HTMLElement>('#board-export-menu button'));
  }

  // Fechar o menu (Esc, clique fora ou escolha de um item) devolve o foco ao botão Exportar.
  protected closeExport(): void {
    this.exportOpen.set(false);
    this.focusAfterRender(() => this.byId('board-export-btn'));
  }

  // Esc fecha o menu, Tab fecha e segue para o próximo controle, e as setas, Home e End andam pelos itens.
  protected onExportKey(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      this.closeExport();
      return;
    }
    if (event.key === 'Tab') {
      this.byId('board-export-btn')?.focus();
      this.exportOpen.set(false);
      return;
    }
    const items = Array.from((event.currentTarget as HTMLElement).querySelectorAll<HTMLElement>('button'));
    const current = items.indexOf(document.activeElement as HTMLElement);
    const next =
      event.key === 'ArrowDown' ? (current + 1) % items.length
      : event.key === 'ArrowUp' ? (current - 1 + items.length) % items.length
      : event.key === 'Home' ? 0
      : event.key === 'End' ? items.length - 1
      : -1;
    if (next < 0) return;
    event.preventDefault();
    items[next]?.focus();
  }

  // As faixas que já estão desenhadas, na ordem do quadro, com o desenho que a tela de cada uma entrega.
  private drawnLanes(): { lane: BoardLane; data: BusinessExportData }[] {
    const byId = new Map<string, BusinessExportData>();
    for (const view of this.laneViews()) {
      const data = view.exportData();
      if (data) byId.set(data.surveyId, data);
    }
    return this.lanes().flatMap((lane) => {
      const data = byId.get(lane.surveyId);
      return data ? [{ lane, data }] : [];
    });
  }

  private exportName(): string {
    return `${BOARD_EXPORT_PREFIX}${fileSlug(this.board()?.name ?? 'quadro')}`;
  }

  private notDrawn(): void {
    this.say('board.export.notDrawn', true);
  }

  private started(file: string): void {
    this.say('survey.export.started', false, { file });
  }

  private noteOf(drawn: readonly { data: BusinessExportData }[]) {
    return boardNoteLines(
      this.board()?.glossary ?? null,
      drawn.map((d) => d.data.flow),
      { glossary: this.i18n.t('board.glossary.label'), legend: this.i18n.t('board.legend.label'), kind: (kind) => this.i18n.t(BUSINESS_KIND_LABEL_KEY[kind]) },
    );
  }

  protected exportDrawio(): void {
    this.closeExport();
    const drawn = this.drawnLanes();
    if (drawn.length === 0) {
      this.notDrawn();
      return;
    }
    this.generatedDrawio(drawn).subscribe((xml) => {
      const name = this.exportName();
      downloadBlob(new Blob([xml], { type: DRAWIO_MIME }), `${name}.drawio`);
      this.started(`${name}.drawio`);
    });
  }

  // O .drawio do quadro: uma moldura por faixa, os passos na posição salva, o link de cada um para o host (o
  // levantamento da faixa resolve o dele) e a nota do glossário. Sem os links (rota fora do ar) o arquivo sai do mesmo
  // jeito, só sem eles.
  private generatedDrawio(drawn: readonly { lane: BoardLane; data: BusinessExportData }[]): Observable<string> {
    return forkJoin(drawn.map(({ lane }) => this.api.links(lane.surveyId).pipe(map((links) => hostLinkResolver(links)), catchError(() => of(undefined))))).pipe(
      map((resolvers) => {
        const lanes: ExportLane[] = drawn.map(({ lane, data }, i) => ({ title: lane.title, data, linkOf: resolvers[i] }));
        return boardToDrawio(lanes, this.board()?.name ?? this.exportName(), this.noteOf(drawn));
      }),
    );
  }

  // ---- Quadro no draw.io (Fase 68) ----

  protected readonly drawioXml = signal<string | null>(null);
  protected readonly drawioOpening = signal(false);
  protected readonly canDrawio = computed(() => this.drawioStatus.available() && this.hasDrawn());
  // Tabelas do que o editor tinha ao abrir; a gravação as atualiza para só mandar o que mudou de verdade.
  private drawioBase: BoardDrawioBase | null = null;
  // Sem ler o que estava guardado, o editor abre mas não grava anotação (gravar vazio apagaria o guardado).
  private annotationsLocked = false;
  protected readonly saveDrawio = (xml: string): void => this.persistDrawio(xml);

  // Abre o quadro no editor: o .drawio de hoje com as anotações guardadas por cima.
  protected openDrawio(): void {
    const board = this.board();
    const drawn = this.drawnLanes();
    if (!board || this.drawioOpening()) return;
    if (drawn.length === 0) {
      this.notDrawn();
      return;
    }
    this.drawioOpening.set(true);
    const boxes = new Map(drawn.map(({ lane, data }) => [lane.surveyId, new Map([...data.geometry.boxes].map(([id, box]) => [id, { x: box.x, y: box.y }]))]));
    forkJoin({ xml: this.generatedDrawio(drawn), saved: this.api.boardDrawio(board.id).pipe(catchError(() => of(null))) }).subscribe({
      next: ({ xml, saved }) => {
        const merged = applyDrawioLayout(xml, { cells: [], annotations: saved?.annotations ?? null });
        this.annotationsLocked = saved === null;
        if (saved === null) this.say('drawio.loadFailed', true);
        this.drawioBase = boardDrawioBase(xml, merged, boxes);
        this.drawioOpening.set(false);
        this.drawioXml.set(merged);
      },
      error: () => {
        this.drawioOpening.set(false);
        this.say('drawio.loadFailed', true);
      },
    });
  }

  // Volta às faixas: grava o que ficou pendente, e a tela lê o quadro de novo (a posição mudou no editor).
  protected closeDrawio(): void {
    this.drawioFrame()?.flushPending();
    this.drawioXml.set(null);
    this.drawioBase = null;
    this.load();
  }

  // Cada autosave do editor: posição do balão na faixa dele, texto como texto do usuário e anotações do quadro.
  private persistDrawio(xml: string): void {
    const board = this.board();
    const base = this.drawioBase;
    const changes = base ? boardDrawioChanges(xml, base) : null;
    if (!board || !base || !changes) return;
    const done = () => this.drawioFrame()?.announceSaved();
    const failed = () => this.drawioFrame()?.failSave();
    if (changes.moved.length > 0) {
      this.api.saveBoardLayout(board.id, { steps: changes.moved.map((m) => m.step), edges: [] }).subscribe({
        next: () => {
          for (const m of changes.moved) base.positions.set(boardKey(m.step.laneSurveyId, m.step.stepId), m.at);
          done();
        },
        error: failed,
      });
    }
    for (const label of changes.labels) {
      this.api.saveBusinessLabel(label.lane, label.stepId, label.text).subscribe({
        next: () => {
          base.texts.set(boardKey(label.lane, label.stepId), label.text);
          done();
        },
        error: failed,
      });
    }
    if (changes.annotationsChanged && !this.annotationsLocked) {
      this.api.saveBoardDrawio(board.id, changes.annotations).subscribe({
        next: () => {
          base.annotations = changes.annotations;
          done();
        },
        error: failed,
      });
    }
  }

  protected exportMermaid(download: boolean): void {
    this.closeExport();
    const drawn = this.drawnLanes();
    const text = businessToMermaid(
      drawn.map(({ lane, data }) => ({ title: lane.title, flow: data.flow })),
      true,
    );
    if (!text) {
      this.notDrawn();
      return;
    }
    if (download) {
      const name = this.exportName();
      downloadBlob(new Blob([text], { type: 'text/plain' }), `${name}.mmd`);
      this.started(`${name}.mmd`);
    } else {
      this.menuCopy.copy('```mermaid\n' + text + '```\n');
    }
  }

  protected exportImage(format: 'png' | 'svg'): void {
    this.closeExport();
    const drawn = this.drawnLanes();
    const style = getComputedStyle(this.host.nativeElement);
    const image = businessToSvg(
      drawn.map(({ lane, data }) => ({ title: lane.title, data })),
      readBusinessColors((name) => style.getPropertyValue(name)),
      true,
      this.noteOf(drawn),
    );
    if (!image) {
      this.notDrawn();
      return;
    }
    const name = this.exportName();
    if (format === 'svg') {
      downloadBlob(new Blob([image.svg], { type: 'image/svg+xml' }), `${name}.svg`);
      this.started(`${name}.svg`);
      return;
    }
    svgToPng(image.svg, image.width, image.height).then(
      (blob) => {
        downloadBlob(blob, `${name}.png`);
        this.started(`${name}.png`);
      },
      (error: Error) => this.say('survey.export.imageFailed', true, { message: error.message }),
    );
  }

  // ---- Retorno curto ----

  private say(key: string, isError = false, params?: Record<string, unknown>): void {
    this.showFeedback({ text: this.i18n.t(key, params), error: isError });
  }

  private sayError(error: unknown): void {
    this.showFeedback({ text: apiErrorMessage(error), error: true });
  }

  // O que deu certo some em alguns segundos; o erro fica até a pessoa fechar (dá tempo de ler e de agir).
  private showFeedback(feedback: { text: string; error: boolean }): void {
    this.feedback.set(feedback);
    clearTimeout(this.feedbackTimer);
    if (!feedback.error) this.feedbackTimer = setTimeout(() => this.feedback.set(null), BOARD_FEEDBACK_MS);
  }

  protected dismissFeedback(): void {
    clearTimeout(this.feedbackTimer);
    this.feedback.set(null);
  }
}
