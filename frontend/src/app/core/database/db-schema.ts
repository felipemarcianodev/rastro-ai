import { currentLocale, tr } from '../i18n/locale';
import { translatedRecord } from '../graph/translated';
import { DbOperation, FindingKind, FindingSeverity, SourceLocation } from '../models';

// Esquema do banco (Fases 37 e 38, ADR-006), como a API devolve em GET api/repositories/{nome}/schema.
// Tudo lido do código do repositório e sem IA: o mesmo código dá o mesmo esquema e o mesmo mapa.

export enum SqlDialect {
  SqlServer = 0,
  PostgreSql = 1,
}

export enum SchemaSourceKind {
  EfModel = 0,
  EfMigration = 1,
  SqlScript = 2,
  LiveDatabase = 3,
}

export enum DeleteAction {
  NoAction = 0,
  Cascade = 1,
  SetNull = 2,
  SetDefault = 3,
  Restrict = 4,
}

export enum SchemaDivergenceKind {
  ColumnType = 0,
  ColumnNullability = 1,
  ColumnMissing = 2,
  PrimaryKey = 3,
}

export enum SchemaFindingKind {
  ForeignKeyWithoutIndex = 0,
}

export interface SchemaSource {
  kind: SchemaSourceKind;
  file: string;
  line: number;
}

export interface DbColumn {
  name: string;
  // Nulos quando as fontes divergem: o Rastro não escolhe.
  type: string | null;
  nullable: boolean | null;
  default: string | null;
  identity: boolean;
  sources: SchemaSource[];
  personal: boolean;
}

export interface DbPrimaryKey {
  name: string | null;
  columns: string[];
  source: SchemaSource;
}

export interface DbForeignKey {
  name: string | null;
  columns: string[];
  principalTable: string;
  principalColumns: string[];
  onDelete: DeleteAction;
  source: SchemaSource;
}

export interface DbIndex {
  name: string | null;
  columns: string[];
  included: string[];
  unique: boolean;
  source: SchemaSource;
  convention: boolean;
  // Método de acesso do PostgreSQL ("gin", "gist"...), quando o código o escreve (Fase 43a).
  method?: string | null;
}

export interface DbTable {
  schema: string | null;
  name: string;
  columns: DbColumn[];
  primaryKey: DbPrimaryKey | null;
  foreignKeys: DbForeignKey[];
  indexes: DbIndex[];
  sources: SchemaSource[];
  // Estimativa de linhas pelo catálogo do banco vivo (Fase 44); nula sem banco vivo ou sem estatística.
  estimatedRows?: number | null;
}

// "Cerca de 12.345 linhas, pela estimativa do banco vivo" (milhar no formato do idioma ativo).
export function estimatedRowsText(rows: number | null | undefined): string | null {
  if (rows === null || rows === undefined || rows <= 0) return null;
  const formatted = new Intl.NumberFormat(currentLocale()).format(Math.round(rows));
  return tr('dbSchema.estimatedRows', { count: rows, rows: formatted });
}

export interface DbView {
  schema: string | null;
  name: string;
  columns: string[];
  reads: string[];
  source: SchemaSource;
}

export interface DbParameter {
  name: string;
  type: string;
  output: boolean;
}

export interface DbProcedure {
  schema: string | null;
  name: string;
  parameters: DbParameter[];
  reads: string[];
  writes: string[];
  source: SchemaSource;
}

export interface DivergentValue {
  value: string;
  source: SchemaSource;
}

export interface SchemaDivergence {
  kind: SchemaDivergenceKind;
  table: string;
  column: string;
  values: DivergentValue[];
}

export interface SchemaNote {
  message: string;
  file: string;
  line: number;
}

export interface SchemaFinding {
  kind: SchemaFindingKind;
  table: string;
  columns: string[];
  source: SchemaSource;
}

