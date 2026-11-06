import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { Subscription } from 'rxjs';
import { DbSchema, SQL_DIALECT_LABEL, SQL_READER_LABEL, SqlDialect, findTable, sameName, tableKey } from '../../core/database/db-schema';
import {
  QUERY_FINDING_TITLE,
  QUERY_SEVERITY_LABEL,
  QUERY_SEVERITY_SLUG,
  QueryAnalysis,
  QueryFinding,
  QueryTableUse,
  querySegments,
} from '../../core/database/query-analysis';
import { QueryAiSuggestion } from '../../core/database/query-suggestion';
import { AI_PROVIDER_LABEL, AiUse, aiCan } from '../../core/models';
import { AiStatusService } from '../../core/ai-status.service';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';
import { QueryPlanComponent } from './query-plan.component';
import { QuerySuggestionComponent } from './query-suggestion.component';

interface Analyzed {
  sql: string;
  analysis: QueryAnalysis;
}

// Aba "Analisar consulta" (Fase 40a): lê uma consulta no dialeto do repositório (T-SQL ou, desde a
// Fase 42, PostgreSQL) contra o esquema lido do código, com
// regras fixas. Sem IA e sem banco: a mesma consulta dá sempre o mesmo resultado.
@Component({
  selector: 'app-query-analysis',
  imports: [QueryPlanComponent, QuerySuggestionComponent],
  templateUrl: './query-analysis.component.html',
  styleUrl: './query-analysis.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QueryAnalysisComponent {
  private readonly api = inject(RastroApi);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly box = viewChild<ElementRef<HTMLTextAreaElement>>('box');

  readonly repository = input.required<string>();
  readonly schema = input<DbSchema | null>(null);
  // Com conexão ao banco vivo, o plano estimado pode ser pedido (Fase 40b).
  readonly live = input(false);
  // ?consulta= e ?origem= da rota: o texto que veio de um passo do fluxo.
  readonly initial = input<string | undefined>();
  readonly origin = input<string | undefined>();
  // Pedido para ver uma tabela no mapa; a página troca de aba.
  readonly pickTable = output<string>();

  protected readonly text = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly result = signal<Analyzed | null>(null);
  protected readonly active = signal<number | null>(null);

  // O dialeto sai do repositório (Fase 42): o rótulo da caixa e a mensagem de erro de leitura.
  protected readonly dialect = computed(() => this.schema()?.dialect ?? SqlDialect.SqlServer);
  protected readonly dialectLabel = SQL_DIALECT_LABEL;
  protected readonly readerLabel = SQL_READER_LABEL;
  protected readonly title = QUERY_FINDING_TITLE;
  protected readonly severityLabel = QUERY_SEVERITY_LABEL;
  protected readonly severitySlug = QUERY_SEVERITY_SLUG;

  private readonly ai = inject(AiStatusService);
  // Sugestão da IA (Fase 41): só com a IA ligada e o uso "Sugestões"; sem isso, nada aparece.
  protected readonly canSuggest = computed(() => aiCan(this.ai.status(), AiUse.Suggestions));
  protected readonly providerName = computed(() => {
    const status = this.ai.status();
    return status ? AI_PROVIDER_LABEL[status.provider] : '';
  });
  protected readonly suggesting = signal(false);
  protected readonly suggestError = signal<string | null>(null);
  protected readonly suggestion = signal<QueryAiSuggestion | null>(null);

  private sub?: Subscription;
  private suggestSub?: Subscription;

  protected readonly segments = computed(() => {
    const r = this.result();
    return r ? querySegments(r.sql, r.analysis.findings).map((s) => ({ ...s, key: s.findings.length ? '|' + s.findings.join('|') + '|' : '' })) : [];
  });

  protected readonly countText = computed(() => {
    const n = this.result()?.analysis.findings.length ?? 0;
    return n === 0 ? 'Sem achados' : n === 1 ? '1 achado' : `${n} achados`;
  });

  // "Veio do passo" só vale enquanto o texto é o que veio do passo: editado, a consulta é outra.
  protected readonly shownOrigin = computed(() => {
    const origin = this.origin();
    return origin && this.text() === (this.initial() ?? '') ? origin : null;
  });

  constructor() {
    // A consulta que veio da rota preenche a caixa; mudar a rota descarta o resultado anterior.
    effect(() => {
      const initial = this.initial();
      this.repository();
      untracked(() => {
        this.discard();
        this.text.set(initial ?? '');
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.sub?.unsubscribe();
      this.suggestSub?.unsubscribe();
    });
  }

  protected edit(value: string): void {
    this.text.set(value);
    this.discard();
  }

  private discard(): void {
    this.sub?.unsubscribe();
    this.busy.set(false);
    this.result.set(null);
    this.error.set(null);
    this.active.set(null);
    this.discardSuggestion();
  }

  private discardSuggestion(): void {
    this.suggestSub?.unsubscribe();
    this.suggesting.set(false);
    this.suggestion.set(null);
    this.suggestError.set(null);
  }

  // A resposta da IA chega abaixo dos achados, fora da vista: rola só até o começo da seção (UX-DBA-01).
  private revealSuggestion(): void {
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>('.qa-ai')?.scrollIntoView({ block: 'start', behavior: 'smooth' }));
  }

  protected suggest(): void {
    const sql = this.result()?.sql;
    if (!sql || this.suggesting()) return;
    this.suggesting.set(true);
    this.suggestError.set(null);
    this.suggestSub?.unsubscribe();
    this.suggestSub = this.api.suggestQuery(this.repository(), sql).subscribe({
      next: (suggestion) => {
        this.suggesting.set(false);
        this.suggestion.set(suggestion);
        this.revealSuggestion();
      },
      error: (error) => {
        this.suggesting.set(false);
        this.suggestError.set(apiErrorMessage(error));
        this.revealSuggestion();
      },
    });
  }

  protected analyze(): void {
    const sql = this.text();
    if (!sql.trim() || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.active.set(null);
    this.discardSuggestion();
    this.sub?.unsubscribe();
    this.sub = this.api.analyzeQuery(this.repository(), sql).subscribe({
      next: (analysis) => {
        this.busy.set(false);
        this.result.set({ sql, analysis });
      },
      error: (error) => {
        this.busy.set(false);
        this.error.set(apiErrorMessage(error));
      },
    });
  }

  protected backToEditing(): void {
    this.discard();
    // Sem rolar: o foco volta à caixa sem mexer na página.
    setTimeout(() => this.box()?.nativeElement.focus({ preventScroll: true }));
  }

  protected toggle(index: number): void {
    const next = this.active() === index ? null : index;
    this.active.set(next);
    if (next === null) return;
    // Rola só o trecho para a vista dentro da consulta.
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>(`mark[data-f*="|${next}|"]`)?.scrollIntoView({ block: 'nearest', inline: 'nearest' }));
  }

  protected severityOf(segmentFindings: number[]): QueryFinding['severity'] | null {
    const findings = this.result()?.analysis.findings ?? [];
    return segmentFindings.length ? Math.min(...segmentFindings.map((i) => findings[i].severity)) : null;
  }

  protected isActive(segmentFindings: number[]): boolean {
    const active = this.active();
    return active !== null && segmentFindings.includes(active);
  }

  // A tabela do achado só leva ao mapa quando está no esquema lido do código.
  protected tableOf(finding: QueryFinding): string | null {
    const schema = this.schema();
    if (!finding.table || !schema) return null;
    const used = this.result()?.analysis.tables.find((t) => sameName(t.name, finding.table!));
    if (used && !used.inSchema) return null;
    const table = findTable(schema, finding.table);
    return table ? tableKey(table) : null;
  }

  protected tableLink(use: QueryTableUse): string | null {
    const schema = this.schema();
    if (!use.inSchema || !schema) return null;
    const table = findTable(schema, use.name);
    return table ? tableKey(table) : null;
  }
}
