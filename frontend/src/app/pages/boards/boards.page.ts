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
  protected readonly removing = signal<BoardSummary | null>(null);
  protected readonly deleting = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly skeletonRows = SKELETON_ROWS;
  protected readonly shortDate = shortDate;

  ngOnInit(): void {
    this.load();
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
    this.createError.set(null);
    this.api.surveys().subscribe({
      next: (surveys) => this.creating.set(surveys),
      error: (error) => this.createError.set(apiErrorMessage(error)),
    });
  }

  protected onCreated(board: BoardDetail): void {
    this.creating.set(null);
    this.router.navigate(['/boards', board.id]);
  }

  protected askDelete(board: BoardSummary): void {
    this.removing.set(board);
  }

  protected confirmDelete(): void {
    const board = this.removing();
    if (!board) return;
    this.deleting.set(true);
    this.api.deleteBoard(board.id).subscribe({
      next: () => {
        this.deleting.set(false);
        this.removing.set(null);
        this.boards.update((list) => (list ?? []).filter((b) => b.id !== board.id));
        this.notice.set(this.i18n.t('boards.deleted', { name: board.name }));
        setTimeout(() => this.notice.set(null), NOTICE_MS);
      },
      error: (error) => {
        this.deleting.set(false);
        this.removing.set(null);
        this.error.set(apiErrorMessage(error));
      },
    });
  }
}
