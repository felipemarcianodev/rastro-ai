import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, input, output, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HOME_FROM_DB_PARAM, HOME_FROM_DB_VALUE } from '../../core/database/query-analysis';
import { DB_OPERATION_SHORT, DB_OPERATION_SLUG, FINDING_KIND_LABEL, FINDING_SEVERITY_LABEL } from '../../core/models';
import {
  Cardinality,
  DELETE_ACTION_LABEL,
  DIVERGENCE_LABEL,
  DbRelation,
  DbSchema,
  DIVERGENT_TEXT,
  DbUsageReport,
  SCHEMA_FINDING_TITLE,
  KEY_MARK_LABEL,
  SCHEMA_SOURCE_LABEL,
  columnKeys,
  findTable,
  findingText,
  keyMark,
  keyText,
  linkTableOf,
  sameName,
  sourceText,
  tableKey,
  estimatedRowsText,
} from '../../core/database/db-schema';

// O que está escolhido na tela do banco.
export enum DbItemKind {
  Table = 0,
  View = 1,
  Procedure = 2,
}

// O título do painel: é nele que o foco cai ao escolher uma tabela (UX-G-B01).
export const DB_PANEL_TITLE_SELECTOR = '.db-panel-head h2';

export interface DbPick {
  kind: DbItemKind;
  key: string;
}

const CARDINALITY_TEXT: Record<Cardinality, string> = {
  [Cardinality.ManyToOne]: 'N para 1',
  [Cardinality.OneToOne]: '1 para 1',
};

const CARDINALITY_BACK: Record<Cardinality, string> = {
  [Cardinality.ManyToOne]: '1 para N',
  [Cardinality.OneToOne]: '1 para 1',
};

