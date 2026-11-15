import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, input, output, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HOME_FROM_DB_PARAM, HOME_FROM_DB_VALUE } from '../../core/database/query-analysis';
import { TranslatePipe } from '@ngx-translate/core';
import { LanguageService } from '../../core/i18n/i18n';
import { DB_OPERATION_SHORT_KEY, DB_OPERATION_SLUG, FINDING_KIND_LABEL_KEY, FINDING_SEVERITY_LABEL_KEY } from '../../core/models';
import {
  Cardinality,
  DELETE_ACTION_LABEL_KEY,
  DIVERGENCE_LABEL_KEY,
  DbRelation,
  DbSchema,
  divergentText,
  DbUsageReport,
  SCHEMA_FINDING_TITLE_KEY,
  KEY_MARK_LABEL,
  SCHEMA_SOURCE_LABEL_KEY,
  SchemaSourceKind,
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

const CARDINALITY_TEXT_KEY: Record<Cardinality, string> = {
  [Cardinality.ManyToOne]: 'dbPanel.cardinality.manyToOne',
  [Cardinality.OneToOne]: 'dbPanel.cardinality.oneToOne',
};

const CARDINALITY_BACK_KEY: Record<Cardinality, string> = {
  [Cardinality.ManyToOne]: 'dbPanel.cardinality.oneToMany',
  [Cardinality.OneToOne]: 'dbPanel.cardinality.oneToOne',
};

// Painel da tela do banco (Fase 38): campos, índices, relações, divergência entre fontes e achados
// sem IA da tabela; colunas e tabelas lidas da view; parâmetros e tabelas da procedure.
@Component({
  selector: 'app-db-panel',
  imports: [RouterLink, TranslatePipe],
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
  private readonly i18n = inject(LanguageService);
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
  protected readonly sourceLabelKey = SCHEMA_SOURCE_LABEL_KEY;
  protected readonly SchemaSourceKind = SchemaSourceKind;
  protected readonly findingTitleKey = SCHEMA_FINDING_TITLE_KEY;
  protected readonly sourceText = sourceText;
  protected readonly findingText = findingText;
  protected readonly opShortKey = DB_OPERATION_SHORT_KEY;
  protected readonly opSlug = DB_OPERATION_SLUG;
  protected readonly kindKey = FINDING_KIND_LABEL_KEY;
  protected readonly severityKey = FINDING_SEVERITY_LABEL_KEY;

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
        type: c.type ?? divergentText(),
        divergent: c.type == null || c.nullable == null,
        nullable: c.nullable == null ? divergentText() : this.i18n.t(c.nullable ? 'dbPanel.yes' : 'dbPanel.no'),
        nullableDivergent: c.nullable == null,
        mark: KEY_MARK_LABEL[keyMark(keys)],
        keyText: keyText(keys),
        identity: c.identity,
        personal: c.personal,
        default: c.default,
      };
    });
    const t = (key: string, params?: Record<string, unknown>) => this.i18n.t(key, params);
    const indexes = [
      ...(table.primaryKey ? [{ name: table.primaryKey.name ?? t('dbPanel.primaryKey'), columns: table.primaryKey.columns.join(', '), extra: t('dbPanel.primaryKey'), convention: false }] : []),
      ...table.indexes.map((i) => ({
        name: i.name ?? t('dbPanel.unnamed'),
        columns: i.columns.join(', ') + (i.included.length > 0 ? ', ' + t('dbPanel.includes', { columns: i.included.join(', ') }) : ''),
        extra: i.unique ? t('dbPanel.unique') : '',
        convention: i.convention,
      })),
    ];
    const relations = this.relations();
    const out = relations
      .filter((r) => r.from === key)
      .map((r) => ({
        target: r.to,
        text: t('dbPanel.pointsTo', { target: r.to, columns: r.fk.columns.join(', '), cardinality: t(CARDINALITY_TEXT_KEY[r.cardinality]), optional: r.optional ? 'yes' : 'no' }),
        onDelete: t('dbPanel.onDelete', { target: r.to, action: t(DELETE_ACTION_LABEL_KEY[r.fk.onDelete]) }),
      }));
    const incoming = relations
      .filter((r) => r.to === key && r.from !== key)
      .map((r) => ({ target: r.from, text: t('dbPanel.pointsHere', { from: r.from, columns: r.fk.columns.join(', '), cardinality: t(CARDINALITY_BACK_KEY[r.cardinality]) }), onDelete: '' }));
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
      link: link ? t('dbPanel.linkTable', { left: link[0], right: link[1] }) : null,
      views,
      procedures,
      divergences: schema.divergences
        .filter((d) => d.table.toLowerCase() === key.toLowerCase())
        .map((d) => ({
          title: d.column ? t('dbPanel.divergenceKindColumn', { kind: t(DIVERGENCE_LABEL_KEY[d.kind]), column: d.column }) : t(DIVERGENCE_LABEL_KEY[d.kind]),
          values: d.values.map((v) => {
            const source = t(SCHEMA_SOURCE_LABEL_KEY[v.source.kind]);
            return v.source.kind === SchemaSourceKind.LiveDatabase
              ? t('dbPanel.divergenceValue', { source, value: v.value })
              : t('dbPanel.divergenceValueAt', { source, value: v.value, where: `${v.source.file}:${v.source.line}` });
          }),
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
