# Rastro AI

Segue o rastro de uma funcionalidade no seu código .NET, da entrada (controller, job, handler) ao fim (ou do clique numa tela Angular até o controller), com todas as bifurcações e a implementação real atrás de cada interface do DI. Mostra o fluxo como grafo editável e gera o prompt exato para a IA alterar aquele ponto.

O Rastro roda só na sua máquina: o código nunca sai dela.

## Instalar

Requer o SDK do .NET 10. O Node (20 ou mais novo) é opcional: só é preciso para levantar fluxos Angular.

```
dotnet tool install -g rastro-ai
```

## Usar

```
rastro
```

O Rastro abre numa janela própria, como um app, e a tela também fica em http://127.0.0.1:7450 para o navegador (só nesta máquina; troque a porta com `--porta`). Fechar a janela encerra o Rastro; `rastro --sem-janela` sobe só o console e o endereço. Na janela, o campo da pasta do repositório ganha o botão "Procurar…", com a caixa "Escolher pasta" do sistema.

A janela usa o WebView do sistema: no Windows, o WebView2 Runtime (já vem no Windows 11 e nas versões atuais do Windows 10); no Linux, a `libwebkit2gtk-4.1` (`sudo apt install libwebkit2gtk-4.1-0`) e uma sessão com tela; no macOS, nada a instalar. Sem isso, o Rastro avisa no console e segue no navegador. A janela vem do Photino.NET (Apache-2.0).

Se o Rastro já estiver aberto nessa porta, o comando só mostra o endereço e termina. Cadastre os repositórios em Configurações (no cabeçalho da tela) ou pelo terminal:

```
rastro repo add minha-api C:\codigo\minha-api
```

Para o build e os testes entrarem na validação de uma mudança, edite os comandos em Configurações ("Editar build e testes") ou informe-os ao cadastrar pelo terminal:

```
rastro repo add minha-api C:\codigo\minha-api --build "dotnet build MinhaApi.slnx" --testes "dotnet test MinhaApi.slnx"
```

## Fluxo entre sistemas

Quando uma API chama outra por HTTP, o fluxo continua do outro lado. Ao criar o levantamento, escolha **Seguir chamadas HTTP para** e marque os outros repositórios (eles precisam estar cadastrados no Rastro). O Rastro casa o verbo e a rota de cada chamada com as rotas desses repositórios e segue o rastro no método que atende, com as bifurcações de cada sistema no mesmo desenho.

O que não dá para casar vira um passo "não resolvido", com o motivo: rota montada em tempo de execução, sem destino, ou mais de um destino. A IA (com uma chave de API em Configurações) ou o seu agente completa esse passo, e você confirma na tela.

A chamada pode sair pelo `HttpClient` ou por uma interface do Refit (`[Get]`, `[Post]` e os demais verbos); nos dois casos o Rastro casa o verbo e a rota com o controller de destino.

Limites: a edição e a validação de uma mudança valem só para o primeiro sistema do levantamento, e o Rastro mantém uma solution aberta por vez.

## Fluxo Angular (opcional)

O levantamento pode começar no clique de uma tela Angular, passar pelo service e pelo `HttpClient` e chegar ao controller .NET, com as bifurcações da tela (`@if`, `*ngIf`, `[disabled]`) no começo do fluxo.

Requisitos: o Node no PATH (sem ele, a tela não oferece o Angular) e o `npm install` já feito no repositório Angular (em monorepo, na subpasta do app), porque o Rastro usa o compilador do TypeScript do próprio projeto, procurado da pasta do `tsconfig` até a raiz do repositório.

Ao criar o levantamento, escolha o `tsconfig` do app no lugar da solution (a tela sugere o `tsconfig.app.json` das pastas com `angular.json`) e informe o ponto de entrada como `Componente.metodo`, por exemplo `DetalhePedidoComponent.cancelar`. Para atravessar até as APIs, marque os repositórios .NET em **Seguir chamadas HTTP para**; sem a lista, o levantamento mostra só o trecho Angular.

Se o método abre um modal com `MatDialog.open(Componente)` ou com o `Dialog` do CDK, o fluxo ganha um galho "abre o modal Componente" com os métodos que o template do modal liga a eventos (`(click)`, `(ngSubmit)`), e segue dali até o `HttpClient`. Esse vínculo vem da leitura do HTML do modal (ação do usuário), então as arestas aparecem como inferidas. O `afterClosed()` continua depois do galho. Serviço próprio que repassa para o `MatDialog` não é reconhecido.

