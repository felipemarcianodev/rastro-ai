import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { QUERY_FINDING_TITLE } from '../../core/database/query-analysis';
import { QueryAiSuggestion, formatCost } from '../../core/database/query-suggestion';
import { AI_PROVIDER_LABEL } from '../../core/models';
import { CopyFeedback } from '../../core/copy-feedback';
import { LanguageService } from '../../core/i18n/i18n';

// Resultado da sugestão da IA para a consulta (Fase 41): a reescrita, as mudanças e os índices,
// todos já conferidos pelo Rastro. A tela só mostra e copia: nada é aplicado.
@Component({
  selector: 'app-query-suggestion',
  imports: [TranslatePipe],
  templateUrl: './query-suggestion.component.html',
  styleUrl: './query-suggestion.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuerySuggestionComponent {
  private readonly i18n = inject(LanguageService);

  readonly suggestion = input.required<QueryAiSuggestion>();
  // A consulta original, para o lado "Antes".
  readonly sql = input.required<string>();

  protected readonly title = QUERY_FINDING_TITLE;
  protected readonly rewriteCopy = new CopyFeedback();
  protected readonly indexCopy = new CopyFeedback();
  protected copiedIndex = -1;

  protected readonly provider = computed(() => AI_PROVIDER_LABEL[this.suggestion().provider]);
  protected readonly rewrite = computed(() => this.suggestion().review.rewrite);

  protected readonly summary = computed(() => {
    const review = this.suggestion().review;
    const after = review.rewrite?.after;
    return after ? this.i18n.t('querySuggestion.summary', { before: review.findingsBefore, after: after.findings.length }) : null;
  });

  protected readonly cost = computed(() => {
    const plan = this.suggestion().review.plan;
    if (!plan) return null;
    const before = plan.costBefore;
    const after = plan.costAfter;
    const text = before !== null && after !== null ? this.i18n.t('querySuggestion.cost', { before: formatCost(before), after: formatCost(after) }) : null;
    return { text, problem: plan.problem };
  });

  protected copyIndex(index: number, sql: string): void {
    this.copiedIndex = index;
    this.indexCopy.copy(sql);
  }

  protected indexLabel(index: number): string {
    const idle = this.i18n.t('common.copy');
    return this.copiedIndex === index ? this.indexCopy.label(idle) : idle;
  }

  // Resposta sem reescrita, sem mudança e sem índice (UX-FIM-Q16).
  protected readonly isEmpty = computed(() => {
    const review = this.suggestion().review;
    return !review.rewrite && review.changes.length === 0 && review.indexes.length === 0;
  });

  // Texto da região viva do índice copiado (UX-FIM-Q04): só o índice que foi copiado fala.
  protected indexStatus(index: number): string {
    return this.copiedIndex === index && this.indexCopy.status() !== 'idle' ? this.indexCopy.label('') : '';
  }

  protected columns(values: string[]): string {
    return values.join(', ');
  }
}
