import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { DbSchema, SQL_DIALECT_LABEL, SQL_READER_LABEL, SqlDialect, findTable, sameName, tableKey } from '../../core/database/db-schema';
import {
  QUERY_FINDING_TITLE,
  QUERY_SEVERITY_LABEL,
  QUERY_SEVERITY_SLUG,
  QueryAnalysis,
  QueryFinding,
  QueryTableUse,
  exampleQuery,
  querySegments,
} from '../../core/database/query-analysis';
import { QueryAiSuggestion } from '../../core/database/query-suggestion';
import { AiUse, aiCan } from '../../core/models';
import { AiDisclosureComponent } from '../../core/ai-disclosure.component';
import { AiStatusService } from '../../core/ai-status.service';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';
import { QueryPlanComponent } from './query-plan.component';
import { QuerySuggestionComponent } from './query-suggestion.component';

// Códigos de erro da API (ErrorCodes) que só as Configurações resolvem.
const AI_NOT_CONFIGURED_CODE = 'AI_NOT_CONFIGURED';
const AI_BUDGET_EXCEEDED_CODE = 'AI_BUDGET_EXCEEDED';
const SETTINGS_FIX_ERROR_CODES: ReadonlySet<string> = new Set([AI_NOT_CONFIGURED_CODE, AI_BUDGET_EXCEEDED_CODE]);

interface Analyzed {
  sql: string;
  analysis: QueryAnalysis;
}

// Aba "Analisar consulta" (Fase 40a): lê uma consulta no dialeto do repositório (T-SQL ou, desde a
// Fase 42, PostgreSQL) contra o esquema lido do código, com
// regras fixas. Sem IA e sem banco: a mesma consulta dá sempre o mesmo resultado.
@Component({
  selector: 'app-query-analysis',
  imports: [RouterLink, QueryPlanComponent, QuerySuggestionComponent, AiDisclosureComponent],
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
  // O dialeto da conexão viva (Fase 42b): a nota do plano diz como ele foi pedido.
  readonly liveDialect = input<SqlDialect | null>(null);
  // ?consulta= e ?origem= da rota: o texto que veio de um passo do fluxo.
  readonly initial = input<string | undefined>();
  readonly origin = input<string | undefined>();
  // Pedido para ver uma tabela no mapa; a página troca de aba.
  readonly pickTable = output<string>();
  // Sem banco vivo, a lateral oferece conectar um (UX-G-B04); a página abre o diálogo.
  readonly connect = output<void>();

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
  // Enquanto o estado da IA ainda não chegou, a dica "ligue o uso de sugestões" não aparece (UX-G-B11).
  protected readonly aiLoaded = computed(() => this.ai.status() !== null);
  protected readonly suggesting = signal(false);
  protected readonly suggestError = signal<string | null>(null);
  protected readonly suggestion = signal<QueryAiSuggestion | null>(null);
  // Texto da região viva: a espera e a chegada da resposta (o erro tem o próprio role="alert").
  protected readonly suggestAnnouncement = computed(() => {
    if (this.suggesting()) return 'Perguntando à IA…';
    return this.suggestion() ? 'A sugestão da IA chegou.' : '';
  });

  // Chave ausente ou orçamento esgotado: repetir não adianta, o conserto está nas Configurações (UX-FIM-Q08).
  protected readonly suggestErrorNeedsSettings = signal(false);

  private sub?: Subscription;
  private suggestSub?: Subscription;
  // A última sugestão por texto da consulta (UX-G-B05): editar a consulta não a perde, e o mesmo texto de volta a mostra sem gastar de novo.
  private readonly suggestions = new Map<string, QueryAiSuggestion>();

  protected readonly segments = computed(() => {
    const r = this.result();
    return r ? querySegments(r.sql, r.analysis.findings).map((s) => ({ ...s, key: s.findings.length ? '|' + s.findings.join('|') + '|' : '' })) : [];
  });

  protected readonly analyzeHelp = computed(() =>
    this.text().trim() ? 'Ctrl+Enter também analisa. A análise não consulta o banco.' : 'Escreva ou cole uma consulta, ou use o exemplo, para poder analisar. A análise não consulta o banco.',
  );

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
        // UX-DBQ-05: quem chega pelo "Analisar a consulta" do passo (consulta e origem na URL) já vê a análise.
        if (initial?.trim() && this.origin()) this.analyze();
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

  private suggestionKey(sql: string): string {
    return `${this.repository()}\u0000${sql}`;
  }

  private discardSuggestion(): void {
    this.suggestSub?.unsubscribe();
    this.suggesting.set(false);
    this.suggestion.set(null);
    this.suggestError.set(null);
    this.suggestErrorNeedsSettings.set(false);
  }

  // A resposta da IA chega abaixo dos achados, fora da vista: rola só até o começo da seção (UX-DBA-01).
  // O foco vai ao título da seção, que vem antes da sugestão e do erro (UX-G-B10).
  private revealSuggestion(): void {
    setTimeout(() => {
      this.host.nativeElement.querySelector<HTMLElement>('.qa-ai')?.scrollIntoView({ block: 'start', behavior: 'smooth' });
      this.host.nativeElement.querySelector<HTMLElement>('#qa-ai-title')?.focus({ preventScroll: true });
    });
  }

  protected suggest(): void {
    const sql = this.result()?.sql;
    if (!sql || this.suggesting()) return;
    this.suggesting.set(true);
    this.suggestError.set(null);
    this.suggestErrorNeedsSettings.set(false);
    this.suggestSub?.unsubscribe();
    this.suggestSub = this.api.suggestQuery(this.repository(), sql).subscribe({
      next: (suggestion) => {
        this.suggesting.set(false);
        this.suggestions.set(this.suggestionKey(sql), suggestion);
        this.suggestion.set(suggestion);
        this.revealSuggestion();
      },
      error: (error) => {
        this.suggesting.set(false);
        this.suggestError.set(apiErrorMessage(error));
        const code = error instanceof HttpErrorResponse ? (error.error?.code as string | undefined) : undefined;
        this.suggestErrorNeedsSettings.set(!!code && SETTINGS_FIX_ERROR_CODES.has(code));
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
        const remembered = this.suggestions.get(this.suggestionKey(sql));
        if (remembered) this.suggestion.set(remembered);
        this.focusTitle();
      },
      error: (error) => {
        this.busy.set(false);
        this.error.set(apiErrorMessage(error));
      },
    });
  }

  // Depois de analisar, o foco vai ao título do resultado, sem rolar a página.
  private focusTitle(): void {
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>('#qa-result-title')?.focus({ preventScroll: true }));
  }

  protected useExample(): void {
    this.edit(exampleQuery(this.dialect(), this.schema()));
    setTimeout(() => this.box()?.nativeElement.focus({ preventScroll: true }));
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
    return segmentFindings.length ? Math.max(...segmentFindings.map((i) => findings[i].severity)) : null;
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