O que não dá para ler (URL montada fora do código, classe escolhida só em tempo de execução, sem destino ou mais de um destino, profundidade passada) vira um passo "não resolvido", com o motivo. O leitor de template é leve: só enxerga as condições escritas no HTML do próprio componente, sem o compilador do Angular. A edição e a validação de uma mudança valem só para fluxos .NET.

## Visão consolidada

O fluxo também tem um **mapa**, no nível de um diagrama C2: uma faixa por sistema, poucos blocos em cada uma, as decisões que mudam o desfecho, as chamadas entre sistemas e os caminhos de ponta a ponta. Clicar num bloco mostra só aquele trecho. As decisões viram **regras** em frase, com a fonte no código, e o Rastro responde **perguntas** prontas sobre o fluxo; com o modo provedor ligado, também a pergunta livre, que usa o uso "Sugestões" e manda ao provedor a pergunta, o mapa, as regras e até 8 trechos de código.

Cada levantamento tem uma visão resumida em poucos passos, em frase, como um diagrama Mermaid que você edita ao lado da prévia. Clique num passo para escolher a cor dele. A versão editada fica salva no levantamento. No fluxo, a visão também é um dos desenhos do botão que troca o desenho: clicar num passo abre o painel com o contexto dele, arrastar move o desenho e a roda do mouse aproxima.

## Esquema do banco

O Rastro lê o esquema do banco do próprio código do repositório, sem conectar em banco nenhum: o modelo do EF Core (`DbContext`, `DbSet`, atributos, `OnModelCreating`, `IEntityTypeConfiguration` e o `UseSnakeCaseNamingConvention`), as migrations e os arquivos `.sql` do projeto de banco (SQL Server, pelo leitor oficial de T-SQL da Microsoft, e PostgreSQL, pelo SqlParserCS, os dois dentro do pacote e sem biblioteca nativa). O dialeto sai do repositório: o `UseNpgsql` ou o `UseSqlServer`, as anotações das migrations e, sem EF, as marcas dos `.sql`. Saem tabelas, colunas, chaves, índices, views e procedures, cada item com o arquivo e a linha de onde veio. Quando o modelo e a migration discordam (um tamanho de coluna, por exemplo), as duas versões aparecem e o Rastro não escolhe. Nada do seu código é compilado nem executado, e nenhum dado das tabelas é lido.

O esquema aparece na tela **Mapa do banco** de cada repositório (link "Ver o mapa do banco" no levantamento novo): as tabelas com PK e FK, as setas com a cardinalidade, a busca, o recorte numa tabela e nas vizinhas, a exportação em PNG, SVG e Mermaid `erDiagram`, a marca "pessoal" nas colunas com nome de dado pessoal (só pelo nome) e o achado de chave estrangeira criada sem índice pela migration ou pelo `.sql`. No levantamento, o passo que usa o EF Core diz qual tabela lê ou grava (consulta, inclusão, alteração, exclusão) e qual procedure executa, e o painel da tabela mostra os fluxos que a usam. Os achados sem IA do banco (consulta dentro de laço, `ToList` antes do filtro, leitura sem `AsNoTracking`, procedure por SQL cru, `ToList` só para apagar e `LIKE` com curinga no começo) entram nas melhorias do fluxo, também quando a consulta está num repositório genérico (`IRepository<T>`), cujo `T` é fechado pela chamada. Levantamento feito antes desta versão precisa ser refeito para ter isso. Também sai pela API (`GET api/repositories/{nome}/schema` e `GET api/repositories/{nome}/schema/usage`) e pelo MCP (`get_db_schema`, e o campo `db` dos passos).

Na aba **Analisar consulta** da mesma tela, você cola uma consulta no dialeto do repositório, SQL Server ou PostgreSQL (ou abre a de um passo do fluxo que usa SQL cru), e o Rastro aponta os gargalos por gravidade, com o trecho marcado e o motivo: `SELECT *`, função na coluna do filtro, `LIKE '%...'`, `OR` entre colunas, coluna sem índice no filtro ou na junção (o índice composto conta quando as colunas iniciais dele estão no filtro com igualdade), leitura sem limite filtrada só por igualdade, junção sem condição, `DISTINCT` sobre junção, subconsulta correlacionada, `ORDER BY` sem limite, `NOT IN` com coluna que aceita nulo e `UPDATE` ou `DELETE` sem `WHERE` (no PostgreSQL, também o `ILIKE '%...'` e a conversão com `::` na coluna do filtro). A consulta não é executada e nada vai a banco nenhum. Também pela API (`POST api/repositories/{nome}/schema/analyze`) e pelo MCP (`analyze_query`).

