import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { AiStatusService } from './ai-status.service';
import { LanguageService } from './i18n/i18n';
import { AI_PROVIDER_LABEL } from './models';
import { currentLocale } from './i18n/locale';

// Aviso único do que vai para o provedor de IA e do que o pedido gasta (UX-FIM-Q06). Fica visível
// antes de cada pedido, inclusive "pedir outra", que gasta de novo. Sem IA ligada e configurada,
// não mostra nada (sem aviso e sem botão morto).
@Component({
  selector: 'app-ai-disclosure',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (ai.active()) {
      <span class="muted ai-disclosure">{{ text() }}</span>
    }
  `,
  styles: `
    .ai-disclosure {
      font-size: var(--text-xs);
    }
  `,
})
export class AiDisclosureComponent {
  protected readonly ai = inject(AiStatusService);
  private readonly i18n = inject(LanguageService);

  // O que vai para o provedor, em uma oração sem ponto final (ex.: "a consulta e o esquema das tabelas citadas").
  readonly goes = input.required<string>();
  // O que nunca vai (opcional), já com ponto final.
  readonly never = input('');

  protected readonly provider = computed(() => {
    const status = this.ai.status();
    return status ? AI_PROVIDER_LABEL[status.provider] : '';
  });

  protected readonly text = computed(() =>
    [this.i18n.t('aiDisclosure.goes', { provider: this.provider(), goes: this.goes() }), this.never(), this.i18n.t('aiDisclosure.cost'), this.usage()].filter(Boolean).join(' '),
  );

  private readonly usage = computed(() => {
    const status = this.ai.status();
    if (!status) return '';
    const used = status.tokensToday.toLocaleString(currentLocale());
    return status.dailyTokenBudget > 0
      ? this.i18n.t('aiDisclosure.usageLimited', { used, budget: status.dailyTokenBudget.toLocaleString(currentLocale()) })
      : this.i18n.t('aiDisclosure.usageUnlimited', { used });
  });
}
