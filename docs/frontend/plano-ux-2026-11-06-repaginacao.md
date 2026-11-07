# Plano de UX: repaginação da navegação e da usabilidade (2026-11-06)

**Status: EXECUTADO em 2026-11-06** (Fases A, B e C; o item 6 da Fase C fica de fora por regra de segurança). Pedido do dono depois de não achar a tela do banco ("a funcionalidade banco está escondida, é um link lá em cima") e de pedir que a usabilidade seja repaginada. Feito pela skill `levantamento-ux`, revista no mesmo dia com a passada de navegação e a tabela de cobertura vindas da skill de um projeto irmão. O MCP Knowledge não estava disponível; o levantamento seguiu as etapas da própria skill.

## Cobertura

| Tela ou funcionalidade | Código | Tela no navegador | Larguras vistas |
|---|---|---|---|
| Cabeçalho | lido | visto | 1568 (a janela não passou disso) e 360 (num quadro de 360 dentro da página) |
| Tela inicial (lista e Novo levantamento), primeiro uso e com dados | lido | visto, com `RASTRO_HOME` vazio e depois com dois repositórios públicos | 1568 |
| Configurações | lido | visto no primeiro uso | 1568 |
| Tela do fluxo (barra, canvas, painel, menu "⋯", edição, diálogos) | lido | barra, painel, canvas e menu "⋯" vistos; edição e diálogos só pelo código | 1568 |
| Mapa do fluxo, Regras e Perguntas | lido | não visto | |
| Tela do banco (mapa, consulta) | lido | vista | 1568 e 360 |
| Banco vivo, plano estimado e Sugestão da IA | lido | não vistos (exigem banco em contêiner e IA ligada) | |

Dados usados: a API local com `RASTRO_HOME` temporário, as fixtures públicas `TarefasRepo` e `LojaBanco` e um levantamento de `TarefaService.Lote`. A janela do navegador desta máquina não chega a 1920; a área vazia em 1920 foi medida pelo CSS.

## Confiabilidade

O coordenador conferiu na tela: o cabeçalho sem navegação, o link "Banco" na linha de metadados do levantamento, o menu "⋯" só com "Excluir levantamento…", a tela inicial que só fala de levantamento e o mapa do banco com os tipos cortados. Os demais achados vêm da leitura do código pelos agentes, com arquivo e linha. Sem usuário real nem dados de uso, os achados são prováveis, não comprovados; o único comprovado é o principal: o dono, que fez o produto, não achou o banco.

## Descoberta: quantos cliques cada funcionalidade leva

A partir da tela inicial, com pelo menos um levantamento pronto.

| Funcionalidade (`visao.md`) | Entrada hoje | Cliques | Achável por quem não sabe? |
|---|---|---|---|
| Levantar um fluxo | formulário Novo levantamento | 1 depois de preencher | sim |
| Linha do tempo (grafo) | abrir o levantamento | 1 | sim |
| Mapa do fluxo por sistemas | botão "Mapa do fluxo" na barra | 2 | sim |
| Regras do fluxo | painel, seção aberta | 1 | sim |
| Perguntas sobre o fluxo | Mapa do fluxo, aba "Perguntas" | 3 | não: só dentro do mapa |
| IA propõe ligações e você confirma | passo com buraco, "Propor destinos com IA" | 3 | em parte |
| Prompt da mudança e validação | "Editar fluxo", "Gerar prompt" | 3 | em parte |
| Melhorias e dívida técnica | painel, seção aberta | 1 | sim |
| Visão consolidada (Mermaid) | botão na barra | 2 | sim |
| Exportação C4 | "Comparar e exportar", "Diagramas C4" | 3 | não: o rótulo não fala em C4 |
| Comparar | "Comparar e exportar" | 3 | em parte |
| **Mapa do banco** | link de texto "Banco" na linha de metadados do levantamento, ou link de ajuda no formulário depois de escolher o repositório | 2 | **não** |
| **Analisar consulta** | dentro da tela do banco, ou "Analisar a consulta" no painel de um passo com SQL | 3 | **não** |
| Banco vivo | dentro da tela do banco | 3 | não |
| Sugestão da IA para consulta | dentro da aba de consulta, só com IA ligada | 4 | não |
| Documentação | não há entrada própria na tela | | não |
| Configurações (repositórios, IA, MCP) | selo da IA ou engrenagem sem rótulo | 1 | em parte |