export interface DbSchema {
  dialect: SqlDialect;
  tables: DbTable[];
  views: DbView[];
  procedures: DbProcedure[];
  divergences: SchemaDivergence[];
  notes: SchemaNote[];
  findings: SchemaFinding[];
}

// Quem usa cada tabela (Fase 39), de GET api/repositories/{nome}/schema/usage: os passos dos
// levantamentos do repositório (o mais novo de cada método) e os achados sem IA desses fluxos.
export interface DbUse {
  target: string;
  operation: DbOperation;
  surveyId: string;
  entryPoint: string;
  nodeId: string;
  nodeLabel: string;
  location?: SourceLocation;
  surveyCreatedAt: string;
}

export interface DbFlowFinding {
  target: string;
  kind: FindingKind;
  severity: FindingSeverity;
  message: string;
  surveyId: string;
  entryPoint: string;
  nodeId: string;
  location?: SourceLocation;
}

export interface DbUsageReport {
  uses: DbUse[];
  findings: DbFlowFinding[];
}

export function sameName(a: string, b: string): boolean {
  return a.localeCompare(b, undefined, { sensitivity: 'accent' }) === 0;
}

export const SQL_DIALECT_LABEL: Record<SqlDialect, string> = {
  [SqlDialect.SqlServer]: 'SQL Server',
  [SqlDialect.PostgreSql]: 'PostgreSQL',
};

// Quem lê a consulta em cada dialeto, para a mensagem de erro de leitura (Fase 42).
const SQL_READER_LABEL_KEY: Record<SqlDialect, string> = {
  [SqlDialect.SqlServer]: 'dbSchema.reader.sqlServer',
  [SqlDialect.PostgreSql]: 'dbSchema.reader.postgreSql',
};
// Lida no idioma ativo (a constante é usada pela análise de consulta).
export const SQL_READER_LABEL: Record<SqlDialect, string> = translatedRecord(SQL_READER_LABEL_KEY);

export const SCHEMA_SOURCE_LABEL_KEY: Record<SchemaSourceKind, string> = {
  [SchemaSourceKind.EfModel]: 'dbSchema.source.efModel',
  [SchemaSourceKind.EfMigration]: 'dbSchema.source.efMigration',
  [SchemaSourceKind.SqlScript]: 'dbSchema.source.sqlScript',
  [SchemaSourceKind.LiveDatabase]: 'dbSchema.source.liveDatabase',
};

export const DELETE_ACTION_LABEL_KEY: Record<DeleteAction, string> = {
  [DeleteAction.NoAction]: 'dbSchema.deleteAction.noAction',
  [DeleteAction.Cascade]: 'dbSchema.deleteAction.cascade',
  [DeleteAction.SetNull]: 'dbSchema.deleteAction.setNull',
  [DeleteAction.SetDefault]: 'dbSchema.deleteAction.setDefault',
  [DeleteAction.Restrict]: 'dbSchema.deleteAction.restrict',
};

export const DIVERGENCE_LABEL_KEY: Record<SchemaDivergenceKind, string> = {
  [SchemaDivergenceKind.ColumnType]: 'dbSchema.divergence.columnType',
  [SchemaDivergenceKind.ColumnNullability]: 'dbSchema.divergence.columnNullability',
  [SchemaDivergenceKind.ColumnMissing]: 'dbSchema.divergence.columnMissing',
  [SchemaDivergenceKind.PrimaryKey]: 'dbSchema.divergence.primaryKey',
};

export const SCHEMA_FINDING_TITLE_KEY: Record<SchemaFindingKind, string> = {
  [SchemaFindingKind.ForeignKeyWithoutIndex]: 'dbSchema.finding.foreignKeyWithoutIndex.title',
};

// Selo curto no bloco do mapa.
export const SCHEMA_FINDING_BADGE_KEY: Record<SchemaFindingKind, string> = {
  [SchemaFindingKind.ForeignKeyWithoutIndex]: 'dbSchema.finding.foreignKeyWithoutIndex.badge',
};

