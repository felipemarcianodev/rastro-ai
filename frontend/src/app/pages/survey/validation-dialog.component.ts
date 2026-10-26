import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DialogDirective } from '../../core/dialog.directive';
import {
  CHECK_STATUS_LABEL,
  CHECK_STATUS_SLUG,
  CheckStatus,
  VALIDATION_VERDICT_LABEL,
  VALIDATION_VERDICT_SLUG,
  ValidationStatus,
  ValidationVerdict,
  ValidationView,
} from '../../core/models';

// Resultado da validação da mudança (Fase 9): nota, critérios do rascunho, o que mudou fora do
// pedido, build e testes. Aprovar gera a documentação do fluxo.
@Component({
  selector: 'app-validation-dialog',
  imports: [RouterLink, DialogDirective],
  templateUrl: './validation-dialog.component.html',
  styleUrl: './validation-dialog.component.scss',
})
export class ValidationDialogComponent {
  readonly validation = input.required<ValidationView>();
  readonly document = input<string | null>(null);
  readonly busy = input(false);
  readonly message = input<string | null>(null);

  readonly closed = output<void>();
  readonly approve = output<void>();
  readonly revalidate = output<void>();

  protected readonly ValidationStatus = ValidationStatus;
  protected readonly ValidationVerdict = ValidationVerdict;
  protected readonly CheckStatus = CheckStatus;
  protected readonly verdictLabel = VALIDATION_VERDICT_LABEL;
  protected readonly verdictSlug = VALIDATION_VERDICT_SLUG;
  protected readonly checkLabel = CHECK_STATUS_LABEL;
  protected readonly checkSlug = CHECK_STATUS_SLUG;

  // O motivo na linha da nota: quantos critérios falharam e se build ou testes caíram.
  protected failureSummary(): string {
    const result = this.validation().result;
    if (!result || result.verdict === ValidationVerdict.Approved) return '';
    const parts: string[] = [];
    const failed = result.expectations.filter((c) => c.status === CheckStatus.Failed).length;
    if (failed > 0) parts.push(failed === 1 ? '1 critério falhou' : `${failed} critérios falharam`);
    if (result.unexpected.length > 0) parts.push(result.unexpected.length === 1 ? '1 mudança fora do pedido' : `${result.unexpected.length} mudanças fora do pedido`);
    if (result.build.status === CheckStatus.Failed) parts.push('o build falhou');
    if (result.tests.status === CheckStatus.Failed) parts.push('os testes falharam');
    return parts.length > 0 ? parts.join(', ') + '.' : '';
  }

  protected documentUrl(): string {
    return `/api/validations/${this.validation().id}/document`;
  }

  // Mesmo nome que a API manda no Content-Disposition.
  protected documentName(): string {
    return `fluxo-${this.validation().id.replaceAll('-', '')}.md`;
  }

  protected mark(status: CheckStatus): string {
    return status === CheckStatus.Passed ? '✓' : status === CheckStatus.Failed ? '✕' : '–';
  }
}