## Diagnóstico: os 10 principais problemas

1. **O cabeçalho não tem navegação** (UXN-H01, B01). Os cinco itens são a marca, o selo da IA, a paleta, o tema e a engrenagem. Nenhum é uma área do produto.
2. **O banco só se alcança por um link de texto camuflado** (UXN-F01, B02). "Banco" fica entre o nome do repositório e o caminho da solution, com a mesma cor e o mesmo tamanho do texto.
3. **A tela inicial só fala de levantamento** (UXN-H08). Quem chega não sabe que existem banco, consulta, C4 e perguntas.
4. **O fluxo não leva ao banco** (UXN-F03, F04, F05, B21, B22). A etiqueta "banco" no passo é texto, e os achados de banco só têm "Ver no fluxo".
5. **A barra do levantamento tem até 9 controles do mesmo peso** (UXN-F08), sem grupos.
6. **As três visões do fluxo parecem telas diferentes** (UXN-F09). Grafo, mapa e consolidada são botões soltos; o grafo não tem rótulo.
7. **Perguntas, C4 e Mermaid estão escondidos** (UXN-F07, F10, F11).
8. **Primeiro uso sem caminho** (UXN-H09, H10). O estado vazio pede para escolher um repositório que ainda não existe.
9. **Configurações sem índice e com ações faltando** (UXN-H07, H16, H18). O repositório só tem "Remover…"; build e testes só pelo terminal.
10. **A tela do banco não explica a si mesma** (UXN-B05, B06, B08, B09, B10). As abas, o banco vivo e a IA não dizem o que fazem nem como ligar.

## Achados

Cabeçalho, tela inicial e Configurações (UXN-H):

| ID | Onde | Problema | Sugestão | Risco | Esforço | Impacto |
|---|---|---|---|---|---|---|
| H01 | `app.html:11-15` | sem navegação para as áreas | links Levantamentos, Banco, Configurações | alto (menu) | P | alto |
| H02 | `app.html:12,34` | selo da IA e engrenagem vão ao mesmo lugar | um item "Configurações" com o estado da IA | alto (menu) | P | médio |
| H03 | `app.html:34-39` | engrenagem sem rótulo | rótulo visível | baixo | P | médio |
| H04 | `app.html:17-32` | paleta e tema ocupam o lugar da navegação | levar para Configurações, seção Aparência | alto (menu) | M | médio |
| H05 | `app.html:2` | sem estado ativo de "Levantamentos" | `routerLinkActive` e `aria-current` | baixo | P | médio |
| H06 | `surveys.page.html:111` | link do banco aparece só depois de escolher o repositório e parece ajuda | botão secundário; entrada no cabeçalho | baixo | M | alto |
| H07 | `settings.page.html:22-39` | repositório só tem "Remover…" | ações "Mapa do banco" e "Novo levantamento" | baixo | P | alto |
| H08 | `surveys.page.html:4-5` | tela inicial não mostra o que dá para fazer | bloco de atalhos | alto (copy) | M | alto |
| H09 | `surveys.page.html:18-21` | estado vazio pede repositório que não existe | passos numerados, "Cadastrar repositório" | médio | M | alto |
| H10 | `surveys.page.html:106-108,208` | primeiro uso manda a outra tela; "Levantar o fluxo" ativo sem repositório | cadastrar ali mesmo; desabilitar o envio | médio | M | alto |
| H11 | `surveys.page.html:8` | região viva vazia ocupa espaço | renderizar só com aviso | baixo | P | baixo |
| H12 | `surveys.page.html:15-16` | carregando só em texto | esqueleto da lista | baixo | P | baixo |
| H13 | `surveys.page.html:84,118` | explicação dos campos só no hover do "i" | ajuda fixa sob o campo | baixo | P | médio |
| H14 | `surveys.page.html:111` | atributo sem espaço, link com estilo de ajuda | corrigir | baixo | P | baixo |
| H15 | `settings.page.html:68,165` | dois botões primários na mesma tela | "Adicionar repositório" secundário | baixo | P | baixo |
| H16 | `settings.page.html:3-94` | três cartões empilhados sem índice; IA só no fim | índice de âncoras; selo da IA leva a `#ia` | médio | M | médio |
| H17 | `settings.page.html:3` | "Voltar" pelo histórico pode sair do app | link para a tela inicial sem histórico | médio | P | baixo |
| H18 | `settings.page.html:34` | build e testes só pelo terminal | campos e "Editar" por repositório | médio | G | médio |
| H19 | `settings.page.html:52,60` | exemplo de pasta só do Windows | exemplo neutro | baixo | P | baixo |
| H21 | `surveys.page.scss:7-14` | 1280 de largura máxima; formulário sempre à frente | "Novo levantamento" que abre o painel quando já há levantamentos | alto (reorganiza) | M | médio |
| H22 | `surveys.page.html:42-72` | a linha da lista só abre o levantamento | ações "Visão consolidada" e "Mapa do banco" | baixo | M | médio |
| H23 | `settings.page.html:105` | "cifradas no banco" confunde com a tela Banco | "no armazenamento local do Rastro" | alto (copy) | P | baixo |
| H24 | `settings.page.html:78-81` | erro de carga sem "Tentar de novo" | botão | baixo | P | médio |
| H25 | `settings.page.html:107-109` | IA desligada não diz o que se ganha | listar os usos | alto (copy) | P | médio |
| H26 | `app.html:12` | "IA desligada" parece erro no primeiro uso | "IA: configurar" | alto (copy) | P | baixo |

