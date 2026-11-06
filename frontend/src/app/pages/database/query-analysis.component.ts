import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { Subscription } from 'rxjs';
import { DbSchema, findTable, sameName, tableKey } from '../../core/database/db-schema';
import {
  QUERY_FINDING_TITLE,
  QUERY_SEVERITY_LABEL,
  QUERY_SEVERITY_SLUG,
  QueryAnalysis,
  QueryFinding,
  QueryTableUse,
  querySegments,
} from '../../core/database/query-analysis';
import { RastroApi, apiErrorMessage } from '../../core/rastro-api.service';

interface Analyzed {
  sql: string;
  analysis: QueryAnalysis;
}

// Aba "Analisar consulta" (Fase 40a): lê uma consulta T-SQL contra o esquema lido do código, com
// regras fixas. Sem IA e sem banco: a mesma consulta dá sempre o mesmo resultado.
@Component({
  selector: 'app-query-analysis',
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

  protected readonly title = QUERY_FINDING_TITLE;
  protected readonly severityLabel = QUERY_SEVERITY_LABEL;
  protected readonly severitySlug = QUERY_SEVERITY_SLUG;

  private sub?: Subscription;

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
    inject(DestroyRef).onDestroy(() => this.sub?.unsubscribe());
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
  }

  protected analyze(): void {
    const sql = this.text();
    if (!sql.trim() || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.active.set(null);
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
