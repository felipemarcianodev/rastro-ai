using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Rastro.Core.Localization;
using Txt = Rastro.Extractor.Resources.Database;

namespace Rastro.Extractor.Database;

// Allowed: a consulta pode ir ao banco vivo para o plano estimado. Reason: por que não, no idioma pedido.
// Variables: as variáveis citadas (@p0, @cliente), que o pedido do plano declara antes.
public record PlanGuardResult(bool Allowed, string? Reason, IReadOnlyList<string> Variables);

// Porta da consulta para o banco vivo (Fase 40b, ADR-006): antes de pedir o plano estimado, o leitor de
// T-SQL confere que o texto é uma instrução só, SELECT, INSERT, UPDATE, DELETE ou MERGE, sem SET, EXEC,
// DDL, GO, SELECT INTO, OUTPUT INTO nem acesso a outro servidor (OPENROWSET, OPENQUERY,
// OPENDATASOURCE). Assim um "SET SHOWPLAN_XML OFF" escondido na consulta nunca chega ao banco.
public static partial class TSqlPlanGuard
{
    public static PlanGuardResult Check(string sql, UiLanguage language)
    {
        string T(string key, params object[] args) => Txt.Get(key, language, args);
        var tree = TSqlParsing.Parse(sql, out var errors);

        if (errors.Count > 0 || tree is not TSqlScript script)
        {
            var first = errors.FirstOrDefault();
            return Deny(T("Guard.TSqlNotUnderstood", first?.Line ?? 1, first is null ? T("Guard.EmptyText") : SqlMessageTranslator.Translate(first.Message, language)));
        }

        var statements = script.Batches.SelectMany(b => b.Statements).ToList();
        if (script.Batches.Count != 1 || statements.Count != 1)
            return Deny(T("Guard.TSqlSingleStatement"));

        var statement = statements[0];
        if (statement is not (SelectStatement or InsertStatement or UpdateStatement or DeleteStatement or MergeStatement))
            return Deny(T("Guard.TSqlOnlyDml"));

        var visitor = new Forbidden(language);
        statement.Accept(visitor);
        if (visitor.Reason is { } reason) return Deny(reason);

        var invalid = visitor.Variables.FirstOrDefault(v => !VariableName().IsMatch(v));
        if (invalid is not null) return Deny(T("Guard.TSqlVariable", invalid));
        return new PlanGuardResult(true, null, visitor.Variables.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static PlanGuardResult Deny(string reason) => new(false, reason, []);

    private sealed class Forbidden(UiLanguage language) : TSqlFragmentVisitor
    {
        private string T(string key) => Txt.Get(key, language);

        public string? Reason { get; private set; }
        public List<string> Variables { get; } = [];

        private void Set(string reason) => Reason ??= reason;

        public override void Visit(SelectStatement node)
        {
            if (node.Into is not null) Set(T("Guard.SelectInto"));
        }

        public override void Visit(OutputIntoClause node) => Set(T("Guard.OutputInto"));

        public override void Visit(ExecuteSpecification node) => Set(T("Guard.Exec"));

        public override void Visit(OpenRowsetTableReference node) => Set(T("Guard.OpenRowset"));

        public override void Visit(OpenQueryTableReference node) => Set(T("Guard.OpenQuery"));

        public override void Visit(AdHocTableReference node) => Set(T("Guard.OpenDataSource"));

        public override void Visit(BulkOpenRowset node) => Set(T("Guard.OpenRowsetBulk"));

        public override void Visit(VariableReference node) => Variables.Add(node.Name);
    }

    [GeneratedRegex(@"^@[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial Regex VariableName();
}
