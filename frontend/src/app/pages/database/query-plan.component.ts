import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { SqlDialect } from '../../core/database/db-schema';
import { MissingIndex, PLAN_NOTE, PlanOperation, QueryPlan, missingIndexText, operatorLabel } from '../../core/database/live-database';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';

const MIN_PERCENT = 1;

// Cartão "Plano estimado pelo banco" (Fase 40b): o SET SHOWPLAN_XML do SQL Server ou o EXPLAIN sem
// ANALYZE do PostgreSQL (Fase 42b), pedido só pelo botão. O banco diz como faria; a consulta não é executada.
@Component({
  selector: 'app-query-plan',
  templateUrl: './query-plan.component.html',
  styleUrl: './query-plan.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QueryPlanComponent {
  private readonly api = inject(RastroApi);
  private sub?: Subscription;

  readonly repository = input.required<string>();
  readonly sql = input.required<string>();
  readonly dialect = input<SqlDialect>(SqlDialect.SqlServer);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly plan = signal<QueryPlan | null>(null);

  protected readonly note = computed(() => PLAN_NOTE[this.dialect()]);
  protected readonly showAll = signal(false);

  // Operação com menos de 1% do custo não diz nada: fica atrás de "mais N operações" (UX-DBL-04).
  protected readonly main = computed(() => (this.plan()?.operations ?? []).filter((o) => o.percent >= MIN_PERCENT));
  protected readonly minor = computed(() => (this.plan()?.operations ?? []).filter((o) => o.percent < MIN_PERCENT));

  // Resumo anunciado quando o plano chega: o restante do cartão é lido pela navegação normal.
  protected readonly summary = computed(() => {
    const plan = this.plan();
    if (!plan) return '';
    const ops = plan.operations.length;
    const missing = plan.missingIndexes.length;
    return `Plano estimado recebido: ${ops === 1 ? '1 operação' : ops + ' operações'}, ${missing === 0 ? 'nenhum índice sugerido' : missing === 1 ? '1 índice sugerido' : missing + ' índices sugeridos'}.`;
  });

  // O que o leitor de tela ouve: a espera, e depois o resumo do plano.
  protected readonly announcement = computed(() => (this.busy() ? 'Pedindo o plano…' : this.summary()));

  constructor() {
    inject(DestroyRef).onDestroy(() => this.sub?.unsubscribe());
  }

  protected request(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.sub?.unsubscribe();
    this.sub = this.api.queryPlan(this.repository(), this.sql()).subscribe({
      next: (plan) => {
        this.busy.set(false);
        this.plan.set(plan);
        this.showAll.set(false);
      },
      error: (error) => {
        this.busy.set(false);
        this.plan.set(null);
        this.error.set(apiErrorMessage(error));
      },
    });
  }

  protected label(operation: PlanOperation): string {
    return operatorLabel(operation.physicalOp);
  }

  protected percentText(operation: PlanOperation): string {
    return `${Math.round(operation.percent)}%`;
  }

  protected barWidth(operation: PlanOperation): number {
    return Math.min(100, Math.max(0, operation.percent));
  }

  protected indexText(index: MissingIndex): string {
    return missingIndexText(index);
  }
}
