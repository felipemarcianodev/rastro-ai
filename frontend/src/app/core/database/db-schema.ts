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
export const SQL_READER_LABEL: Record<SqlDialect, string> = {
  [SqlDialect.SqlServer]: 'O leitor de T-SQL',
  [SqlDialect.PostgreSql]: 'O leitor de SQL do PostgreSQL',
};

export const SCHEMA_SOURCE_LABEL: Record<SchemaSourceKind, string> = {
  [SchemaSourceKind.EfModel]: 'modelo do EF Core',
  [SchemaSourceKind.EfMigration]: 'migration',
  [SchemaSourceKind.SqlScript]: 'arquivo .sql',
  [SchemaSourceKind.LiveDatabase]: 'banco vivo',
};

export const DELETE_ACTION_LABEL: Record<DeleteAction, string> = {
  [DeleteAction.NoAction]: 'sem ação',
  [DeleteAction.Cascade]: 'apaga junto (cascata)',
  [DeleteAction.SetNull]: 'anula a chave',
  [DeleteAction.SetDefault]: 'volta ao padrão',
  [DeleteAction.Restrict]: 'impede apagar',
};

export const DIVERGENCE_LABEL: Record<SchemaDivergenceKind, string> = {
  [SchemaDivergenceKind.ColumnType]: 'tipo',
  [SchemaDivergenceKind.ColumnNullability]: 'nulo',
  [SchemaDivergenceKind.ColumnMissing]: 'existência da coluna',
  [SchemaDivergenceKind.PrimaryKey]: 'chave primária',
};

export const SCHEMA_FINDING_TITLE: Record<SchemaFindingKind, string> = {
  [SchemaFindingKind.ForeignKeyWithoutIndex]: 'Chave estrangeira sem índice',
};

// Selo curto no bloco do mapa.
export const SCHEMA_FINDING_BADGE: Record<SchemaFindingKind, string> = {
  [SchemaFindingKind.ForeignKeyWithoutIndex]: 'FK sem índice',
};

export function findingText(finding: SchemaFinding): string {
  const columns = finding.columns.join(', ');
  switch (finding.kind) {
    case SchemaFindingKind.ForeignKeyWithoutIndex:
      return `${sourceText(finding.source)} cria a FK ${columns} sem índice. Toda busca por ${columns} (e todo DELETE na tabela de destino) varre esta tabela.`;
  }
}

// "migration Loja.Api/Migrations/20261020_Pagamentos.cs:24"; banco vivo não tem arquivo.
export function sourceText(source: SchemaSource): string {
  return source.kind === SchemaSourceKind.LiveDatabase ? SCHEMA_SOURCE_LABEL[source.kind] : `${SCHEMA_SOURCE_LABEL[source.kind]} ${source.file}:${source.line}`;
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

// "PK", "FK", "PK FK" ou vazio.
export function keyMark(keys: ColumnKeys): string {
  return [keys.pk ? 'PK' : '', keys.fk ? 'FK' : ''].filter(Boolean).join(' ');
}

export function keyText(keys: ColumnKeys): string {
  return keys.pk && keys.fk ? 'primária e estrangeira' : keys.pk ? 'primária' : keys.fk ? 'estrangeira' : '';
}

// Mermaid erDiagram (Fase 38) do que está no mapa. O tipo vira uma palavra (o Mermaid não aceita
// vírgula nem espaço nele) e o nome com ponto vira sublinhado.
export function toErMermaid(schema: DbSchema, keys: ReadonlySet<string>): string {
  const lines = ['erDiagram'];
  const id = (key: string) => key.replace(/[^A-Za-z0-9_]/g, '_');
  for (const table of schema.tables.filter((t) => keys.has(tableKey(t)))) {
    lines.push(`  ${id(tableKey(table))} {`);
    for (const column of table.columns) {
      const mark = keyMark(columnKeys(table, column.name)).replace(' ', ', ');
      const type = (column.type ?? 'divergente').replace(/,/g, '_').replace(/[^A-Za-z0-9_()[\]-]/g, '_');
      const comment = column.personal ? ' "dado pessoal"' : '';
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
