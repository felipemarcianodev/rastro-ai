using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Rastro.Extractor.Database;

// Allowed: a consulta pode ir ao banco vivo para o plano estimado. Reason: por que não, em pt-BR.
// Variables: as variáveis citadas (@p0, @cliente), que o pedido do plano declara antes.
public record PlanGuardResult(bool Allowed, string? Reason, IReadOnlyList<string> Variables);

// Porta da consulta para o banco vivo (Fase 40b, ADR-006): antes de pedir o plano estimado, o leitor de
// T-SQL confere que o texto é uma instrução só, SELECT, INSERT, UPDATE, DELETE ou MERGE, sem SET, EXEC,
// DDL, GO, SELECT INTO, OUTPUT INTO nem acesso a outro servidor (OPENROWSET, OPENQUERY,
// OPENDATASOURCE). Assim um "SET SHOWPLAN_XML OFF" escondido na consulta nunca chega ao banco.
public static partial class TSqlPlanGuard
{
    public static PlanGuardResult Check(string sql)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        TSqlFragment? tree;
        IList<ParseError> errors;
        using (var reader = new StringReader(sql))
            tree = parser.Parse(reader, out errors);

        if (errors.Count > 0 || tree is not TSqlScript script)
        {
            var first = errors.FirstOrDefault();
            return Deny($"O leitor de T-SQL não entendeu a consulta (linha {first?.Line ?? 1}): {(first is null ? "texto vazio" : SqlMessageTranslator.Translate(first.Message))}.");
        }

        var statements = script.Batches.SelectMany(b => b.Statements).ToList();
        if (script.Batches.Count != 1 || statements.Count != 1)
            return Deny("O plano estimado sai para uma instrução só (sem GO e sem ponto e vírgula separando outras).");

        var statement = statements[0];
        if (statement is not (SelectStatement or InsertStatement or UpdateStatement or DeleteStatement or MergeStatement))
            return Deny("O plano estimado só sai para SELECT, INSERT, UPDATE, DELETE ou MERGE: SET, EXEC, DECLARE e DDL não vão ao banco.");

        var visitor = new Forbidden();
        statement.Accept(visitor);
        if (visitor.Reason is { } reason) return Deny(reason);

        var invalid = visitor.Variables.FirstOrDefault(v => !VariableName().IsMatch(v));
        if (invalid is not null) return Deny($"Nome de variável que o Rastro não declara: {invalid}.");
        return new PlanGuardResult(true, null, visitor.Variables.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static PlanGuardResult Deny(string reason) => new(false, reason, []);

    private sealed class Forbidden : TSqlFragmentVisitor
    {
        public string? Reason { get; private set; }
        public List<string> Variables { get; } = [];

        private void Set(string reason) => Reason ??= reason;

        public override void Visit(SelectStatement node)
        {
            if (node.Into is not null) Set("SELECT INTO cria uma tabela: não vai ao banco vivo.");
        }

        public override void Visit(OutputIntoClause node) => Set("OUTPUT INTO grava em outra tabela: não vai ao banco vivo.");

        public override void Visit(ExecuteSpecification node) => Set("EXEC não vai ao banco vivo.");

        public override void Visit(OpenRowsetTableReference node) => Set("OPENROWSET acessa outro servidor: não vai ao banco vivo.");

        public override void Visit(OpenQueryTableReference node) => Set("OPENQUERY acessa outro servidor: não vai ao banco vivo.");

        public override void Visit(AdHocTableReference node) => Set("OPENDATASOURCE acessa outro servidor: não vai ao banco vivo.");

        public override void Visit(BulkOpenRowset node) => Set("OPENROWSET(BULK) lê arquivo: não vai ao banco vivo.");

        public override void Visit(VariableReference node) => Variables.Add(node.Name);
    }

    [GeneratedRegex(@"^@[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial Regex VariableName();
}
