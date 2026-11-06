import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { QUERY_FINDING_TITLE } from '../../core/database/query-analysis';
import { QueryAiSuggestion, findingsText, formatCost } from '../../core/database/query-suggestion';
import { AI_PROVIDER_LABEL } from '../../core/models';
import { CopyFeedback } from '../../core/copy-feedback';

// Resultado da sugestão da IA para a consulta (Fase 41): a reescrita, as mudanças e os índices,
// todos já conferidos pelo Rastro. A tela só mostra e copia: nada é aplicado.
@Component({
  selector: 'app-query-suggestion',
  templateUrl: './query-suggestion.component.html',
  styleUrl: './query-suggestion.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuerySuggestionComponent {
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
    return after ? `${findingsText(review.findingsBefore)} antes, ${findingsText(after.findings.length)} depois` : null;
  });

  protected readonly cost = computed(() => {
    const plan = this.suggestion().review.plan;
    if (!plan) return null;
    const before = plan.costBefore;
    const after = plan.costAfter;
    const text = before !== null && after !== null ? `Custo estimado pelo banco: ${formatCost(before)} antes, ${formatCost(after)} depois` : null;
    return { text, problem: plan.problem };
  });

  protected copyIndex(index: number, sql: string): void {
    this.copiedIndex = index;
    this.indexCopy.copy(sql);
  }

  protected indexLabel(index: number): string {
    return this.copiedIndex === index ? this.indexCopy.label('Copiar') : 'Copiar';
  }

  protected columns(values: string[]): string {
    return values.join(', ');
  }
}