Tela do fluxo (UXN-F):

| ID | Onde | Problema | Sugestão | Risco | Esforço | Impacto |
|---|---|---|---|---|---|---|
| F01 | `survey.page.html:18` | "Banco" camuflado na linha de metadados | botão "Mapa do banco" no grupo das visões | baixo | P | alto |
| F02 | `survey.page.html:53-99` | banco fora do grupo das visões | junto de Visão consolidada e Mapa do fluxo | baixo | P | médio |
| F03 | `flow-canvas.component.html:110,201` | etiqueta "banco" não leva a nada | dica no `title` e link na legenda | baixo | P | médio |
| F04 | `node-panel.component.html:137-157` | Visão geral não resume as tabelas do fluxo | seção "Banco neste fluxo (N tabelas)" | médio | M | alto |
| F05 | `findings-list.component.html:6-27` | achado de banco não leva ao banco | ação "Ver no mapa do banco" | baixo | M | alto |
| F06 | `survey.page.html:58-93` | "⋯" com uma ação só | botão de texto "Excluir" | alto (remove) | P | baixo |
| F07 | `survey.page.html:59-84` | "Comparar e exportar" esconde C4 e Mermaid | dois botões: "Comparar" e "Exportar" | alto (reorganiza) | M | médio |
| F08 | `survey.page.html:36-107` | até 9 controles sem grupo | grupos Ver, Compartilhar e Alterar | alto (reorganiza) | M | alto |
| F09 | `survey.page.html:53-57` | visões como botões soltos; grafo sem rótulo | seletor "Grafo, Mapa por sistemas, Visão consolidada" | alto (reorganiza) | M | alto |
| F10 | `consolidated-view.component.html:9` | "Editar texto e cores" não diz Mermaid | "Editar o Mermaid (texto e cores)" | alto (copy) | P | baixo |
| F11 | `flow-map-view.component.html:78-148` | Perguntas só dentro do mapa | atalho no painel | médio | M | alto |
| F12 | `node-panel.component.html:296-392` | Melhorias por último, depois de regras longas | faixa com contadores no topo do painel | médio | M | médio |
| F13 | `node-panel.component.html:297` | painel não diz o que dá para fazer | bloco de atalhos | alto (copy) | M | médio |
| F14 | `survey.page.html:98,110-142` | caminho da mudança só aparece na edição; "?" sem texto | "Como funciona" visível | baixo | P | médio |
| F15 | `survey.page.html:44-51` | nota da validação sem escala | "nota 82 de 100" | baixo | P | baixo |
| F16 | `survey.page.html:283` | aba de recolher o painel sem texto | rótulo "Painel" | baixo | P | médio |
| F17 | `node-panel.component.html:70,192,310` | IA ligada com o uso desligado: botão some sem explicação | dizer qual uso ligar | baixo | P | médio |
| F18 | `survey.page.html:320` | carregamento inicial só em texto | o mesmo indicador dos outros estados | baixo | P | baixo |
| F19 | `survey.page.html:237-258` | "Resumo/Completo" mostra o estado a aplicar; busca só com ícone | seletor; "Buscar" com texto | baixo | P | médio |
| F20 | `survey.page.html:122-128` | "Mais" da edição com uma ação só | "Descartar rascunho" | alto (remove) | P | baixo |
| F21 | `survey.page.html:59-133` | menus sem `role="menu"` | papéis de menu | baixo | P | baixo |
| F22 | `survey.page.html:18-32` | commit atrás de "Detalhes" | commit curto na linha | baixo | P | baixo |

