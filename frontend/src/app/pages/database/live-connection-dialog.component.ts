import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, inject, input, output, signal } from '@angular/core';
import { CopyFeedback } from '../../core/copy-feedback';
import { DialogDirective } from '../../core/dialog.directive';
import { SQL_DIALECT_LABEL, SqlDialect } from '../../core/database/db-schema';
import {
  LIVE_CONNECTION_PLACEHOLDER,
  LIVE_PERMISSION_COMMANDS,
  LiveConnectionTest,
  LiveConnectionView,
} from '../../core/database/live-database';
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
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly repository = input.required<string>();
  readonly connection = input<LiveConnectionView | null>(null);
  // O dialeto da conexão salva ou, sem ela, o do repositório (Fase 42b): a permissão mínima de cada um.
  readonly dialect = input<SqlDialect>(SqlDialect.SqlServer);
  readonly closed = output<void>();
  // Salvou ou removeu: a tela recarrega o esquema e a conexão.
  readonly changed = output<void>();
  // Só a remoção: a tela avisa que voltou a ler só o código.
  readonly removed = output<void>();

  protected readonly key = signal('');
  protected readonly busy = signal(false);
  protected readonly confirmingRemove = signal(false);
  protected readonly saved = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly testResult = signal<LiveConnectionTest | null>(null);

  protected readonly commands = computed(() => LIVE_PERMISSION_COMMANDS[this.dialect()]);
  protected readonly commandsCopy = new CopyFeedback();
  protected readonly placeholder = LIVE_CONNECTION_PLACEHOLDER;
  protected readonly dialectLabel = SQL_DIALECT_LABEL;
  protected readonly SqlDialect = SqlDialect;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.closing = true;
      this.commandsCopy.reset();
    });
  }

  protected copyCommands(): void {
    this.commandsCopy.copy(this.commands().join('\n'));
  }

  // Clique no fundo fecha, menos com a string digitada: um clique sem querer apagaria o que a pessoa colou (B23).
  protected dismiss(): void {
    if (!this.key().trim()) this.closed.emit();
  }

  protected save(): void {
    const value = this.key().trim();
    if (!value || this.busy() || this.confirmingRemove()) return;
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

  // A pergunta de remover foca "Manter" (o caminho seguro) e, ao fechar, devolve o foco ao "Remover conexão" (UX-FIM-B04).
  protected askRemove(): void {
    this.confirmingRemove.set(true);
    this.focusLater('#live-keep');
  }

  protected keepConnection(): void {
    this.confirmingRemove.set(false);
    this.focusLater('#live-remove-ask');
  }

  private focusLater(selector: string): void {
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus());
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
        this.removed.emit();
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
