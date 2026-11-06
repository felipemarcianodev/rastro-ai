import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { MissingIndex, PLAN_SHOWPLAN_NOTE, PlanOperation, QueryPlan, missingIndexText, operatorLabel } from '../../core/database/live-database';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';

const MIN_PERCENT = 1;

// Cartão "Plano estimado pelo banco" (Fase 40b): o SET SHOWPLAN_XML do banco vivo, pedido só pelo
// botão. O banco diz como faria; a consulta não é executada.
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

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly plan = signal<QueryPlan | null>(null);

  protected readonly note = PLAN_SHOWPLAN_NOTE;
  protected readonly showAll = signal(false);

  // Operação com menos de 1% do custo não diz nada: fica atrás de "mais N operações" (UX-DBL-04).
  protected readonly main = computed(() => (this.plan()?.operations ?? []).filter((o) => o.percent >= MIN_PERCENT));
  protected readonly minor = computed(() => (this.plan()?.operations ?? []).filter((o) => o.percent < MIN_PERCENT));

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
