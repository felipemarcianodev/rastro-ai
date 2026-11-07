# ADR-006: visão do banco pelo código, com banco vivo opcional e só de leitura

**Data:** 2026-11-06
**Status:** Accepted
**Contexto:** Fases 36 a 42 do [plano](../arquitetura/plano-mvp.md) (visão do banco e análise de consulta). Decisões do dono de 2026-11-06, sobre o protótipo navegável da tela.

---

## Contexto

O dono pediu duas funcionalidades: a visão do banco (tabelas, campos, chaves, índices, relacionamentos, views e procedures, com um mapa relacional) e a análise de consulta (receber uma consulta SQL e apontar onde ela tem gargalo; com a IA plugada, a IA sugere a melhora).

As duas mexem no que o cliente mais protege. A primeira pergunta de quem avalia uma ferramenta assim é "ela lê os meus dados?", e a segunda é "para onde vai a minha string de conexão?". A resposta precisa estar escrita antes do código.

O Rastro já lê o repositório (Roslyn, ADR-001 e ADR-005). Quase todo sistema .NET guarda o esquema no próprio código: o modelo do EF Core, as migrations e, às vezes, scripts `.sql` ou um projeto de banco (`.sqlproj`). O banco vivo tem o que o código não tem (o plano de execução, o índice criado à mão em produção), mas exige uma credencial.

## Decisão

### 1. Fontes do esquema: primeiro o código

| Fonte | Como é lida | Quando |
|---|---|---|
| Modelo do EF Core (`DbContext`, `DbSet`, atributos, `OnModelCreating`, `IEntityTypeConfiguration`) | Roslyn, só a árvore e os símbolos, **sem rodar o código do cliente** | Fase 37 |
| Migrations do EF Core (`migrationBuilder.CreateTable`, `AddForeignKey`, `CreateIndex`) | Roslyn, do mesmo jeito | Fase 37 |
| Arquivos `.sql` e projeto de banco (`CREATE TABLE`, `ALTER TABLE`, `CREATE INDEX`, `CREATE VIEW`, `CREATE PROCEDURE`) | leitor de SQL do dialeto (seção 2) | Fase 37 |
| Banco vivo (opcional) | catálogo do banco, só leitura (seção 3) | Fase 40 |

- Cada tabela, coluna, chave e índice guarda **de qual fonte veio** (`arquivo:linha` no código, ou "banco vivo").
- Quando as fontes divergem (o modelo diz `nvarchar(20)` e a migration diz `nvarchar(30)`), o Rastro mostra as duas e marca a divergência. Ele nunca escolhe uma em silêncio.
- A leitura é determinística e não usa IA (ADR-001): o mesmo código dá o mesmo esquema. O extrator mora no `Rastro.Extractor` e o modelo (`DbSchema`) no `Rastro.Core`.

### 2. Bancos suportados: SQL Server primeiro, PostgreSQL depois

- **SQL Server primeiro**, com o leitor oficial de T-SQL da Microsoft, `Microsoft.SqlServer.TransactSql.ScriptDom` (licença MIT). Ele vai dentro do pacote do Rastro, sem nada a instalar na máquina.
- **PostgreSQL neste mesmo plano, na Fase 42**, com um leitor de SQL do PostgreSQL de licença aberta, escolhido na própria fase e registrado aqui. O critério: licença compatível com a MIT, roda no processo do Rastro sem instalar nada e devolve a árvore da instrução, e não só tokens.
- O modelo `DbSchema` e o extrator de `.sql` não são amarrados ao SQL Server: o dialeto é um campo do esquema, e cada leitor de SQL é uma implementação de uma interface comum. O PostgreSQL entra como um leitor a mais, sem mudar o modelo nem a API.
- O tipo da coluna é guardado como o banco escreve (`nvarchar(30)`, `jsonb`), sem traduzir para um tipo genérico, porque a tradução perde informação e é o tipo real que a análise de consulta precisa.

#### O leitor de SQL do PostgreSQL (Fase 42)