Tela do banco (UXN-B; os itens que repetem o plano anterior citam o ID dele):

| ID | Onde | Problema | Sugestão | Risco | Esforço | Impacto |
|---|---|---|---|---|---|---|
| B01 | `app.html`, `app.routes.ts:20` | sem entrada no cabeçalho (UX-DB-09) | ver H01; sem repositório, levar a Configurações | alto (menu) | M | alto |
| B04 | `database.page.html:3` | "←" sempre vai à lista, nunca ao fluxo de origem (UX-FIM-B12) | voltar à origem | médio | M | alto |
| B05 | `database.page.html:50-55` | abas sem explicação | uma linha sob as abas | alto (copy) | P | médio |
| B06 | `database.page.html:73-78` | "Nenhum banco encontrado" sem ação (UX-FIM-C09) | "Analisar uma consulta mesmo assim" e "Conferir a pasta" | baixo | P | alto |
| B07 | `database.page.html:29,50,57-64` | repositório inexistente: abas e "Conectar banco vivo" visíveis no erro | esconder; link para Configurações | baixo | P | médio |
| B08 | `database.page.html:29` | "Conectar banco vivo" sem dizer o ganho | frase do ganho | alto (copy) | P | médio |
| B09 | `database.page.html:25,27` | "Nenhum banco é consultado" ao lado de "Conectar banco vivo" | uma frase só (UX-FIM-B07) | alto (copy) | P | médio |
| B10 | `query-analysis.component.html:60` | IA desligada some sem dizer como ligar (UX-FIM-Q01) | texto com link para Configurações | baixo | P | alto |
| B11 | `query-analysis.component.html:10-18` | "Analisar" desabilitado sem motivo; sem exemplo | "Usar um exemplo" | médio | P | médio |
| B12 | `query-analysis.component.html:25-29` | foco perdido depois de analisar (UX-FIM-Q02) | foco no título e contagem anunciada | médio | P | alto |
| B13 | `query-analysis.component.scss:6` | faixa vazia de 560 px em 1920 (UX-FIM-N02) | centralizar ou deixar a lateral crescer | baixo | P | baixo |
| B17 | `database.page.html:30-46` | "Exportar" não diz que exporta o mapa | "Exportar o mapa" | alto (copy) | P | baixo |
| B19 | `db-panel.component.html:152-176` | "levante o fluxo de novo" sem link | link para os levantamentos do repositório | baixo | P | médio |
| B20 | `node-panel.component.html:139-153` | "Analisar a consulta" repetido com o mesmo nome | `aria-label` com a tabela e a linha | baixo | P | médio |
| B23 | `live-connection-dialog.component.html:1,20-40` | campo abaixo das permissões; clique no fundo apaga a string (UX-FIM-A01, B03 a B06) | campo primeiro; não fechar com texto digitado | alto (reordena) | P | médio |
| B24 | `database.page.ts:212,334-337` | salvar ou remover a conexão apaga a consulta (UX-FIM-B01, B02) | manter o esquema até chegar o novo | médio | M | alto |
| B25 | `query-plan.component.html:12,49-55` | espera e erro da IA sem anúncio nem "Tentar de novo" | região viva e botão | baixo | P | médio |
| B26 | `database.page.html:18` | tipos do PostgreSQL cortados nos blocos (UX-FIM-N01) | tipo curto no bloco | médio | P | alto |
| B27 | mapa do banco (visto na tela) | sem chave estrangeira, as tabelas ficam numa coluna só e o resto do mapa fica vazio | distribuir em grade quando não há setas | médio | M | médio |

