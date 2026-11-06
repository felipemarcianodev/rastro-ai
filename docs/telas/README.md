# Telas

Prints de cada fase, tirados no fim dela. Desde a abertura do código (Fase 24, [ADR-004](../adr/adr-004-codigo-aberto.md)), só entram prints de repositórios públicos; os das fases anteriores mostravam código privado e ficaram fora deste repositório.

Os fluxos abaixo são do [Mirante](https://github.com/felipemarcianodev/apresentacao-dashboard-angular) (`ConsultasComCache.ObterAsync`, a consulta às fontes de dados com cache e reserva) e do [saga-poc-dotnet](https://github.com/felipemarcianodev/saga-poc-dotnet).

| Fase | Tela | Arquivo |
|---|---|---|
| 24 | Linha do tempo (vertical): passos numerados na ordem do código, o `try` como bloco com uma seção para cada `catch`, a etiqueta "cache" e o resumo do fluxo na visão geral | [fase-24/01-linha-do-tempo.jpg](fase-24/01-linha-do-tempo.jpg) |
| 24 | Árvore: a mesma regra, com ligações retas em ângulo | [fase-24/02-arvore.jpg](fase-24/02-arvore.jpg) |
| 24 | Horizontal: os passos seguem para a direita e o que cada passo chama desce | [fase-24/03-horizontal.jpg](fase-24/03-horizontal.jpg) |
| 24 | Modo "Resumo": só o código do método de entrada, com os métodos chamados fechados | [fase-24/04-modo-resumo.jpg](fase-24/04-modo-resumo.jpg) |
| 24 | Legenda: ordem de leitura, etiquetas do que o passo faz no mundo e linhas de destaque | [fase-24/05-legenda.jpg](fase-24/05-legenda.jpg) |
| 24 | Levantamentos e o formulário de levantamento novo, com a explicação de cada campo | [fase-24/06-levantamentos.jpg](fase-24/06-levantamentos.jpg) |
| 24 | Configurações: cadastrar um repositório pela tela | [fase-24/07-adicionar-repositorio.png](fase-24/07-adicionar-repositorio.png) |
| 27 | Visão consolidada: o texto Mermaid à esquerda e a prévia à direita, gerada do fluxo do `ConsultasComCache.ObterAsync` (Mirante) | [fase-27/01-visao-consolidada.jpg](fase-27/01-visao-consolidada.jpg) |
| 27 | Cor do passo: o passo escolhido na prévia, o painel com fundo, borda e texto e o passo já pintado | [fase-27/02-cor-do-passo.jpg](fase-27/02-cor-do-passo.jpg) |
| 25 | Levantamento novo com "Seguir chamadas HTTP para": os outros repositórios cadastrados, desligados por padrão | [fase-25/01-levantamento-novo.jpg](fase-25/01-levantamento-novo.jpg) |
| 25 | Um levantamento só com três sistemas: `PedidosController.Cancelar` segue o `POST api/estornos` para a API de pagamentos e, no boleto, o `POST api/notificacoes` para a terceira; cada sistema numa moldura, com as bifurcações dele | [fase-25/02-fluxo-entre-sistemas.jpg](fase-25/02-fluxo-entre-sistemas.jpg) |
| 25 | Visão consolidada com um bloco por sistema e a ligação HTTP no rótulo da seta | [fase-25/03-visao-consolidada.jpg](fase-25/03-visao-consolidada.jpg) |
| 26 | Do clique à API: `DetalhePedidoComponent.cancelar` (loja fictícia em Angular) com a guarda do template (`@if (!pedido.enviado)`), a confirmação e o `POST api/pedidos/{}/cancelamento`, que segue para a API de pedidos e dela para a de pagamentos, cada sistema na sua moldura | [fase-26/01-do-clique-ate-as-apis.jpg](fase-26/01-do-clique-ate-as-apis.jpg) |
| 26 | Visão consolidada do mesmo fluxo, com um bloco para a tela e um para cada API | [fase-26/02-visao-consolidada.jpg](fase-26/02-visao-consolidada.jpg) |
| 28 | O consolidado como desenho do fluxo: o passo `EstornosController.Criar` escolhido no desenho, com o painel do passo aberto ao lado (loja fictícia) | [fase-28/01-consolidado-no-fluxo.jpg](fase-28/01-consolidado-no-fluxo.jpg) |
| 28 | Tela de edição com o palco novo: zoom, enquadrar e 100% no canto, o desenho aberto na largura e o "Ver no fluxo" no painel de cor | [fase-28/02-edicao-com-zoom.jpg](fase-28/02-edicao-com-zoom.jpg) |
| 35 | Mapa enxuto: o terceiro sistema recolhido num bloco com "+4", abre ao clicar (mapa fictício) | [fase-35/01-sistema-recolhido.jpg](fase-35/01-sistema-recolhido.jpg) |
| 35 | O mesmo mapa com o sistema aberto: o quarto sistema fica recolhido e o painel oferece recolher | [fase-35/02-sistema-aberto.jpg](fase-35/02-sistema-aberto.jpg) |
| 35 | Clique no desfecho "Se não": o caminho acende, sem a lista no topo, e a barra oferece limpar | [fase-35/03-destaque-do-desfecho.jpg](fase-35/03-destaque-do-desfecho.jpg) |
| 38 | Mapa do banco da loja fictícia: `pagamentos` escolhida, com o achado "chave estrangeira sem índice" criado pela migration, a seta acesa até `pedidos` e as colunas de dado pessoal marcadas em `clientes` | [fase-38/01-achado-fk-sem-indice.jpg](fase-38/01-achado-fk-sem-indice.jpg) |
| 38 | Painel de `pedidos` com a divergência entre fontes (o modelo diz `nvarchar(20)` e a migration `nvarchar(30)`) e o tipo marcado como divergente | [fase-38/02-divergencia-entre-fontes.jpg](fase-38/02-divergencia-entre-fontes.jpg) |
| 38 | Recorte: `pedidos` e as vizinhas até 1 salto, com a contagem do que ficou fora | [fase-38/03-recorte-por-tabela.jpg](fase-38/03-recorte-por-tabela.jpg) |
| 38 | A procedure `sp_baixar_estoque` escolhida acende as tabelas que ela lê e grava | [fase-38/04-procedure-acende-tabelas.jpg](fase-38/04-procedure-acende-tabelas.jpg) |
| 38 | Exportar o que está no mapa: PNG, SVG e Mermaid `erDiagram` | [fase-38/05-exportar.jpg](fase-38/05-exportar.jpg) |
| 39 | Painel de `pedidos` com "Fluxos que usam esta tabela" (inclusão em `PedidosController.Fechar` e leitura em `PedidoRepositorio.AbertosAsync`) e os achados sem IA dos fluxos ("ToList antes do filtro" e "Leitura sem AsNoTracking"); os blocos com selo de achado | [fase-39/01-fluxos-que-usam-a-tabela.jpg](fase-39/01-fluxos-que-usam-a-tabela.jpg) |
| 39 | O passo `PedidoRepositorio.AbertosAsync` aberto pelo link do painel da tabela: "No banco" (lê `pedidos`, linha 18), a etiqueta "banco" nos passos e a categoria "Banco de dados" nas melhorias | [fase-39/02-passo-no-banco.jpg](fase-39/02-passo-no-banco.jpg) |
| 39 | A procedure `sp_baixar_estoque` com o fluxo que a chama por `ExecuteSqlRaw` e o achado "Procedure por SQL cru" | [fase-39/03-procedure-chamada-pelo-fluxo.jpg](fase-39/03-procedure-chamada-pelo-fluxo.jpg) |
| 40 | O passo `RelatorioController.DoAno` (loja fictícia) com SQL cru: "No banco" lê `pedidos` e oferece "Analisar a consulta" | [fase-40/01-passo-com-consulta.jpg](fase-40/01-passo-com-consulta.jpg) |
| 40 | Aba "Analisar consulta" com a consulta do passo: quatro achados por gravidade, os trechos sublinhados e o da função no filtro destacado pelo clique, com as tabelas da consulta ao lado | [fase-40/02-achados-com-trecho-marcado.jpg](fase-40/02-achados-com-trecho-marcado.jpg) |
| 40 | Uma consulta limpa (com `TOP`, filtro e junção por colunas com índice): sem achados | [fase-40/03-consulta-limpa.jpg](fase-40/03-consulta-limpa.jpg) |
| 40 | Diálogo "Banco vivo": a conexão salva (só o servidor e o banco), o teste com a contagem de tabelas e a recomendação de permissão mínima; o selo na barra e a fonte "banco vivo" no esquema | [fase-40/04-banco-vivo-conexao.jpg](fase-40/04-banco-vivo-conexao.jpg) |
| 40 | Plano estimado pelo banco ao lado dos achados: ordenação, varredura do índice clustered de `pedidos` e busca em `clientes`, sem executar a consulta (SQL Server descartável em contêiner) | [fase-40/05-plano-estimado.jpg](fase-40/05-plano-estimado.jpg) |
| 41 | Com a IA ligada, a seção "Sugestão da IA" abaixo dos achados, com o botão e o que vai para o provedor (loja fictícia, API local com `RASTRO_HOME` temporário) | [fase-41/01-botao-sugerir-melhora.jpg](fase-41/01-botao-sugerir-melhora.jpg) |
| 41 | O provedor falhou (chave falsa de teste, recusada pelo provedor): a mensagem na própria seção | [fase-41/02-erro-do-provedor.jpg](fase-41/02-erro-do-provedor.jpg) |
| 41 | Reescrita aceita pela conferência: antes e depois lado a lado, copiar, os achados que ficaram e as mudanças com o motivo (resposta da IA simulada, conferida pelo núcleo de verdade) | [fase-41/03-reescrita-aceita-antes-e-depois.jpg](fase-41/03-reescrita-aceita-antes-e-depois.jpg) |
| 41 | Índices sugeridos: um aceito, com o `CREATE INDEX` escrito pelo Rastro, e um recusado porque o índice já existe | [fase-41/04-indices-aceito-e-recusado.jpg](fase-41/04-indices-aceito-e-recusado.jpg) |
| 41 | Reescrita recusada: devolvia 2 das 10 colunas da original; o motivo aparece e o texto fica escondido | [fase-41/05-reescrita-recusada.jpg](fase-41/05-reescrita-recusada.jpg) |
| 41 | IA desligada: a análise sem nenhum botão de IA (ADR-001) | [fase-41/06-sem-ia-sem-botao.jpg](fase-41/06-sem-ia-sem-botao.jpg) |