export function findingText(finding: SchemaFinding): string {
  const columns = finding.columns.join(', ');
  switch (finding.kind) {
    case SchemaFindingKind.ForeignKeyWithoutIndex:
      return tr('dbSchema.finding.foreignKeyWithoutIndex.text', { source: sourceText(finding.source), columns });
  }
}

// "migration Loja.Api/Migrations/20261020_Pagamentos.cs:24"; banco vivo não tem arquivo.
export function sourceText(source: SchemaSource): string {
  const label = tr(SCHEMA_SOURCE_LABEL_KEY[source.kind]);
  return source.kind === SchemaSourceKind.LiveDatabase ? label : `${label} ${source.file}:${source.line}`;
}

// Nome como o resto do esquema escreve (DbNames.Display): sem o schema padrão do dialeto.
export function tableKey(item: { schema: string | null; name: string }): string {
  return item.schema ? `${item.schema}.${item.name}` : item.name;
}

function same(a: string, b: string): boolean {
  return a.localeCompare(b, undefined, { sensitivity: 'accent' }) === 0;
}

function sameSet(a: readonly string[], b: readonly string[]): boolean {
  const left = new Set(a.map((c) => c.toLowerCase()));
  return a.length > 0 && left.size === b.length && b.every((c) => left.has(c.toLowerCase()));
}

// A tabela pelo nome que a FK, a view e a procedure citam (com ou sem schema, sem diferenciar caixa).
export function findTable(schema: DbSchema, name: string): DbTable | undefined {
  const bare = name.includes('.') ? name.slice(name.lastIndexOf('.') + 1) : name;
  return schema.tables.find((t) => same(tableKey(t), name)) ?? schema.tables.find((t) => same(t.name, bare));
}

export enum Cardinality {
  ManyToOne = 0,
  OneToOne = 1,
}

// Uma ligação do mapa: a FK de "from" (quem tem a chave) para "to" (a tabela de destino).
export interface DbRelation {
  from: string;
  to: string;
  fk: DbForeignKey;
  cardinality: Cardinality;
  optional: boolean;
}

// Um para um quando as colunas da FK são a chave primária ou um índice único; senão, N para 1.
export function relationsOf(schema: DbSchema): DbRelation[] {
  return schema.tables.flatMap((table) =>
    table.foreignKeys.flatMap((fk) => {
      const target = findTable(schema, fk.principalTable);
      if (!target) return [];
      const unique = sameSet(table.primaryKey?.columns ?? [], fk.columns) || table.indexes.some((i) => i.unique && sameSet(i.columns, fk.columns));
      const optional = fk.columns.some((c) => table.columns.find((col) => same(col.name, c))?.nullable === true);
      return [{ from: tableKey(table), to: tableKey(target), fk, cardinality: unique ? Cardinality.OneToOne : Cardinality.ManyToOne, optional }];
    }),
  );
}

// Tabela de ligação (N para N): a chave primária é exatamente as colunas de duas FKs para tabelas diferentes.
export function linkTableOf(table: DbTable, schema: DbSchema): [string, string] | null {
  const pk = table.primaryKey?.columns ?? [];
  if (table.foreignKeys.length !== 2 || pk.length === 0) return null;
  const [a, b] = table.foreignKeys;
  if (!sameSet(pk, [...a.columns, ...b.columns])) return null;
  const left = findTable(schema, a.principalTable);
  const right = findTable(schema, b.principalTable);
  return left && right && left !== right ? [tableKey(left), tableKey(right)] : null;
}

// Recorte (Fase 38): a tabela e os vizinhos até "hops" saltos pelas FKs, nos dois sentidos.
export function neighborhood(relations: readonly DbRelation[], start: string, hops: number): Set<string> {
  const seen = new Set([start]);
  let frontier = [start];
  for (let step = 0; step < hops && frontier.length > 0; step++) {
    const next: string[] = [];
    for (const key of frontier) {
      for (const r of relations) {
        const other = r.from === key ? r.to : r.to === key ? r.from : null;
        if (other && !seen.has(other)) {
          seen.add(other);
          next.push(other);
        }
      }
    }
    frontier = next;
  }
  return seen;
}