Os demais itens do plano [`plano-ux-2026-11-06-fim-plano-banco.md`](plano-ux-2026-11-06-fim-plano-banco.md) continuam valendo no código de hoje (os agentes conferiram) e seguem lá: acessibilidade da lista e do mapa do banco (B08 a B11, B13 a B20 do plano anterior), tokens e strings mágicas (B16 a B18, Q13, Q14, C15) e os textos de IA (Q07, Q15).

## Fases

Cada fase é commitável sozinha, com build, testes e prints em `docs/telas/`.

### Fase A: entradas e rótulos sem mudar a estrutura (baixo risco) `[x]`

O que tira o banco do esconderijo sem mexer no cabeçalho nem na barra.
- F01, F02: botão "Mapa do banco" no grupo das visões da barra do levantamento, no lugar do link de texto.
- F05: "Ver no mapa do banco" nos achados de banco, com `?tabela=`.
- F03: dica no `title` da etiqueta "banco" e link na legenda.
- H06, H14: o link do formulário vira botão secundário e o atributo é corrigido.
- H07: "Mapa do banco" e "Novo levantamento" em cada repositório das Configurações.
- B06, B07, B10, B19: ações nos estados vazio e de erro da tela do banco; IA desligada com o caminho para ligar.
- H03, H05, H11, H13, H15, H24, F14 a F19, F21, F22, B13, B20, B25: rótulos, estados, foco e acessibilidade.

Aceite: da tela inicial, o mapa do banco a 2 cliques por um botão com rótulo; de qualquer achado de banco, 1 clique até a tabela. Testes de componente para os links novos.

Feito em 2026-11-06: os itens acima, com a tela inicial lendo `?repositorio=` para pré-selecionar o repositório (as Configurações e o painel da tabela usam esse link). O botão "Mapa do banco" some durante a edição do fluxo, para não sair da tela com o rascunho aberto. Frontend 352/352. Prints em `docs/telas/ux-fase-a/`. Ficou para a Fase B: na tela do banco com repositório inexistente, o seletor mostra outro repositório.

### Fase B: comportamento (médio risco) `[x]`

- F04: seção "Banco neste fluxo (N tabelas)" na Visão geral do painel.
- F11, F12: atalho para Perguntas no painel; faixa de contadores (buracos, entre fluxos, melhorias) no topo.
- B04: o "←" do banco volta ao fluxo de origem.
- B11, B12, B24, B26, B27: exemplo na consulta, foco depois de analisar, conexão sem perder a consulta, tipos curtos e grade sem setas.
- H09, H10, H16, H17: primeiro uso guiado na tela inicial, índice nas Configurações e "Voltar" seguro.

Feito em 2026-11-06: os itens acima. O "←" do banco volta ao levantamento pelo parâmetro `?levantamento=`, que todos os links do fluxo para o banco passaram a mandar. O cadastro do repositório abre num diálogo na tela inicial e já escolhe o repositório. H17 já funcionava (entrada direta volta à tela inicial). Frontend 378/378. Prints em `docs/telas/ux-fase-b/`. Ficaram de fora: guardar o texto da consulta se o recarregamento do esquema falhar; o `node-panel.component.scss` passa do orçamento de 6 kB desde antes desta fase (dividir o painel ou subir estilos ao tema).

### Fase C: repaginação da navegação (alto risco, requer aprovação item a item) `[x]`