**Escolhido: [SqlParserCS](https://github.com/TylerBrinks/SqlParser-cs) 0.6.5** (licença MIT), o porte para C# do `sqlparser-rs`, com o dialeto do PostgreSQL. É código gerenciado (uma DLL só, com a dependência `Microsoft.Extensions.ObjectPool`), roda no processo do Rastro em Windows, Linux e macOS sem instalar nada e devolve a árvore de cada instrução (`CREATE TABLE`, `CREATE INDEX`, `CREATE VIEW`, `ALTER TABLE`, `SELECT`, `INSERT`, `UPDATE`, `DELETE`, `MERGE`), e não só tokens. Mantido (versões de 2025), com cerca de 900 mil downloads no NuGet.

Alternativas descartadas:

| Alternativa | Por que não |
|---|---|
| `pgsqlparser`, `Npgquery`, `PgQuery` (o `libpg_query`, o leitor do próprio PostgreSQL) | A leitura seria a mais fiel, com a posição de cada nó, mas é biblioteca nativa (uma por sistema e processador, 9 MB no pacote). O pacote do Rastro roda em Windows, Linux e macOS, e uma DLL nativa que falte num deles quebra a tela do banco ali. O SqlParserCS cobre o que as regras precisam sem esse risco. |
| Gramática do PostgreSQL do ANTLR (`grammars-v4`, BSD) com o `Antlr4.Runtime.Standard` | Gerenciada, mas a gramática gerada é enorme e lenta, a geração pede Java no build, e a árvore é a da gramática, sem os nós prontos (tabela, coluna, junção) que as regras usam. |
| Leitor comercial (General SQL Parser) | Licença paga, incompatível com o Rastro aberto e gratuito (ADR-004). |
| Expressão regular | Já recusada acima: erra em silêncio em comentário, string e `$$`. |

O que o SqlParserCS não dá, e como o Rastro resolve, sem palpite:
- **Sem a posição de cada nó.** O leitor dá a linha e a coluna de cada token. Para marcar o trecho de um achado, o Rastro escreve o nó de volta (`ToSql`) e procura os tokens dele nos tokens do texto, sem olhar espaço, comentário, caixa nem o `AS` opcional.
- **O `LIKE` engolia o `AND`.** Na versão 0.6.5, o dialeto do PostgreSQL lê `a LIKE '%x' AND b = 1` como `a LIKE ('%x' AND b = 1)`. O Rastro usa o dialeto com o `AND` e o `OR` na precedência deles, como no dialeto genérico do mesmo leitor (teste `EXT-PGQ-018`).
- **O `@p0` do Npgsql** (e o que o Rastro tira do `FromSql`) é lido como o operador `@` sobre um nome; as regras o tratam como parâmetro, como o `$1`.
- **Função e procedure** (`RETURNS SETOF`, o modo `IN`, o corpo em PL/pgSQL) são lidas pelos tokens: o nome, os parâmetros com o modo e as tabelas que o corpo lê e grava. O arquivo é separado nas instruções pelos tokens (o ponto e vírgula dentro de string e de `$$` não conta), e a carga do `COPY ... FROM stdin` do `pg_dump` é ignorada.

**O dialeto sai do repositório**, sem perguntar: o provedor do EF Core (`UseNpgsql` ou `UseSqlServer`), as anotações das migrations (`Npgsql:` ou `SqlServer:`) e, sem EF, as marcas dos `.sql` pelos tokens. Sem marca nenhuma, SQL Server. No PostgreSQL, os sinônimos de tipo (`int` e `integer`, `varchar` e `character varying`, `timestamptz` e `timestamp with time zone`, `serial` e o inteiro dele) são comparados pelo nome do catálogo, e não contam como divergência; o tipo mostrado continua como cada fonte escreve.

### 3. Banco vivo: opcional, só de leitura, nunca os dados

- **Opcional.** Sem conexão, tudo sai do código, e a tela não mostra opção morta.
- **Nunca lê dados das tabelas.** O Rastro só consulta o catálogo (tabelas, colunas, chaves, índices, views e procedures) e pede o **plano estimado** de uma consulta, sem executá-la (`SET SHOWPLAN_XML ON` no SQL Server; `EXPLAIN` sem `ANALYZE` no PostgreSQL).
- **Comandos fixos.** As consultas ao catálogo são constantes no código, com parâmetros; nenhum texto da requisição entra montado no SQL.
- **Consulta do plano conferida antes.** A consulta que o usuário cola só vai ao banco depois de passar pelo leitor de SQL: uma única instrução `SELECT`, `INSERT`, `UPDATE`, `DELETE` ou `MERGE`, sem `SET`, `EXEC`, `GO`, DDL nem mais de uma instrução. Assim um `SET SHOWPLAN_XML OFF` escondido na consulta não chega ao banco.
- **Tempo limite** em todo comando e na abertura da conexão.
- **Usuário de banco recomendado com permissão mínima** (no SQL Server, `VIEW DEFINITION` para o catálogo e `SHOWPLAN` para o plano), escrito no README. **Correção na Fase 40b:** o SQL Server só devolve o plano estimado a quem tem a permissão de rodar a instrução, então o plano pede também o `SELECT` (ou o `INSERT`, `UPDATE` e `DELETE`) nas tabelas da consulta. Sem ele, o catálogo funciona e o plano volta recusado com esse motivo. O Rastro continua sem executar a consulta e sem ler dados. A garantia do Rastro é o código acima; a permissão mínima é a segunda camada, do lado do cliente.
- **String de conexão cifrada.** Fica no banco local do Rastro (`~/.rastro/rastro.db`), cifrada com DataProtection, como a chave da IA, com um propósito próprio. Nunca volta na resposta da API, nunca vai para o log e nunca vai para a IA. A tela só mostra se há conexão e o servidor (sem usuário e senha).
- **Por repositório.** A conexão é cadastrada no repositório, pela tela, e só vale para ele. O driver é o oficial de cada banco (`Microsoft.Data.SqlClient` no SQL Server e `Npgsql` no PostgreSQL), dentro do pacote.
- **PostgreSQL (Fase 42b):** o catálogo sai do `pg_catalog` (tabelas, colunas com o `format_type`, identidade e `serial`, chaves, índices com as colunas incluídas, views e as funções e procedures do usuário, sem as de extensão). O plano é o `EXPLAIN (FORMAT JSON)` **sem `ANALYZE`**, conferido antes pelo `PgSqlPlanGuard` (uma instrução só, sem `COPY`, `CALL`, `DO`, `SELECT INTO`, escrita dentro de outra instrução nem função de efeito colateral conhecida). Três camadas atrás da porta: a conexão abre com `default_transaction_read_only` e o `statement_timeout` do próprio banco (o que a string trouxer em `Options` é substituído), o pedido roda numa transação `READ ONLY` que sempre volta atrás, e o comando vai num lote de um só, que o PostgreSQL recusa com duas instruções. O teste com PostgreSQL de verdade confere que o `EXPLAIN` de `INSERT`, `UPDATE` e `DELETE` não muda nenhuma linha. Com parâmetro (`$1`, ou o `@nome` do Npgsql, trocado por `$n` pelos tokens), a consulta vira um `PREPARE` e o plano é o genérico (`plan_cache_mode = force_generic_plan` e `EXPLAIN EXECUTE` com nulos), do PostgreSQL 12 em diante; o `EXPLAIN (GENERIC_PLAN)` do PostgreSQL 16 não serve, porque o driver manda os parâmetros pelo protocolo e o banco planeja com os valores. Permissão mínima recomendada: `CONNECT` no banco para o catálogo e, para o plano, `USAGE` no schema e `SELECT` nas tabelas da consulta (o PostgreSQL confere a permissão da instrução também no `EXPLAIN`). O dialeto da conexão sai da própria string (as chaves que só um dos drivers aceita) ou, quando ela serve aos dois, do código do repositório, e fica guardado com a conexão (migration `BancoVivoDialeto`).
- **Quando entra no código:** implementada na Fase 40b, a primeira que precisa dela (plano estimado), junto com a leitura do catálogo como mais uma fonte do esquema. Os comandos ao catálogo são constantes, a conexão não entra em pool (o `SHOWPLAN_XML` não passa para outra conexão), tem tempo limite de 10 s para abrir e de 30 s por comando e se identifica como "Rastro (só leitura)". As variáveis da consulta (`@p0`) entram declaradas como texto antes do pedido do plano.

### 4. O que sai da máquina e o que é guardado

| O quê | Para onde | Quando |
|---|---|---|
| Consultas ao catálogo e pedido de plano estimado | o servidor de banco que o usuário cadastrou (pode estar em outra máquina) | só com banco vivo ligado |
| Plano estimado e custo da consulta (sem a string de conexão) | o agente conectado pelo MCP, na máquina do usuário | só com banco vivo cadastrado na tela, quando o agente chama `get_query_plan` ou `propose_query_rewrite` (Fase 44) |
| A consulta analisada, os achados e só as tabelas que ela usa (nomes, colunas, tipos, índices) | o provedor de IA | só com o modo provedor ligado e o uso "Sugestões", no clique de "Sugerir melhora com a IA" (implementado na Fase 41). No modo agente, o `analyze_query` entrega o mesmo recorte ao agente |
| Dados das tabelas | lugar nenhum | nunca: o Rastro não os lê |
| String de conexão | lugar nenhum | nunca sai da máquina nem do banco local |

Guardado no banco local: a string de conexão cifrada. O esquema é refeito a cada leitura, como o grafo do fluxo, e não é mantido em paralelo ao código.

### Ajustes da Fase 43 (teste num sistema real, 2026-11-06)

O teste das consultas de uma tela real (PostgreSQL, 87 tabelas) mostrou que as regras olhavam coluna por coluna e davam mais falsos que verdadeiros. As decisões, todas determinísticas:
- **Índice:** a coluna do filtro está atendida quando um índice da tabela (ou a chave primária) começa por colunas do filtro em igualdade; as outras colunas do filtro são conferidas nas linhas já buscadas. Só há achado quando nenhum índice atende, e é um por tabela.
- **Lista filha e partição:** a leitura sem limite e o `ORDER BY` sem limite não disparam quando o filtro é uma chave estrangeira, declarada ou implícita (a coluna `x_id` com a tabela `x` no esquema). A exceção não vale para a coluna de partição, aquela cujo nome aparece em pelo menos metade das tabelas (o tenant).
- **Escala de gravidade:** a da análise de consulta passa a crescer com a gravidade (`Low = 0`, `High = 2`), no mesmo sentido dos achados do fluxo.
- **Banco nos fluxos:** o repositório genérico é fechado pelo tipo da chamada, a chamada por interface é resolvida pelo tipo do receptor, e os achados seguem a cadeia de chamadas até quatro saltos.

Sem o banco vivo, o Rastro não sabe o tamanho da tabela: a leitura sem limite numa tabela pequena continua aparecendo, com gravidade baixa.

### Ajustes da Fase 44 (pendências do banco, 2026-11-07)

- **Tamanho da tabela pelo catálogo.** Com banco vivo, o catálogo traz a estimativa de linhas de cada tabela, sem ler dados: `pg_class.reltuples` no PostgreSQL e a soma de `sys.partitions.rows` (heap ou índice clusterizado) no SQL Server. As duas leituras são de catálogo e não pedem permissão nova. A estimativa depende das estatísticas do banco (`ANALYZE` no PostgreSQL); valor zero ou desconhecido conta como "sem estimativa".
- **Leitura sem limite pelo tamanho.** Com estimativa, a leitura sem limite não é achado em tabela abaixo de 1.000 linhas, e a exceção da lista filha deixa de valer em tabela com 100.000 linhas ou mais (a lista de um pai que tem muitas linhas). A mensagem diz o número estimado. Sem banco vivo, nada muda. `IS NULL` e `IS NOT NULL` não contam como filtro nessa regra.
- **Entidade principal (sem banco vivo).** A comparação item a item do reteste mostrou que a chave estrangeira implícita calou a lista do contêiner (as tarefas de um espaço). Sem estimativa de tamanho, a exceção da lista filha não vale quando a tabela filtrada é apontada por duas ou mais tabelas do esquema: é entidade principal, e a lista dela tende a crescer. A tabela folha (subtarefas, comentários) ou apontada por uma só (os status de um espaço) continua sem achado. Vale para a leitura sem limite e para o `ORDER BY` sem limite.
- **`EXISTS` na lista do `SELECT`.** A subconsulta dentro de `SELECT EXISTS (...)` (no SQL Server, `CASE WHEN EXISTS`) passa a ser analisada, como a do `WHERE`: antes, o `lower()` no filtro dela não era achado.
- **Banco vivo pelo MCP.** O `rastro mcp` passa a pedir o plano estimado (ferramenta `get_query_plan`) e o `propose_query_rewrite` traz o custo da consulta original e da reescrita, com a mesma porta do leitor de SQL da tela. O cadastro e a string continuam só pela tela; o MCP lê as chaves da DataProtection do banco local (só leitura: nunca cria nem grava chave) para abrir a conexão, e a string nunca vai para o agente, para a resposta nem para o log. O agente vê o servidor, o banco, o plano e o custo, como a tela.

## Alternativas recusadas

- **Rodar o `DbContext` do cliente** (carregar o assembly e pedir o `Model` ao EF Core): o modelo sairia completo, mas o Rastro passaria a executar código do cliente, com as dependências e a configuração dele. A leitura pelo Roslyn cobre os casos comuns, e o que ela não alcança fica marcado, como no fluxo.
- **Só o banco vivo:** obriga toda pessoa a passar uma credencial para ver o básico, e a tela do banco deixa de funcionar offline.
- **Parser próprio de SQL (expressão regular):** erra em silêncio em comentário, string e colchete. O leitor oficial não erra.
- **Modelo amarrado ao SQL Server:** o PostgreSQL viraria uma reescrita, e não um leitor a mais.

## Consequências

- O pacote ganha o `ScriptDom` (Fase 37), com o banco vivo o `Microsoft.Data.SqlClient` (Fase 40) e, para o PostgreSQL, o `SqlParserCS` (Fase 42a) e o `Npgsql` (licença PostgreSQL, Fase 42b). O README e o README do pacote dizem o que entra e quando.
- O esquema do EF Core lido pelo Roslyn tem limites (convenção montada em tempo de execução, configuração por reflection). O que não for lido aparece marcado, sem palpite.
- O `SECURITY.md` passa a prometer: nunca ler dados das tabelas, comandos fixos ao catálogo, plano sem executar e string de conexão cifrada.
- As regras de IA plugável não mudam: o esquema, o mapa relacional e os achados da consulta são determinísticos; a IA só propõe, e a proposta passa pela conferência do núcleo antes de aparecer (Fase 41).
