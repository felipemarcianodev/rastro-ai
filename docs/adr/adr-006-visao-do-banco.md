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

### 3. Banco vivo: opcional, só de leitura, nunca os dados

- **Opcional.** Sem conexão, tudo sai do código, e a tela não mostra opção morta.
- **Nunca lê dados das tabelas.** O Rastro só consulta o catálogo (tabelas, colunas, chaves, índices, views e procedures) e pede o **plano estimado** de uma consulta, sem executá-la (`SET SHOWPLAN_XML ON` no SQL Server; `EXPLAIN` sem `ANALYZE` no PostgreSQL).
- **Comandos fixos.** As consultas ao catálogo são constantes no código, com parâmetros; nenhum texto da requisição entra montado no SQL.
- **Consulta do plano conferida antes.** A consulta que o usuário cola só vai ao banco depois de passar pelo leitor de SQL: uma única instrução `SELECT`, `INSERT`, `UPDATE`, `DELETE` ou `MERGE`, sem `SET`, `EXEC`, `GO`, DDL nem mais de uma instrução. Assim um `SET SHOWPLAN_XML OFF` escondido na consulta não chega ao banco.
- **Tempo limite** em todo comando e na abertura da conexão.
- **Usuário de banco recomendado com permissão mínima** (no SQL Server, `VIEW DEFINITION` para o catálogo e `SHOWPLAN` para o plano), escrito no README. **Correção na Fase 40b:** o SQL Server só devolve o plano estimado a quem tem a permissão de rodar a instrução, então o plano pede também o `SELECT` (ou o `INSERT`, `UPDATE` e `DELETE`) nas tabelas da consulta. Sem ele, o catálogo funciona e o plano volta recusado com esse motivo. O Rastro continua sem executar a consulta e sem ler dados. A garantia do Rastro é o código acima; a permissão mínima é a segunda camada, do lado do cliente.
- **String de conexão cifrada.** Fica no banco local do Rastro (`~/.rastro/rastro.db`), cifrada com DataProtection, como a chave da IA, com um propósito próprio. Nunca volta na resposta da API, nunca vai para o log e nunca vai para a IA. A tela só mostra se há conexão e o servidor (sem usuário e senha).
- **Por repositório.** A conexão é cadastrada no repositório, pela tela, e só vale para ele. O driver é o oficial de cada banco (`Microsoft.Data.SqlClient` no SQL Server e `Npgsql` no PostgreSQL), dentro do pacote.
- **Quando entra no código:** implementada na Fase 40b, a primeira que precisa dela (plano estimado), junto com a leitura do catálogo como mais uma fonte do esquema. Os comandos ao catálogo são constantes, a conexão não entra em pool (o `SHOWPLAN_XML` não passa para outra conexão), tem tempo limite de 10 s para abrir e de 30 s por comando e se identifica como "Rastro (só leitura)". As variáveis da consulta (`@p0`) entram declaradas como texto antes do pedido do plano.

### 4. O que sai da máquina e o que é guardado

| O quê | Para onde | Quando |
|---|---|---|
| Consultas ao catálogo e pedido de plano estimado | o servidor de banco que o usuário cadastrou (pode estar em outra máquina) | só com banco vivo ligado |
| A consulta analisada, os achados e só as tabelas que ela usa (nomes, colunas, tipos, índices) | o provedor de IA | só com o modo provedor ligado e no clique de "Sugerir melhora" (Fase 41) |
| Dados das tabelas | lugar nenhum | nunca: o Rastro não os lê |
| String de conexão | lugar nenhum | nunca sai da máquina nem do banco local |

Guardado no banco local: a string de conexão cifrada. O esquema é refeito a cada leitura, como o grafo do fluxo, e não é mantido em paralelo ao código.

## Alternativas recusadas

- **Rodar o `DbContext` do cliente** (carregar o assembly e pedir o `Model` ao EF Core): o modelo sairia completo, mas o Rastro passaria a executar código do cliente, com as dependências e a configuração dele. A leitura pelo Roslyn cobre os casos comuns, e o que ela não alcança fica marcado, como no fluxo.
- **Só o banco vivo:** obriga toda pessoa a passar uma credencial para ver o básico, e a tela do banco deixa de funcionar offline.
- **Parser próprio de SQL (expressão regular):** erra em silêncio em comentário, string e colchete. O leitor oficial não erra.
- **Modelo amarrado ao SQL Server:** o PostgreSQL viraria uma reescrita, e não um leitor a mais.

## Consequências

- O pacote ganha o `ScriptDom` (Fase 37) e, com o banco vivo, o `Microsoft.Data.SqlClient` (Fase 40). O README e o README do pacote dizem o que entra e quando.
- O esquema do EF Core lido pelo Roslyn tem limites (convenção montada em tempo de execução, configuração por reflection). O que não for lido aparece marcado, sem palpite.
- O `SECURITY.md` passa a prometer: nunca ler dados das tabelas, comandos fixos ao catálogo, plano sem executar e string de conexão cifrada.
- As regras de IA plugável não mudam: o esquema, o mapa relacional e os achados da consulta são determinísticos; a IA só propõe, e a proposta passa pela conferência do núcleo antes de aparecer (Fase 41).