Proposta para o dono decidir. Antes de implementar, um protótipo navegável com as três telas (inicial, fluxo e banco) para aprovar o desenho. **Aprovada pelo dono em 2026-11-06** ("vamos executar todos até tudo terminar"), com o protótipo como referência e os botões dos cartões em grade de três colunas iguais (pedido dele sobre o protótipo). O item 6 não entra: o comando de build e de testes só se configura pelo terminal (ADR-003 e `CLAUDE.md`, Segurança); mudar isso pede uma ADR nova.

1. **Cabeçalho com as áreas do produto** (H01, H02, H04, B01): "Levantamentos", "Banco" e "Configurações" com rótulo e estado ativo; o selo da IA dentro de Configurações; paleta e tema numa seção Aparência das Configurações. "Banco" abre o último repositório usado (ou o único); com vários, a tela do banco já tem o seletor; sem nenhum, leva a Configurações. *Pergunta ao dono: aprova o cabeçalho com três áreas, e a paleta e o tema saindo do cabeçalho?*
2. **Tela inicial como ponto de partida** (H08, H21, H22): os repositórios com as ações "Levantar fluxo", "Mapa do banco" e "Analisar consulta", e a lista de levantamentos abaixo; "Novo levantamento" vira botão que abre o formulário quando já há levantamentos. *Pergunta: aprova a tela inicial organizada por repositório?*
3. **Barra do levantamento em grupos** (F07, F08, F09): seletor "Grafo, Mapa por sistemas, Visão consolidada"; "Mapa do banco" ao lado; "Comparar" e "Exportar (PNG, SVG, Mermaid, C4)" separados; "Editar fluxo" e "Validar" no grupo Alterar; status e contagem de nós na linha de metadados. *Pergunta: aprova os grupos e o seletor das visões?*
4. **Menus de uma ação só viram botão de texto** (F06, F20). *Pergunta: aprova "Excluir" e "Descartar rascunho" à vista, com confirmação?*
5. **Textos** (H23, H25, H26, F10, F13, B05, B08, B09, B17, B23 e os Q07, Q15 do plano anterior): cada texto novo vai no protótipo para o dono ler. *Pergunta: aprova os textos do protótipo?*
6. **Build e testes pela tela** (H18): campos "Comando de build" e "Comando de testes" no cadastro do repositório. Muda o que o cliente configura (o comando continua só da configuração, nunca da requisição). *Pergunta: quer isso nesta repaginação ou depois?*

Feito em 2026-11-06 (itens 1 a 5): cabeçalho com Levantamentos, Banco e Configurações (o Banco abre o último repositório usado, guardado no navegador), selo "IA: configurar", paleta e tema na seção Aparência das Configurações; tela inicial por repositório, com os três botões em grade de colunas iguais, os levantamentos recentes com "Abrir" e "Banco deste fluxo" e o formulário num diálogo; barra do levantamento em grupos (Ver, Compartilhar, Alterar), seletor Grafo, Mapa por sistemas e Visão consolidada, "Comparar" e "Exportar" separados, "Excluir levantamento…" à vista; atalhos "O que dá para fazer aqui" no painel; tela do banco com a frase única, a linha de cada aba e "Exportar o mapa"; diálogo do banco vivo com o campo antes das permissões. Frontend 398/398. Prints em `docs/telas/ux-fase-c/`. Ficou para depois: `survey.page.scss` e `node-panel.component.scss` passam do orçamento de 6 kB (subir o seletor segmentado e os grupos da barra para o tema); o atalho "Mapa do banco" do painel repete o botão da seção "Banco neste fluxo".

## Fora do escopo e riscos

- Identidade visual nova (marca, paletas) e o desenho do grafo ficam como estão.
- Mudar o cabeçalho e a barra quebra prints e testes de tela existentes e muda os vídeos já publicados da série; os prints de `docs/telas/` são refeitos na Fase C.
- O link "Banco" do cabeçalho precisa de um repositório "atual"; guardar o último usado no navegador é preferência local, sem efeito no servidor.
- Os itens de risco alto que o dono não aprovar continuam como proposta neste plano.
