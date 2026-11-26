import { Component, OnInit, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { shortDate } from '../../core/format';
import { LanguageService } from '../../core/i18n/i18n';
import { BoardDetail, BoardSummary, SurveySummary } from '../../core/models';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';
import { ConfirmDialogComponent } from '../survey/confirm-dialog.component';
import { BoardCreateDialogComponent } from './board-create-dialog.component';

// Linhas do esqueleto da lista enquanto os quadros carregam.
const SKELETON_ROWS = [0, 1, 2];
// Quanto tempo o aviso de exclusão fica na tela.
const NOTICE_MS = 5000;
// Chave do estado da navegação com que a tela do quadro avisa, ao voltar para a lista, qual quadro foi excluído.
export const BOARDS_DELETED_STATE = 'deletedBoard';

// Lista dos quadros de fluxos (Fase 60): abrir, criar e excluir.
@Component({
  selector: 'app-boards-page',
  imports: [RouterLink, TranslatePipe, ConfirmDialogComponent, BoardCreateDialogComponent],
  templateUrl: './boards.page.html',
  host: { class: 'board-host' },
})
export class BoardsPage implements OnInit {
  private readonly api = inject(RastroApi);
  private readonly router = inject(Router);
  private readonly i18n = inject(LanguageService);

  protected readonly boards = signal<BoardSummary[] | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly creating = signal<SurveySummary[] | null>(null);
  protected readonly createError = signal<string | null>(null);
  // Erro de uma ação (excluir): separado do erro de carga, para não esconder os quadros que já estão na tela.
  protected readonly actionError = signal<string | null>(null);
  protected readonly opening = signal(false);
  // O quadro foi criado, mas a tela não abriu: o aviso leva o link para ele.
  protected readonly openFailed = signal<BoardDetail | null>(null);
  protected readonly removing = signal<BoardSummary | null>(null);
  protected readonly deleting = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly skeletonRows = SKELETON_ROWS;
  protected readonly shortDate = shortDate;

  ngOnInit(): void {
    this.load();
    const deleted = (history.state as Record<string, unknown> | null)?.[BOARDS_DELETED_STATE];
    if (typeof deleted === 'string') {
      this.showDeleted(deleted);
      history.replaceState({ ...history.state, [BOARDS_DELETED_STATE]: undefined }, '');
    }
  }

  private showDeleted(name: string): void {
    this.notice.set(this.i18n.t('boards.deleted', { name }));
    setTimeout(() => this.notice.set(null), NOTICE_MS);
  }

  protected load(): void {
    this.error.set(null);
    this.api.boards().subscribe({
      next: (boards) => this.boards.set(boards),
      error: (error) => this.error.set(apiErrorMessage(error)),
    });
  }

  // O diálogo de criação escolhe entre os levantamentos prontos: busca a lista ao abrir.
  protected openCreate(): void {
    if (this.opening()) return;
    this.createError.set(null);
    this.openFailed.set(null);
    this.opening.set(true);
    this.api.surveys().subscribe({
      next: (surveys) => {
        this.opening.set(false);
        this.creating.set(surveys);
      },
      error: (error) => {
        this.opening.set(false);
        this.createError.set(apiErrorMessage(error));
      },
    });
  }

  // Navega primeiro: o diálogo só fecha se a tela do quadro abriu. Se a navegação foi cancelada ou falhou, o diálogo
  // fecha do mesmo jeito (o quadro já existe e outro envio criaria um repetido) e o aviso leva o link para o quadro.
  protected onCreated(board: BoardDetail): void {
    this.router.navigate(['/boards', board.id]).then(
      (opened) => this.afterCreate(board, opened),
      () => this.afterCreate(board, false),
    );
  }

  private afterCreate(board: BoardDetail, opened: boolean): void {
    this.creating.set(null);
    this.openFailed.set(opened ? null : board);
  }

  protected askDelete(board: BoardSummary): void {
    this.removing.set(board);
  }

  protected confirmDelete(): void {
    const board = this.removing();
    if (!board) return;
    this.actionError.set(null);
    this.deleting.set(true);
    this.api.deleteBoard(board.id).subscribe({
      next: () => {
        this.deleting.set(false);
        this.removing.set(null);
        this.boards.update((list) => (list ?? []).filter((b) => b.id !== board.id));
        this.showDeleted(board.name);
      },
      error: (error) => {
        this.deleting.set(false);
        this.removing.set(null);
        this.actionError.set(apiErrorMessage(error));
      },
    });
  }
}
