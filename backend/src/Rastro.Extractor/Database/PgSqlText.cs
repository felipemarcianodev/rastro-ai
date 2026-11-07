using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using SqlParser;
using SqlParser.Ast;
using SqlParser.Dialects;
using SqlParser.Tokens;

namespace Rastro.Extractor.Database;

// Um token do texto com a posição: Start e Length em caracteres (Start a partir de 0) e Line (1 em diante).
internal sealed record PgToken(Token Token, int Start, int Length, int Line)
{
    public string? Word => Token is Word w ? w.Value : null;

    public bool Is(string keyword) => Token is Word { QuoteStyle: null } w && w.Value.Equals(keyword, StringComparison.OrdinalIgnoreCase);
}

// O texto de uma consulta ou de um arquivo .sql do PostgreSQL, lido pelo SqlParserCS (MIT, código
// gerenciado, sem biblioteca nativa; ADR-006). O leitor dá a árvore, mas não a posição de cada nó: a
// posição sai dos tokens, que o mesmo leitor dá com linha e coluna. Para marcar um trecho, o nó é
// escrito de volta (ToSql) e os tokens dele são procurados nos tokens do texto, sem olhar espaço,
// comentário, caixa nem o AS opcional. Nada aqui executa a consulta.
internal sealed class PgSqlText
{
    private static readonly PostgreSqlDialect Dialect = new FixedPostgreSqlDialect();

    public string Sql { get; }

    // Os tokens com significado (sem espaço nem comentário), na ordem do texto.
    public IReadOnlyList<PgToken> Tokens { get; }

    // O intervalo de tokens de cada instrução (separadas pelo ponto e vírgula de fora).
    public IReadOnlyList<(int From, int To)> Statements { get; }

