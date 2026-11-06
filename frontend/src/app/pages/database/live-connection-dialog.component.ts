import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, output, signal } from '@angular/core';
import { DialogDirective } from '../../core/dialog.directive';
import { LIVE_PERMISSION_GRANTS, LIVE_PERMISSION_SELECT, LIVE_PERMISSION_SHOWPLAN, LiveConnectionTest, LiveConnectionView } from '../../core/database/live-database';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';

// Diálogo do banco vivo (Fase 40b, ADR-006): cadastra, testa e remove a conexão só leitura do
// repositório. A string de conexão é digitada aqui e nunca volta da API: o campo nunca vem preenchido.
@Component({
  selector: 'app-live-connection-dialog',
  imports: [DialogDirective],
  templateUrl: './live-connection-dialog.component.html',
  styleUrl: './live-connection-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LiveConnectionDialogComponent {
  private readonly api = inject(RastroApi);
  private closing = false;

  readonly repository = input.required<string>();
  readonly connection = input<LiveConnectionView | null>(null);
  readonly closed = output<void>();
  // Salvou ou removeu: a tela recarrega o esquema e a conexão.
  readonly changed = output<void>();

  protected readonly key = signal('');
  protected readonly busy = signal(false);
  protected readonly confirmingRemove = signal(false);
  protected readonly saved = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly testResult = signal<LiveConnectionTest | null>(null);

  protected readonly grants = LIVE_PERMISSION_GRANTS;
  protected readonly showplan = LIVE_PERMISSION_SHOWPLAN;
  protected readonly select = LIVE_PERMISSION_SELECT;

  constructor() {
    inject(DestroyRef).onDestroy(() => (this.closing = true));
  }

  protected save(): void {
    const value = this.key().trim();
    if (!value || this.busy()) return;
    this.begin();
    this.api.saveConnection(this.repository(), value).subscribe({
      next: () => {
        this.busy.set(false);
        // A string já foi: o campo esvazia e a tela passa a mostrar só o servidor e o banco.
        this.key.set('');
        this.saved.set(true);
        this.changed.emit();
      },
      error: (error) => this.fail(error),
    });
  }

  protected test(): void {
    if (this.busy() || !this.connection()?.configured) return;
    this.begin();
    this.api.testConnection(this.repository()).subscribe({
      next: (result) => {
        this.busy.set(false);
        this.testResult.set(result);
      },
      error: (error) => this.fail(error),
    });
  }

  protected remove(): void {
    if (this.busy()) return;
    this.begin();
    this.api.removeConnection(this.repository()).subscribe({
      next: () => {
        this.busy.set(false);
        this.changed.emit();
        this.closed.emit();
      },
      error: (error) => this.fail(error),
    });
  }

  private begin(): void {
    this.busy.set(true);
    this.error.set(null);
    this.saved.set(false);
    this.testResult.set(null);
  }

  private fail(error: unknown): void {
    if (this.closing) return;
    this.busy.set(false);
    this.confirmingRemove.set(false);
    this.error.set(apiErrorMessage(error));
  }
}
