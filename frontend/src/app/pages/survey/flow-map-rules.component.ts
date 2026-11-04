import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { RULE_EFFECT_GLYPH, RULE_EFFECT_LABEL, RULE_EFFECT_SLUG, filterBy } from '../../core/graph/flow-insight';
import { FlowRule, RuleEffect, RuleSource } from '../../core/models';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';

// Lista de regras do fluxo (Fase 32), com filtro por efeito. A fonte de cada regra escolhe o bloco no mapa.
@Component({
  selector: 'app-flow-map-rules',
  templateUrl: './flow-map-rules.component.html',
  styleUrl: './flow-map-rules.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FlowMapRulesComponent {
  private readonly api = inject(RastroApi);

  readonly surveyId = input.required<string>();
  // Nome de cada faixa (id da faixa para o sistema que aparece na fonte).
  readonly laneTitles = input<ReadonlyMap<string, string>>(new Map());
  readonly pickBlock = output<string>();

  protected readonly rules = signal<FlowRule[] | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly effect = signal<RuleEffect | null>(null);
  protected readonly shown = computed(() => filterBy(this.rules() ?? [], (rule) => rule.effect, this.effect()));
  protected readonly filters: { value: RuleEffect | null; label: string }[] = [
    { value: null, label: 'Todas' },
    ...[RuleEffect.Blocks, RuleEffect.Allows, RuleEffect.SideEffect].map((value) => ({ value, label: RULE_EFFECT_LABEL[value] })),
  ];
  protected readonly label = RULE_EFFECT_LABEL;
  protected readonly glyph = RULE_EFFECT_GLYPH;
  protected readonly slug = RULE_EFFECT_SLUG;
  private readonly reload = signal(0);

  constructor() {
    effect((onCleanup) => {
      const id = this.surveyId();
      this.reload();
      untracked(() => this.error.set(null));
      const sub = this.api.rules(id).subscribe({
        next: (rules) => untracked(() => this.rules.set(rules)),
        error: (error) => untracked(() => this.error.set(apiErrorMessage(error))),
      });
      onCleanup(() => sub.unsubscribe());
    });
  }

  protected retry(): void {
    this.reload.update((n) => n + 1);
  }

  protected sourceText(source: RuleSource): string {
    const lane = this.laneTitles().get(source.lane) ?? source.lane;
    return source.location ? `${lane} ${source.location.file}:${source.location.line}` : lane;
  }
}