    private PgSqlText(string sql, IReadOnlyList<PgToken> tokens)
    {
        Sql = sql;
        Tokens = tokens;
        var ranges = new List<(int, int)>();
        var start = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Token is not SemiColon) continue;
            if (i > start) ranges.Add((start, i));
            start = i + 1;
        }
        if (start < tokens.Count) ranges.Add((start, tokens.Count));
        Statements = ranges;
    }

    // Nulo quando o texto nem vira tokens (aspas sem fechar); error diz onde.
    public static PgSqlText? Read(string sql, out PgParseError? error)
    {
        error = null;
        IList<Token> raw;
        try
        {
            raw = new Tokenizer().Tokenize(sql, Dialect);
        }
        catch (TokenizeException ex)
        {
            error = new PgParseError(Clean(ex.Message), (int)Math.Max(ex.Line, 1), (int)Math.Max(ex.Column, 1));
            return null;
        }

        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < sql.Length; i++)
            if (sql[i] == '\n') lineStarts.Add(i + 1);

        int Offset(Token t)
        {
            var line = (int)Math.Clamp(t.Location.Line, 1, lineStarts.Count);
            return Math.Clamp(lineStarts[line - 1] + (int)Math.Max(t.Location.Column, 1) - 1, 0, sql.Length);
        }

        var all = raw.Where(t => t is not EOF).ToList();
        var tokens = new List<PgToken>();
        for (var i = 0; i < all.Count; i++)
        {
            if (all[i] is Whitespace) continue;
            var start = Offset(all[i]);
            var end = i + 1 < all.Count ? Offset(all[i + 1]) : sql.Length;
            tokens.Add(new PgToken(all[i], start, Math.Max(end - start, 0), (int)Math.Max(all[i].Location.Line, 1)));
        }
        return new PgSqlText(sql, tokens);
    }

    // A carga do pg_dump (COPY ... FROM stdin; as linhas de dados; \.) não é SQL: as linhas de dados viram
    // espaço, com o mesmo tamanho, para as posições e as linhas do resto do arquivo não mudarem.
    public static string WithoutCopyData(string sql)
    {
        if (!sql.Contains("stdin", StringComparison.OrdinalIgnoreCase)) return sql;
        var chars = sql.ToCharArray();
        var inside = false;
        var start = 0;
        for (var i = 0; i <= chars.Length; i++)
        {
            if (i < chars.Length && chars[i] != '\n') continue;
            var line = new string(chars, start, i - start).TrimEnd('\r');
            if (inside)
            {
                var end = line.Trim() == "\\.";
                for (var j = start; j < i; j++)
                    if (chars[j] != '\r') chars[j] = ' ';
                if (end) inside = false;
            }
            else if (line.TrimStart().StartsWith("COPY ", StringComparison.OrdinalIgnoreCase)
                && line.TrimEnd().EndsWith("FROM stdin;", StringComparison.OrdinalIgnoreCase))
                inside = true;
            start = i + 1;
        }
        return new string(chars);
    }

    // As instruções da árvore; nulo quando o leitor não entendeu (error diz onde).
    public static Sequence<Statement>? Parse(string sql, out PgParseError? error)
    {
        error = null;
        try
        {
            return new Parser().ParseSql(sql, Dialect);
        }
        catch (ParserException ex)
        {
            error = new PgParseError(Clean(ex.Message), (int)Math.Max(ex.Line, 1), (int)Math.Max(ex.Column, 1));
        }
        catch (TokenizeException ex)
        {
            error = new PgParseError(Clean(ex.Message), (int)Math.Max(ex.Line, 1), (int)Math.Max(ex.Column, 1));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException or NullReferenceException)
        {
            // Defeito do leitor num texto que ele não previu: vale como texto não entendido, sem derrubar a análise.
            error = new PgParseError("o leitor de SQL do PostgreSQL não conseguiu ler o texto", 1, 1);
        }
        return null;
    }

    // "Expected end of statement, found x, Line: 1, Col: 5": a posição vai à parte.
    private static string Clean(string message)
    {
        var at = message.IndexOf(", Line:", StringComparison.Ordinal);
        return SqlMessageTranslator.Translate((at > 0 ? message[..at] : message).Trim());
    }

    // O trecho de um nó da árvore dentro dos tokens [from, to). avoid: começos já usados por outro achado
    // do mesmo tipo (o mesmo trecho escrito duas vezes marca as duas). Sem achar o nó inteiro, fica o
    // maior começo dele que aparece; sem nada, o começo da instrução.
    public (int Start, int Length, int Line) Locate(string written, int from, int to, ISet<int>? avoid = null)
    {
        var text = Read(written, out _);
        var pattern = text?.Tokens.Select(t => t.Token).ToList() ?? [];
        from = Math.Clamp(from, 0, Tokens.Count);
        to = Math.Clamp(to, from, Tokens.Count);
        if (pattern.Count == 0 || from == to) return Whole(from, to);

        (int First, int Last)? full = null, partial = null;
        var partialLength = 0;
        for (var i = from; i < to; i++)
        {
            var (matched, last) = Match(pattern, i, to);
            if (matched == pattern.Count)
            {
                if (avoid is null || !avoid.Contains(Tokens[i].Start)) return Span(i, last);
                full ??= (i, last);
            }
            else if (matched > partialLength)
            {
                partial = (i, last);
                partialLength = matched;
            }
        }
        return full is { } f ? Span(f.First, f.Last) : partial is { } p ? Span(p.First, p.Last) : Whole(from, to);
    }

    // Quantos tokens do padrão casam a partir de i, e o último token do texto que casou.
    private (int Matched, int Last) Match(List<Token> pattern, int i, int to)
    {
        int p = 0, t = i, last = i;
        while (p < pattern.Count && t < to)
        {
            if (Same(pattern[p], Tokens[t].Token)) { last = t; p++; t++; continue; }
            // O AS do apelido é opcional: o texto pode ter e a escrita não, ou o contrário.
            if (IsAs(Tokens[t].Token)) { t++; continue; }
            if (IsAs(pattern[p])) { p++; continue; }
            break;
        }
        return (p, last);
    }

    private static bool IsAs(Token token) => token is Word { QuoteStyle: null } w && w.Value.Equals("AS", StringComparison.OrdinalIgnoreCase);

    private static bool Same(Token a, Token b)
    {
        if (a is Word wa && b is Word wb) return wa.Value.Equals(wb.Value, StringComparison.OrdinalIgnoreCase);
        return a.GetType() == b.GetType() && string.Equals(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public (int Start, int Length, int Line) Span(int first, int last)
    {
        var a = Tokens[first];
        var b = Tokens[Math.Max(last, first)];
        return (a.Start, Math.Max(b.Start + b.Length - a.Start, 0), a.Line);
    }

    private (int Start, int Length, int Line) Whole(int from, int to) =>
        from < to ? Span(from, to - 1) : (0, 0, 1);

    // O índice do token que começa no caractere start (a partir de from); from quando nenhum começa ali.
    public int TokenAt(int start, int from)
    {
        for (var i = Math.Max(from, 0); i < Tokens.Count; i++)
            if (Tokens[i].Start >= start) return i;
        return Math.Max(from, 0);
    }

    // O primeiro token com a palavra-chave dentro de [from, to), a partir de start.
    public int Find(string keyword, int from, int to)
    {
        for (var i = Math.Max(from, 0); i < Math.Min(to, Tokens.Count); i++)
            if (Tokens[i].Is(keyword)) return i;
        return -1;
    }
}

public record PgParseError(string Message, int Line, int Column);

// O dialeto do PostgreSQL do SqlParserCS 0.6.5 lê "a LIKE '%x' AND b = 1" como "a LIKE ('%x' AND b = 1)":
// o padrão do LIKE e do ILIKE engole o AND que vem depois. Aqui o AND e o OR têm a precedência deles,
// como no dialeto genérico do mesmo leitor; o resto fica como o leitor faz (teste EXT-PGQ-018).
internal sealed class FixedPostgreSqlDialect : PostgreSqlDialect
{
    public override short? GetNextPrecedence(Parser parser)
    {
        if (parser.PeekToken() is Word { QuoteStyle: null } word)
        {
            if (word.Value.Equals("AND", StringComparison.OrdinalIgnoreCase)) return GetPrecedence(Precedence.And);
            if (word.Value.Equals("OR", StringComparison.OrdinalIgnoreCase)) return GetPrecedence(Precedence.Or);
        }
        return base.GetNextPrecedence(parser);
    }
}

// Percorre a árvore do SqlParserCS por reflexão: os filhos de um nó são as propriedades que também são
// nós (ou listas deles), na ordem em que o leitor as declara. Assim cada regra decide onde parar (a
// subconsulta tem as próprias tabelas) sem depender de um visitante que entra em tudo.
internal static class PgAst
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new();

    public static IEnumerable<object> Children(object node)
    {
        foreach (var property in Properties.GetOrAdd(node.GetType(), Nodes))
        {
            object? value;
            try
            {
                value = property.GetValue(node);
            }
            catch (TargetInvocationException)
            {
                continue;
            }
            switch (value)
            {
                case null or string:
                    continue;
                case IEnumerable list when !IsNode(value.GetType()):
                    foreach (var item in list)
                        if (item is not null && IsNode(item.GetType())) yield return item;
                    break;
                default:
                    if (IsNode(value.GetType())) yield return value;
                    break;
            }
        }
    }

    // Os nós abaixo de node (sem ele), em pré-ordem. stop: o nó é devolvido, mas não se entra nele.
    public static IEnumerable<object> Descendants(object node, Func<object, bool>? stop = null)
    {
        var stack = new Stack<IEnumerator<object>>();
        stack.Push(Children(node).GetEnumerator());
        while (stack.Count > 0)
        {
            var top = stack.Peek();
            if (!top.MoveNext())
            {
                stack.Pop();
                continue;
            }
            var current = top.Current;
            yield return current;
            if (stop is null || !stop(current)) stack.Push(Children(current).GetEnumerator());
        }
    }

    private static PropertyInfo[] Nodes(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && p.Name != "EqualityContract")
            .Where(p => IsNode(p.PropertyType) || IsList(p.PropertyType) || p.PropertyType == typeof(object))
            .ToArray();

    private static bool IsNode(Type type) =>
        type.Namespace is "SqlParser.Ast" && !type.IsEnum && !type.IsPrimitive;

    private static bool IsList(Type type) =>
        type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

    // Nome sem aspas da última parte ("public"."pedidos" vira pedidos).
    public static string Last(ObjectName name) => name.Values[^1].Value;

    public static string? SchemaOf(ObjectName name) => name.Values.Count >= 2 ? name.Values[^2].Value : null;
}
