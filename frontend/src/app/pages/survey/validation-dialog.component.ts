import { Component, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { DialogDirective } from '../../core/dialog.directive';
import { validationFailureText } from '../../core/failure-text';
import { LanguageService } from '../../core/i18n/i18n';
import {
  CHECK_STATUS_LABEL_KEY,
  CHECK_STATUS_SLUG,
  CheckStatus,
  VALIDATION_VERDICT_LABEL_KEY,
  VALIDATION_VERDICT_SLUG,
  ValidationStatus,
  ValidationVerdict,
  ValidationView,
} from '../../core/models';

// Resultado da validação da mudança (Fase 9): nota, critérios do rascunho, o que mudou fora do
// pedido, build e testes. Aprovar gera a documentação do fluxo.
@Component({
  selector: 'app-validation-dialog',
  imports: [RouterLink, TranslatePipe, DialogDirective],
  templateUrl: './validation-dialog.component.html',
  styleUrl: './validation-dialog.component.scss',
})
export class ValidationDialogComponent {
  private readonly i18n = inject(LanguageService);

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
  protected readonly verdictKey = VALIDATION_VERDICT_LABEL_KEY;
  protected readonly failureText = validationFailureText;
  protected readonly verdictSlug = VALIDATION_VERDICT_SLUG;
  protected readonly checkKey = CHECK_STATUS_LABEL_KEY;
  protected readonly checkSlug = CHECK_STATUS_SLUG;

  // O motivo na linha da nota: quantos critérios falharam e se build ou testes caíram.
  protected failureSummary(): string {
    const result = this.validation().result;
    if (!result || result.verdict === ValidationVerdict.Approved) return '';
    const parts: string[] = [];
    const failed = result.expectations.filter((c) => c.status === CheckStatus.Failed).length;
    if (failed > 0) parts.push(this.i18n.t('validation.failedCriteria', { count: failed }));
    if (result.unexpected.length > 0) parts.push(this.i18n.t('validation.unexpectedChanges', { count: result.unexpected.length }));
    if (result.build.status === CheckStatus.Failed) parts.push(this.i18n.t('validation.buildFailed'));
    if (result.tests.status === CheckStatus.Failed) parts.push(this.i18n.t('validation.testsFailed'));
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