**Banco vivo, opcional e só de leitura:** pelo botão "Conectar banco vivo", você cadastra a string de conexão de um SQL Server ou de um PostgreSQL (cifrada no banco local, nunca devolvida para a tela; o dialeto sai da própria string ou do repositório). O catálogo entra como mais uma fonte do esquema, e a aba de consulta pede o plano estimado ao banco, sem executar a consulta: `SET SHOWPLAN_XML ON` no SQL Server (com o índice que o próprio banco diz faltar) e `EXPLAIN` sem `ANALYZE` no PostgreSQL, numa conexão e numa transação só de leitura, com as operações mais caras e as varreduras. Os dados das tabelas nunca são lidos. Use um usuário só de leitura. No SQL Server, `VIEW DEFINITION` para o catálogo e, para o plano, `SHOWPLAN` e o `SELECT` nas tabelas da consulta (o SQL Server só mostra o plano a quem pode rodar a instrução). No PostgreSQL, `CONNECT` no banco para o catálogo e, para o plano, `USAGE` no schema e `SELECT` nas tabelas da consulta; deixe o usuário só de leitura também no banco (`ALTER ROLE usuario SET default_transaction_read_only = on`).

**A IA sugere a melhora (opcional):** com o modo provedor ligado e o uso "Sugestões", o botão "Sugerir melhora com a IA" pede a consulta reescrita e os índices, com o motivo de cada mudança. O Rastro confere antes de mostrar (a reescrita é lida pelo leitor de SQL do dialeto, faz a mesma operação, cita só tabelas e colunas que existem e devolve as mesmas colunas; o índice usa colunas que existem e não repete um que já existe) e recusa com o motivo o que não passar. Antes e depois aparecem lado a lado, com o custo estimado do plano das duas quando há banco vivo, sem executar nada. Nada é aplicado: quem copia é você. Vão ao provedor a consulta (com os literais), os achados e só as tabelas que ela usa (nomes, colunas, tipos e índices). A string de conexão e os dados das tabelas nunca vão.

## Com o seu agente de IA (MCP)

O Rastro funciona sem IA. Com um agente que fala MCP, como o Claude Code, o agente completa o que o compilador não resolveu e escreve as regras de cada passo, usando a assinatura dele:

```
claude mcp add rastro -- rastro mcp
```

Para o fluxo entre sistemas, a ferramenta `list_cross_edges` lista as ligações HTTP e as chamadas ainda sem destino, e `propose_edge` aceita o parâmetro `system`, com o repositório de destino.

Para perguntas sobre o fluxo, `get_flow_map` mostra o mapa por sistema (blocos, decisões, chamadas e caminhos) e `get_flow_rules` as regras tiradas das decisões, para o agente responder citando os passos.

Para o banco, `get_db_schema` dá o esquema lido do código (ou só uma tabela), com a fonte de cada item e as divergências entre o modelo, as migrations e os `.sql`, e `analyze_query` dá os achados de uma consulta SQL, sem executá-la, com o esquema só das tabelas que ela usa. Com `propose_query_rewrite`, o agente propõe a consulta reescrita e os índices; o Rastro confere do mesmo jeito que na tela e recusa com o motivo o que não passar. Nada é aplicado: quem copia é você.

Tudo o que a IA propõe chega marcado como inferido e só entra no fluxo quando você confirma.

## Licença

Código aberto, licença MIT: <https://github.com/felipemarcianodev/rastro-ai>.

## Seus dados

Ficam em `~/.rastro` (banco SQLite e repositórios cadastrados). Para usar outra pasta, defina a variável `RASTRO_HOME`.

## Atualizar e remover

Pare o `rastro` antes (no Windows, o executável em uso não pode ser trocado):

```
dotnet tool update -g rastro-ai
dotnet tool uninstall -g rastro-ai
```
