# Rastro AI

**English** · [Português](https://github.com/felipemarcianodev/rastro-ai/blob/main/docs/pacote/README.pt-BR.md) · [Español](https://github.com/felipemarcianodev/rastro-ai/blob/main/docs/pacote/README.es.md)

Follows the trail of a feature through your .NET code, from the entry point (controller, job, handler) to the end (or from a click on an Angular screen all the way to the controller), with every branch and the real implementation behind each DI interface. It shows the flow as an editable graph and generates the exact prompt for an AI to change that spot.

Rastro runs only on your machine: your code never leaves it.

## Install

Requires the .NET 10 SDK. Node (20 or newer) is optional: it is only needed to trace Angular flows.

```
dotnet tool install -g rastro-ai
```

## Use

```
rastro
```

Rastro opens in its own window, like an app, and the UI is also available at http://127.0.0.1:7450 for the browser (this machine only; change the port with `--porta`). Closing the window shuts Rastro down; `rastro --sem-janela` (or `--no-window`) starts only the console and the address. In the window, the repository folder field gets a "Browse…" button that opens the system's "Choose folder" dialog.

The window uses the system WebView: on Windows, the WebView2 Runtime (already included in Windows 11 and current versions of Windows 10); on Linux, `libwebkit2gtk-4.1` (`sudo apt install libwebkit2gtk-4.1-0`) and a session with a display; on macOS, nothing to install. Without it, Rastro warns you in the console and keeps running in the browser. The window comes from Photino.NET (Apache-2.0).

If Rastro is already open on that port, the command just prints the address and exits. Register your repositories in Settings (in the screen header) or from the terminal:

```
rastro repo add minha-api C:\codigo\minha-api
```

For the build and the tests to take part in validating a change, edit the commands in Settings ("Edit build and tests") or provide them when registering from the terminal:

```
rastro repo add minha-api C:\codigo\minha-api --build "dotnet build MinhaApi.slnx" --testes "dotnet test MinhaApi.slnx"
```

Command and option names (`rastro repo add`, `--build`, `--testes`, `--sem-janela`, `--porta`) are the same in every language. The English aliases `--tests`, `--no-window` and `--port` also work, alongside the Portuguese names. The help (`rastro --help`) and the terminal messages come out in the language set in `config.json`.

## Cross-system flow

When one API calls another over HTTP, the flow continues on the other side. When you create a trace, choose **Follow HTTP calls to** and select the other repositories (they must be registered in Rastro). Rastro matches the verb and route of each call against the routes of those repositories and follows the trail into the method that handles it, with each system's branches in the same drawing.

What cannot be matched becomes an "unresolved" step, with the reason: a route built at run time, no destination, or more than one destination. The AI (with an API key in Settings) or your agent fills that step, and you confirm it on screen.

The call can go out through `HttpClient` or through a Refit interface (`[Get]`, `[Post]` and the other verbs); in both cases Rastro matches the verb and route against the destination controller.

Limits: editing and validating a change only work for the first system of the trace, and Rastro keeps one solution open at a time.

## Angular flow (optional)

A trace can start at the click of an Angular screen, go through the service and `HttpClient`, and reach the .NET controller, with the screen's branches (`@if`, `*ngIf`, `[disabled]`) at the start of the flow.

Requirements: Node on the PATH (without it, the UI does not offer Angular) and `npm install` already done in the Angular repository (in a monorepo, in the app's subfolder), because Rastro uses the project's own TypeScript compiler, searched from the `tsconfig` folder up to the repository root.

When creating the trace, choose the app's `tsconfig` in place of the solution (the UI suggests the `tsconfig.app.json` of folders with `angular.json`) and give the entry point as `Component.method`, for example `DetalhePedidoComponent.cancelar`. To cross over to the APIs, select the .NET repositories in **Follow HTTP calls to**; without that list, the trace shows only the Angular part.

If the method opens a modal with `MatDialog.open(Component)` or with the CDK `Dialog`, the flow gets an "opens the modal Component" branch with the methods the modal's template binds to events (`(click)`, `(ngSubmit)`), and continues from there to `HttpClient`. That link comes from reading the modal's HTML (a user action), so the edges appear as inferred. `afterClosed()` continues after the branch. A custom service that forwards to `MatDialog` is not recognized.

What cannot be read (a URL built outside the code, a class chosen only at run time, no destination or more than one destination, depth exceeded) becomes an "unresolved" step, with the reason. The template reader is lightweight: it only sees the conditions written in the component's own HTML, without the Angular compiler. Editing and validating a change only work for .NET flows.

## Consolidated view

The flow also has a **map**, at the level of a C2 diagram: one lane per system, a few blocks in each, the decisions that change the outcome, the calls between systems, and the end-to-end paths. Clicking a block shows only that stretch. Decisions become **rules** in plain sentences, with the source in the code, and Rastro answers ready-made **questions** about the flow; with provider mode on, it also answers free-form questions, which use the "Suggestions" use and send the provider the question, the map, the rules, and up to 8 code excerpts.

Each trace has a summary view in a few steps, in plain sentences, as a Mermaid diagram that you edit next to the preview. Click a step to choose its color. The edited version is saved with the trace. In the flow, the view is also one of the drawings of the button that switches the drawing: clicking a step opens the panel with its context, dragging moves the drawing, and the mouse wheel zooms. The Export menu of a trace also downloads the graph or the map by systems as a draw.io file (`.drawio`), with the positions on screen and the `repository · file:line` of each step.

## Database schema

Rastro reads the database schema from the repository's own code, without connecting to any database: the EF Core model (`DbContext`, `DbSet`, attributes, `OnModelCreating`, `IEntityTypeConfiguration` and `UseSnakeCaseNamingConvention`), the migrations, and the `.sql` files of the database project (SQL Server, through Microsoft's official T-SQL reader, and PostgreSQL, through SqlParserCS, both inside the package and with no native library). The dialect comes from the repository: `UseNpgsql` or `UseSqlServer`, the migration annotations and, without EF, the markers in the `.sql` files. Tables, columns, keys, indexes, views and procedures come out, each item with the file and line it came from. When the model and a migration disagree (a column size, for example), both versions appear and Rastro does not choose. Nothing in your code is compiled or executed, and no table data is read.

The schema appears on each repository's **Database map** screen ("View the database map" link in the new trace): tables with PK and FK, arrows with cardinality, search, a cut to one table and its neighbors, export to PNG, SVG and Mermaid `erDiagram`, the "personal" mark on columns named like personal data (by name only), and the finding for a foreign key created without an index by the migration or the `.sql`. In a trace, a step that uses EF Core says which table it reads or writes (query, insert, update, delete) and which procedure it runs, and the table panel shows the flows that use it. The database findings that need no AI (query inside a loop, `ToList` before the filter, read without `AsNoTracking`, procedure called through raw SQL, `ToList` just to delete, and `LIKE` with a leading wildcard) go into the flow's improvements, also when the query lives in a generic repository (`IRepository<T>`), whose `T` is closed by the call. A trace made before this version has to be redone to get this. It is also available through the API (`GET api/repositories/{nome}/schema` and `GET api/repositories/{nome}/schema/usage`) and the MCP (`get_db_schema`, and the `db` field of the steps).

On the **Analyze query** tab of the same screen, you paste a query in the repository's dialect, SQL Server or PostgreSQL (or open the one from a flow step that uses raw SQL), and Rastro points out the bottlenecks by severity, with the marked excerpt and the reason: `SELECT *`, a function on the filter column, `LIKE '%...'`, `OR` across columns, a column without an index in the filter or the join (a composite index counts when its leading columns are in the filter with equality), an unbounded read filtered only by equality, a join without a condition, `DISTINCT` over a join, a correlated subquery, `ORDER BY` without a limit, `NOT IN` with a nullable column, and `UPDATE` or `DELETE` without `WHERE` (on PostgreSQL, also `ILIKE '%...'` and the `::` cast on the filter column). The query is not executed and nothing goes to any database. With a live database, the catalog's row estimate enters the unbounded-read check: a small table raises no finding, and a parent's large child list raises one again. Also through the API (`POST api/repositories/{nome}/schema/analyze`) and the MCP (`analyze_query`).

**Live database, optional and read-only:** with the "Connect live database" button, you register the connection string of a SQL Server or a PostgreSQL (encrypted in the local database, never returned to the screen; the dialect comes from the string itself or from the repository). The catalog becomes one more source of the schema, and the query tab asks the database for the estimated plan, without executing the query: `SET SHOWPLAN_XML ON` on SQL Server (with the index the database itself says is missing) and `EXPLAIN` without `ANALYZE` on PostgreSQL, on a read-only connection and transaction, with the costliest operations and the scans. Table data is never read. Use a read-only user. On SQL Server, `VIEW DEFINITION` for the catalog and, for the plan, `SHOWPLAN` and `SELECT` on the tables in the query (SQL Server only shows the plan to someone who can run the statement). On PostgreSQL, `CONNECT` on the database for the catalog and, for the plan, `USAGE` on the schema and `SELECT` on the tables in the query; also make the user read-only in the database (`ALTER ROLE usuario SET default_transaction_read_only = on`).

**AI suggests the improvement (optional):** with provider mode on and the "Suggestions" use, the "Suggest an improvement with AI" button asks for the rewritten query and the indexes, with the reason for each change. Rastro checks before showing (the rewrite is read by the dialect's SQL reader, does the same operation, cites only tables and columns that exist, and returns the same columns; the index uses columns that exist and does not repeat one that already exists) and rejects with the reason whatever fails. Before and after appear side by side, with the estimated plan cost of both when there is a live database, without executing anything. Nothing is applied: you are the one who copies. The provider receives the query (with its literals), the findings, and only the tables it uses (names, columns, types and indexes). The connection string and the table data are never sent.

## With your AI agent (MCP)

Rastro works without AI. With an agent that speaks MCP, such as Claude Code, the agent fills in what the compiler could not resolve and writes the rules for each step, using its own subscription:

```
claude mcp add rastro -- rastro mcp
```

The MCP instructions and the tool descriptions are in English, because the agent reads them once, when it connects, and understands them better that way. What the tools return comes out in the language you chose in Rastro (read again on every call, so switching on screen takes effect in the next response), and the agent replies to you in the language you write in.

For the cross-system flow, the `list_cross_edges` tool lists the HTTP links and the calls still without a destination, and `propose_edge` accepts the `system` parameter, with the destination repository.

For questions about the flow, `get_flow_map` shows the map by system (blocks, decisions, calls and paths) and `get_flow_rules` the rules taken from the decisions, so the agent can answer citing the steps. For a business-level reading, `get_business_view` gives only the key steps (start, decisions that change the outcome, notifications, rejections, calls between systems and end), each with the text proposed by rule and its `repository · file:line`; what counts as a notification or a rejection is set in `config.json`, under `Rastro:BusinessView`.

For the database, `get_db_schema` gives the schema read from the code (or just one table), with the source of each item and the differences between the model, the migrations and the `.sql` files, and `analyze_query` gives the findings for a SQL query, without executing it, with the schema of only the tables it uses. With `propose_query_rewrite`, the agent proposes the rewritten query and the indexes; Rastro checks them the same way as on screen and rejects with the reason whatever fails. Nothing is applied: you are the one who copies. With a live database registered on screen, `get_query_plan` gives the estimated plan and the rewrite comes with the cost before and after; the connection string never goes to the agent.

Everything the AI proposes arrives marked as inferred and only enters the flow when you confirm it.

## License

Open source, MIT license: <https://github.com/felipemarcianodev/rastro-ai>.

## Language

Rastro speaks English, Brazilian Portuguese and Spanish, in the UI, the API, the CLI and the MCP. Choose in Settings, under Appearance: the choice is saved in `config.json` (`Rastro:Language`, with `en-US`, `pt-BR` or `es`). Without it, the operating system's language applies, with English as the fallback. The CLI (help and messages) follows that same `config.json`; in the MCP, only the instructions and the tool descriptions stay in fixed English. The palette and the theme stay in the browser only. English is the main language of the documentation. The Portuguese and Spanish versions were translated with AI help, and Spanish has no native-speaker review: corrections by pull request are welcome.

## Your data

It lives in `~/.rastro` (the SQLite database and the registered repositories). To use another folder, set the `RASTRO_HOME` variable.

## Update and uninstall

Stop `rastro` first (on Windows, an executable in use cannot be replaced):

```
dotnet tool update -g rastro-ai
dotnet tool uninstall -g rastro-ai
```