// Busca por tabela, view ou procedure: pelo nome, com ou sem schema, e pelo nome de coluna.
export function matches(query: string, item: { schema: string | null; name: string }, columns: readonly string[] = []): boolean {
  const q = query.trim().toLowerCase();
  if (!q) return true;
  return tableKey(item).toLowerCase().includes(q) || columns.some((c) => c.toLowerCase().includes(q));
}

export interface ColumnKeys {
  pk: boolean;
  fk: boolean;
}

export function columnKeys(table: DbTable, column: string): ColumnKeys {
  return {
    pk: !!table.primaryKey?.columns.some((c) => same(c, column)),
    fk: table.foreignKeys.some((fk) => fk.columns.some((c) => same(c, column))),
  };
}

// Marca de chave de uma coluna. Valor explícito: o rótulo sai só de KEY_MARK_LABEL.
export enum KeyMark {
  None = 0,
  Pk = 1,
  Fk = 2,
  PkFk = 3,
}

export const KEY_MARK_LABEL: Record<KeyMark, string> = {
  [KeyMark.None]: '',
  [KeyMark.Pk]: 'PK',
  [KeyMark.Fk]: 'FK',
  [KeyMark.PkFk]: 'PK FK',
};

// Texto do tipo e do nulo quando as fontes divergem e o Rastro não escolhe.
export function divergentText(): string {
  return tr('dbSchema.divergent');
}

export function keyMark(keys: ColumnKeys): KeyMark {
  if (keys.pk && keys.fk) return KeyMark.PkFk;
  return keys.pk ? KeyMark.Pk : keys.fk ? KeyMark.Fk : KeyMark.None;
}

export function hasPrimaryKey(mark: KeyMark): boolean {
  return mark === KeyMark.Pk || mark === KeyMark.PkFk;
}

export function keyText(keys: ColumnKeys): string {
  return keys.pk && keys.fk ? tr('dbSchema.keyText.pkFk') : keys.pk ? tr('dbSchema.keyText.pk') : keys.fk ? tr('dbSchema.keyText.fk') : '';
}

// Mermaid erDiagram (Fase 38) do que está no mapa. O tipo vira uma palavra (o Mermaid não aceita
// vírgula nem espaço nele) e o nome com ponto vira sublinhado.
export function toErMermaid(schema: DbSchema, keys: ReadonlySet<string>): string {
  const lines = ['erDiagram'];
  const id = (key: string) => key.replace(/[^A-Za-z0-9_]/g, '_');
  for (const table of schema.tables.filter((t) => keys.has(tableKey(t)))) {
    lines.push(`  ${id(tableKey(table))} {`);
    for (const column of table.columns) {
      const mark = KEY_MARK_LABEL[keyMark(columnKeys(table, column.name))].replace(' ', ', ');
      const type = (column.type ?? divergentText()).replace(/,/g, '_').replace(/[^A-Za-z0-9_()[\]-]/g, '_');
      const comment = column.personal ? ` "${tr('dbSchema.personalData')}"` : '';
      lines.push(`    ${type} ${id(column.name)}${mark ? ' ' + mark : ''}${comment}`);
    }
    lines.push('  }');
  }
  for (const r of relationsOf(schema).filter((r) => keys.has(r.from) && keys.has(r.to))) {
    const left = r.optional ? '|o' : '||';
    const right = r.cardinality === Cardinality.OneToOne ? 'o|' : 'o{';
    lines.push(`  ${id(r.to)} ${left}--${right} ${id(r.from)} : "${r.fk.columns.join(', ')}"`);
  }
  return lines.join('\n') + '\n';
}