// Painel da tela do banco (Fase 38): campos, índices, relações, divergência entre fontes e achados
// sem IA da tabela; colunas e tabelas lidas da view; parâmetros e tabelas da procedure.
@Component({
  selector: 'app-db-panel',
  imports: [RouterLink],
  templateUrl: './db-panel.component.html',
  styleUrl: './db-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DbPanelComponent {
  readonly repository = input.required<string>();
  readonly schema = input.required<DbSchema>();
  readonly relations = input.required<readonly DbRelation[]>();
  readonly selection = input.required<DbPick>();
  readonly usage = input<DbUsageReport | null>(null);
  readonly usageError = input(false);
  // Chegou pelo "Ver no mapa" de um achado da análise (UX-G-B23): o painel oferece voltar a ela.
  readonly fromAnalysis = input(false);
  readonly pick = output<DbPick>();
  readonly closed = output<void>();
  readonly backToAnalysis = output<void>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  // Escolheu algo dentro do painel (UX-G-B02): o botão clicado some com o painel antigo, então o foco vai ao título do novo.
  private focusTitleNext = false;

  constructor() {
    effect(() => {
      this.selection();
      untracked(() => {
        if (!this.focusTitleNext) return;
        this.focusTitleNext = false;
        setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>(DB_PANEL_TITLE_SELECTOR)?.focus({ preventScroll: true }));
      });
    });
  }

  // O levantamento leva o repositório e a origem; a volta ao banco sai da origem (UX-G-B25).
  protected readonly surveyParams = computed(() => ({ repositorio: this.repository(), [HOME_FROM_DB_PARAM]: HOME_FROM_DB_VALUE }));
  protected readonly DbItemKind = DbItemKind;
  protected readonly sourceLabel = SCHEMA_SOURCE_LABEL;
  protected readonly findingTitle = SCHEMA_FINDING_TITLE;
  protected readonly sourceText = sourceText;
  protected readonly findingText = findingText;
  protected readonly opShort = DB_OPERATION_SHORT;
  protected readonly opSlug = DB_OPERATION_SLUG;
  protected readonly kindLabel = FINDING_KIND_LABEL;
  protected readonly severityLabel = FINDING_SEVERITY_LABEL;

  // Os passos dos fluxos que usam a tabela ou a procedure escolhida, e os achados deles (Fase 39).
  protected readonly flows = computed(() => {
    const report = this.usage();
    const pick = this.selection();
    if (!report || pick.kind === DbItemKind.View) return null;
    return {
      uses: report.uses.filter((u) => sameName(u.target, pick.key)),
      findings: report.findings.filter((f) => sameName(f.target, pick.key)),
    };
  });

  protected readonly table = computed(() => {
    const pick = this.selection();
    if (pick.kind !== DbItemKind.Table) return null;
    const schema = this.schema();
    const table = schema.tables.find((t) => tableKey(t) === pick.key);
    if (!table) return null;
    const key = tableKey(table);
    const columns = table.columns.map((c) => {
      const keys = columnKeys(table, c.name);
      return {
        name: c.name,
        type: c.type ?? DIVERGENT_TEXT,
        divergent: c.type == null || c.nullable == null,
        nullable: c.nullable == null ? DIVERGENT_TEXT : c.nullable ? 'sim' : 'não',
        nullableDivergent: c.nullable == null,
        mark: KEY_MARK_LABEL[keyMark(keys)],
        keyText: keyText(keys),
        identity: c.identity,
        personal: c.personal,
        default: c.default,
      };
    });
    const indexes = [
      ...(table.primaryKey ? [{ name: table.primaryKey.name ?? 'chave primária', columns: table.primaryKey.columns.join(', '), extra: 'chave primária', convention: false }] : []),
      ...table.indexes.map((i) => ({
        name: i.name ?? 'sem nome',
        columns: i.columns.join(', ') + (i.included.length > 0 ? `, inclui ${i.included.join(', ')}` : ''),
        extra: i.unique ? 'único' : '',
        convention: i.convention,
      })),
    ];
    const relations = this.relations();
    const out = relations
      .filter((r) => r.from === key)
      .map((r) => ({
        target: r.to,
        text: `Aponta para ${r.to} por ${r.fk.columns.join(', ')} (${CARDINALITY_TEXT[r.cardinality]}${r.optional ? ', opcional' : ''})`,
        onDelete: `ao apagar em ${r.to}: ${DELETE_ACTION_LABEL[r.fk.onDelete]}`,
      }));
    const incoming = relations
      .filter((r) => r.to === key && r.from !== key)
      .map((r) => ({ target: r.from, text: `${r.from}.${r.fk.columns.join(', ')} aponta para esta tabela (${CARDINALITY_BACK[r.cardinality]})`, onDelete: '' }));
    const link = linkTableOf(table, schema);
    const views = schema.views.filter((v) => v.reads.some((r) => findTable(schema, r) === table)).map((v) => tableKey(v));
    const procedures = schema.procedures
      .filter((p) => [...p.reads, ...p.writes].some((r) => findTable(schema, r) === table))
      .map((p) => ({ key: tableKey(p), writes: p.writes.some((w) => findTable(schema, w) === table) }));
    return {
      key,
      sources: table.sources,
      size: estimatedRowsText(table.estimatedRows),
      noKey: !table.primaryKey,
      columns,
      indexes,
      relations: [...out, ...incoming],
      link: link ? `Tabela de ligação: ${link[0]} N para N ${link[1]}` : null,
      views,
      procedures,
      divergences: schema.divergences
        .filter((d) => d.table.toLowerCase() === key.toLowerCase())
        .map((d) => ({
          title: `${DIVERGENCE_LABEL[d.kind]}${d.column ? ' de ' + d.column : ''}`,
          values: d.values.map((v) => `${SCHEMA_SOURCE_LABEL[v.source.kind]} diz ${v.value} (${v.source.file}:${v.source.line})`),
        })),
      findings: schema.findings.filter((f) => f.table.toLowerCase() === key.toLowerCase()),
    };
  });

  protected readonly view = computed(() => {
    const pick = this.selection();
    const view = pick.kind === DbItemKind.View ? this.schema().views.find((v) => tableKey(v) === pick.key) : undefined;
    return view ? { key: tableKey(view), view, reads: this.known(view.reads) } : null;
  });

  protected readonly procedure = computed(() => {
    const pick = this.selection();
    const procedure = pick.kind === DbItemKind.Procedure ? this.schema().procedures.find((p) => tableKey(p) === pick.key) : undefined;
    return procedure ? { key: tableKey(procedure), procedure, reads: this.known(procedure.reads), writes: this.known(procedure.writes) } : null;
  });

  // Tabelas citadas, com a chave da tabela do esquema (para o clique) quando ela existe.
  private known(names: readonly string[]): { name: string; key: string | null }[] {
    const schema = this.schema();
    return names.map((name) => {
      const table = findTable(schema, name);
      return { name, key: table ? tableKey(table) : null };
    });
  }

  protected pickTable(key: string): void {
    this.focusTitleNext = true;
    this.pick.emit({ kind: DbItemKind.Table, key });
  }

  protected pickItem(kind: DbItemKind, key: string): void {
    this.focusTitleNext = true;
    this.pick.emit({ kind, key });
  }
}
