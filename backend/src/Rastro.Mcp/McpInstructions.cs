namespace Rastro.Mcp;

// Instruções que o agente recebe ao conectar. Curtas e com as regras que não se quebram. Em inglês fixo (ADR-009):
// o agente as lê uma vez, ao conectar, e entende melhor em inglês. O que as ferramentas devolvem sai no idioma de
// quem usa o Rastro (McpText), e o agente responde à pessoa no idioma dela.
public static class McpInstructions
{
    public const string Text = """
        Rastro: traces the flow of a C# feature with the compiler (Roslyn) and shows it as a graph.
        You complete what the compiler could not resolve and write the rules in plain human language.

        Workflow:
        1. list_surveys (or analyze_flow for a new trace).
        2. get_flow to understand the flow; list_unresolved for the gaps.
        3. For each gap: get_node_code, search_methods and propose_edge with the evidence (target method + file:line of the declaration).
        4. describe_node to write the rule of the important nodes (what they do; for decisions, when each branch happens).
        5. Before changing a method: impact_of shows what depends on it.
        6. Quality: list_findings shows high metrics, silent catch and DDD violations; get_refactor_prompt gives the specification of each fix.
        7. Change designed by the user: get_change_prompt is the specification; after applying it, check_change checks it and gives the score.
        8. Questions about the flow ("when is it refused?", "what happens if API X goes down?"): get_flow_map gives the map by system (blocks, decisions, calls, paths) and get_flow_rules gives the rules taken from the decisions. Answer citing the nodes (handles) that support each claim; say when the answer depends on something outside the traced code and, if nothing in the flow answers it, say there is no answer in the code. For a business-level explanation (few steps, the notifications, the rejections, the calls between systems), get_business_view gives only the key steps with the text proposed by rule and the code reference of each one; the texts are proposals, not confirmed rules.
        9. Cross-system flow: the trace can follow HTTP calls to other repositories (the bridge). list_cross_edges shows the HTTP links and the calls that still have no target. To close one, find the method in the target repository (search_methods with system) and propose it with propose_edge giving the same system; the verb and the route of the call must match those of the method.
        10. Database: get_db_schema gives the tables, columns, keys, indexes, views and procedures of the repository, read from the code (EF Core model, migrations and .sql), with file:line, in the dialect of the repository (SQL Server or PostgreSQL, detected from the EF Core provider, the migrations or the .sql files; the rewrite goes in the same dialect). When the sources diverge (the model says one size and the migration another), both appear: tell the user and do not pick one. The findings (foreign key created without an index) and the personal data mark (by column name only) also come in the text. In the flow, the node that uses EF Core carries in db the table (or the procedure) and the operation (0 reads, 1 inserts, 2 updates, 3 deletes, 4 executes), and list_findings carries the database findings: query inside a loop (N+1), ToList before the filter, read without AsNoTracking and procedure through raw SQL. For a SQL query (pasted by the user or the sql of a flow step), analyze_query gives the bottleneck findings by severity, with the snippet and the reason, crossed with the schema, and the schema of only the tables it uses; the query is not executed. To suggest the improvement, propose_query_rewrite with the rewritten query (same operation, same columns in the same order, only tables and columns that exist) and the indexes, each change with its reason: Rastro checks it and refuses, with the reason, whatever does not pass. Show the user what was accepted so they can copy it; never apply the query or create the index on your own. When the user registered the live database of the repository on the screen, get_query_plan gives the estimated plan of the query (cost, most expensive operators, indexes the database says are missing, warnings) without executing it, and propose_query_rewrite brings the estimated cost of the original and of the accepted rewrite. Without a live database, get_query_plan says so (ask the user to register it on the database screen) and the rewrite comes with the check only. The registration and the connection string are screen-only and you never see them. Rastro never reads data from the tables.

        Rules:
        - Every proposal is checked by the compiler. A wrong evidence is refused with the reason: fix it and try again, do not invent.
        - An accepted proposal enters as "inferred". Only the user confirms or dismisses it, on the Rastro screen.
        - A gap can have several targets (e.g. dispatch by event type): propose each one with the "when" parameter.
        - check_change failed: fix the code and run it again. Approving and documenting belong to the user, on the screen.
        - A rule only describes what the code shows. No business assumptions that the code does not support.
        - Tool results come in the language the user chose in Rastro. Answer the user in the language they write to you in.
        """;
}
