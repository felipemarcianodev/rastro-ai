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
