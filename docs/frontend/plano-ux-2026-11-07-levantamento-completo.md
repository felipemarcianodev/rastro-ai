# Levantamento de UX completo: frontend depois dos acréscimos (2026-11-07)

**Status:** EXECUTADO em 2026-11-07 (o dono aprovou as três fases: "pode atacar os achados"). As Fases A e B (risco baixo e médio) seguem a regra do `CLAUDE.md` e podem ser executadas; a Fase C (risco alto) espera a aprovação do dono, item a item.
**Escopo:** o frontend inteiro, no fim do [plano dos acréscimos de UX](plano-ux-2026-11-07-acrescimos.md), como a skill `levantamento-ux` pede no fim de cada plano.
**Data:** 2026-11-07.
**Como:** três agentes de leitura (análise estática, uma fatia cada: tela inicial, Configurações e cabeçalho; fluxo, painel do passo e visões; banco, consulta e IA) e a verificação no navegador pelo coordenador (Chrome, paleta Papel no claro), com a API local em modo de produção, um `RASTRO_HOME` temporário vazio e as lojas fictícias dos testes (`TarefasRepo`, com um `.csproj` só para o levantamento). O MCP Knowledge não estava disponível nesta sessão; as etapas seguiram a própria skill.

## Cobertura

| Tela ou funcionalidade | Lida no código | Vista na tela | Larguras vistas |
|---|---|---|---|
| Cabeçalho, "Ir para o conteúdo", título da aba | sim | sim | 1920, 1280, 360 |
| Tela inicial no primeiro uso (sem repositório) | sim | sim | 1920 |
| Cadastro de repositório (diálogo) | sim | sim, com clique de verdade e cadastro feito | 1920 |
| Novo levantamento (diálogo) | sim | sim, sem repositório e com um; levantamento feito pela tela | 1920 |
| Tela inicial por repositório e lista de levantamentos | sim | sim | 1920, 360 |
| Fluxo: grafo, legenda, minimapa, painel do passo | sim | sim | 1920, 1280, 360 |
| Menu Exportar e diálogo C4 (DSL inteiro) | sim | sim, medido com `getBoundingClientRect` | 1920 |
| Mapa por sistemas, perguntas, regras, achados | sim | não (o fluxo de teste é pequeno e sem IA) | |
| Edição do fluxo, prompt e validação | sim | não nesta passada (vista nas fases dos acréscimos) | |
| Visão consolidada | sim | não nesta passada (vista na Fase F dos acréscimos) | |
| Tela do banco: mapa, lista, recorte, zoom | sim | sim | 1920, 360 |
| Banco vivo (diálogo), plano estimado | sim | o diálogo sim (Fase F); o plano não (pede banco de verdade) | |
| Analisar consulta e sugestão da IA | sim | a análise sim (Fase F); a sugestão não (pede chave de provedor) | |
| Configurações (repositórios, build e testes, MCP, IA, aparência) | sim | sim (Fase F) | 1920 |

O redimensionamento da janela do Chrome não teve efeito nesta máquina. As larguras 1280 e 360 foram vistas carregando a tela num `iframe` com essa largura, na mesma origem. A janela baixa foi a de 855 px de altura útil.

## Confiabilidade

- **Conferido pelo coordenador na tela:**
  - o primeiro uso;
  - os dois botões primários da tela inicial;
  - o foco em "Fechar" ao abrir o "Novo levantamento";
  - o cadastro sem aviso de sucesso;
  - o "Seguir chamadas HTTP para: o próprio repositório" com um repositório só;
  - o "10 nós" no cabeçalho do fluxo;
  - o título "Fluxo | Rastro AI" fixo;
  - a legenda aberta cobrindo o grafo em 1280 e em 360;
  - a barra "Mostrar" por cima da lista no banco em 360;
  - o diálogo C4 cabendo na janela;
  - o Esc fechando o C4;
  - o console sem erro.
- **Conferido no código pelos agentes, com a linha citada:** o resto da tabela de achados.
- **Estimado:** desempenho em banco e grafo grandes (sem medir com centenas de tabelas ou passos) e contraste do tema escuro (não medido).
- **Limite permanente:** sem usuário real nem dados de uso, os achados são prováveis, não comprovados.

## Descoberta

Cliques a partir da tela inicial, com um repositório cadastrado:

| Funcionalidade (`visao.md`) | Entrada | Cliques |
|---|---|---|
| Levantar um fluxo | "Levantar fluxo" no cartão do repositório, ou "Novo levantamento" | 1 |
| Mapa do banco | "Banco" no cabeçalho, ou "Mapa do banco" no cartão | 1 |
| Analisar uma consulta | "Analisar consulta" no cartão | 1 |
| Banco vivo | "Banco", depois "Conectar banco vivo" | 2 |
| Plano estimado | banco vivo conectado, depois "Analisar consulta" | 3 ou mais; sem conexão, a aba não diz que ele existe (UX-G-B04) |
| Mapa por sistemas e visão consolidada | abrir o levantamento, depois o seletor de visões | 2 |
| Perguntas sobre o fluxo | abrir o levantamento, depois o atalho do painel | 2 |
| Editar o fluxo, gerar o prompt e validar | abrir o levantamento, depois "Editar fluxo" | 2 ou mais |
| Comparar e exportar (PNG, SVG, Mermaid, C4) | abrir o levantamento, depois a barra | 2 a 3 |
| IA (modo provedor) e MCP | "Configurações", ou o selo "IA: configurar" | 1 |
| Build e testes da validação | "Configurações", depois "Editar build e testes" | 2 |
| Não resolvidos | abrir o levantamento; só o canvas leva a eles (UX-G-F03) | 2 ou mais |

Tudo se alcança por botão com rótulo. O ponto fraco de descoberta é o plano estimado e os não resolvidos fora do grafo.

## Diagnóstico

### Métricas medidas

| Tela | Elementos visíveis | Botões primários visíveis |
|---|---|---|
| Tela inicial, primeiro uso (1920) | 36 | 2 ("Novo levantamento" e "Cadastrar repositório") |
| Tela inicial com um repositório (1920) | não medido | 2 ("Novo levantamento" e um "Levantar fluxo" por cartão) |
| Diálogo C4 com o DSL inteiro | 807 px de altura numa janela de 855 px, corpo com rolagem, rodapé visível | 1 ("Baixar workspace.dsl") |

### Os 10 principais problemas, por impacto

1. **UX-G-L01:** mais de um botão primário na tela inicial, e o mesmo ato com três nomes ("Novo levantamento", "Levantar fluxo", "Levantar o fluxo").
2. **UX-G-N02:** no primeiro uso, a tela diz "Seus repositórios" sem nenhum, e os três passos de boas-vindas ficam dentro de "Levantamentos recentes".
3. **UX-G-L03:** os diálogos abrem com o foco em "Fechar", não no primeiro campo.
4. **UX-G-N06:** a legenda do fluxo abre por padrão e cobre quase metade do grafo em 1280 e os controles em 360.
5. **UX-G-F10:** a visão consolidada perde o texto e as cores editados ao sair sem salvar, sem aviso.
6. **UX-G-F06:** arrastar um passo grava a posição sem desfazer.
7. **UX-G-B04:** sem banco vivo, a aba de consulta não diz que existe o plano estimado.
8. **UX-G-B03:** com erro ao ler o esquema, a aba "Analisar consulta" some, embora a análise funcione sem esquema.
9. **UX-G-F01:** no grafo afastado, as marcas ✓ e ? somem, e o daltônico perde a segunda pista da confiança.
10. **UX-G-L04:** um clique no fundo do "Novo levantamento" descarta o que foi digitado.

## Achados

IDs: L (tela inicial, Configurações e cabeçalho), F (fluxo, painel e visões), B (banco, consulta e IA) e N (só no navegador).

| ID | Onde | Problema | Sugestão | Risco | Esforço | Impacto |
|---|---|---|---|---|---|---|
| UX-G-L01 | `surveys.page.html:7, 27, 59, 269` | Vários `btn-p` na tela inicial; o mesmo ato com três rótulos | Um primário por contexto (cartões em `btn-g`); um rótulo só | Alto (texto) | P | Alto |
| UX-G-L02 | `surveys.page.html:15-66` | Erro ou carga da lista de repositórios não aparece na tela inicial; o vazio diz "acima" sem nada acima | Ramo de erro com "Tentar de novo", esqueleto dos cartões e texto sem "acima" | Baixo | P | Alto |
| UX-G-L03 | `core/dialog.directive.ts:36` | O foco inicial vai ao "Fechar" do cabeçalho do diálogo (só `.btn-icon` é pulado) | Preferir o primeiro campo ou `[autofocus]`; marcar o fechar com `data-dialog-close` | Médio | P | Alto |
| UX-G-L04 | `surveys.page.ts:199`, `.html:128` | Clique no fundo fecha o "Novo levantamento" e descarta o digitado | Não fechar pelo fundo com dado digitado, como no banco vivo (B03) | Médio | P | Alto |
| UX-G-L05 | `settings.page.html:12-17, 119, 149, 160` | O índice "Nesta página" não segue a ordem da página (Aparência no meio) | Aparência por último | Alto (reordena) | P | Médio |
| UX-G-L06 | `settings.page.html:58, 240` | Dois `btn-p` nas Configurações ("Salvar comandos" e "Salvar configuração") | "Salvar comandos" em `btn-g` | Baixo | P | Médio |
| UX-G-L07 | `settings.page.html:167` | Só ligar e desligar a IA avisa "Alteração pendente"; provedor, modelo, chave e usos não | `dirty()` com todos os campos | Baixo | M | Médio |
| UX-G-L08 | `settings.page.scss:8` | Configurações em 820 px; em 1920 sobram 550 px de cada lado e o cartão do repositório empilha tudo | Largura `--reading-w` e o cartão do repositório em duas colunas | Médio | M | Médio |
| UX-G-L09 | `surveys.page.html:90` | O resumo da linha (status, nós, erro, data) tem `aria-hidden`; o leitor de tela perde o estado | `aria-describedby` do "Abrir" para o resumo | Baixo | P | Médio |
| UX-G-L10 | `app.html:11-13` | O selo "IA: configurar" parece ação e esconde o estado no `title` | "IA desligada", "IA sem chave" e "IA: Claude", com `aria-label` | Alto (texto) | P | Médio |
| UX-G-L11 | `app.html:6-8`, `surveys.page.html:4` | Três nomes para a tela inicial (menu "Levantamentos", aba "Levantamentos", h1 "Seus repositórios") | Alinhar os nomes | Alto (texto) | P | Médio |
| UX-G-L12 | `app.ts:62-64, 97-100` | "Banco" sem repositório leva às Configurações sem aviso; erro da API some em silêncio | Ir à tela inicial (primeiro uso) ou avisar nas Configurações | Médio | M | Médio |
| UX-G-L13 | `surveys.page.html:166-167` | Sem repositório, o formulário mostra um `select` desabilitado com duas saídas para o mesmo problema | Um vazio com um só "Cadastrar repositório" | Baixo | P | Médio |
| UX-G-L14 | `surveys.page.html:52-57` | Os três passos põem a IA no meio; depois do cadastro, nada diz "cadastrado" (visto na tela) | IA como nota; aviso "Repositório X cadastrado" acima do formulário | Alto (texto) | P | Médio |
| UX-G-L15 | `settings.page.html:53` | O "Comandos salvos" aparece longe da linha editada; a ajuda fala em "shell" e "redirecionamento" | Aviso junto da linha e ajuda mais simples | Baixo | P | Médio |
| UX-G-L16 | `settings.page.html:84-111`, `surveys.page.html:278-314` | Dois formulários de cadastro iguais; o do diálogo não explica a regra do nome | Componente único com a regra do nome na ajuda | Baixo | M | Médio |
| UX-G-L17 | `surveys.page.html:69-70, 177` | "Solution" e "método" na busca e no formulário, sem sentido para o Angular | "Ponto de entrada", "projeto", "Solution ou tsconfig" | Alto (texto) | P | Baixo |
| UX-G-L18 | `surveys.page.html:140-246` | A ajuda vem antes do campo em uns e depois em outros; o mesmo texto três vezes (ajuda, `title` e "i") | Ajuda sempre depois e sem `title` nos "i" | Baixo | P | Baixo |
| UX-G-L19 | `settings.page.html:160-171` | A IA tem três nomes ("Modo provedor", "Inteligência artificial", "IA") | Um nome só; foco no h2 ao chegar por `#ia` | Alto (texto) | P | Baixo |
| UX-G-L20 | `settings.page.html:234-235` | `disabled` redundante no orçamento; "UTC" sem a hora local | Tirar o `disabled`; "zera à meia-noite UTC (21h em Brasília)" | Baixo (código) e alto (texto) | P | Baixo |
| UX-G-L21 | `settings.page.html:122-137` | A troca de paleta não é anunciada | `role="status"` com a paleta aplicada | Baixo | P | Baixo |
| UX-G-L22 | `confirm-dialog.component.ts:20-24` | Confirmação destrutiva sem o nome do alvo; o foco em "Cancelar" depende da ordem do DOM | Título e botão com o nome; foco fixo em "Cancelar" | Baixo | P | Baixo |
| UX-G-L23 | `app.routes.ts:11, 16` | Título da aba fixo no fluxo e na visão consolidada (visto: "Fluxo \| Rastro AI") | O ponto de entrada no título, como o "Banco de :nome" | Baixo | M | Baixo |
| UX-G-L24 | `surveys.page.html:116-118` | "Abrir" e "Banco deste fluxo" em toda linha, que já é link | Só "Abrir" à vista | Alto (remove) | P | Baixo |
| UX-G-L25 | `core/dialog.directive.ts:63-65` | `offsetParent` tira do ciclo de Tab elemento com `position: fixed` (risco latente) | `getClientRects().length > 0` | Baixo | P | Baixo |
| UX-G-F01 | `theme/_flow.scss:366` | No grafo afastado (`.is-far`), as marcas ✓ e ? somem com o rótulo | Esconder só o texto e ampliar a marca | Baixo | P | Alto |
| UX-G-F02 | `flow-canvas.component.html:40-52, 189-191` | A origem da ligação (agente, provedor, manual) não aparece no grafo, só no painel | Marca por origem na ligação e na legenda | Médio | M | Alto |
| UX-G-F03 | `survey.page.html:26`, `flow-canvas.component.html:155-159` | Os não resolvidos só se acham no canvas; no cabeçalho a contagem é texto morto | A contagem vira botão que leva ao próximo buraco | Médio | P | Alto |
| UX-G-F04 | `survey.page.html:25, 83` | "nó" no cabeçalho e no Comparar (visto: "10 nós") | "passo" | Alto (texto) | P | Médio |
| UX-G-F05 | `flow-canvas.component.html:192`, `node-panel.component.html:405` | "nó" na legenda e no vazio das regras | "passo" | Alto (texto) | P | Médio |
| UX-G-F06 | `flow-canvas.component.ts:710` | Arrastar um passo grava a posição sem desfazer | Pilha de posições com Ctrl+Z, ou aviso "Passo movido. Desfazer" | Médio | M | Alto |
| UX-G-F07 | `flow-canvas.component.html:129`, `_flow.scss:432-450` | Minimapa fixo de 180 por 120; não resolvido só por cor | Contorno no não resolvido; tamanho proporcional | Médio | M | Médio |
| UX-G-F08 | `flow-canvas.component.html:40-46` | Quatro conectores por passo mesmo fora da edição; layout de grafo grande sem medida | Conectores só na edição; medir o layout com 200 passos | Médio | M | Médio |
| UX-G-F09 | `survey.page.html:125`, `survey.page.ts:112` | A dica de como ligar passos fica fechada na primeira edição e não cita o teclado | Abrir na primeira vez e citar o "Ligar a…" do painel | Alto (mostra conteúdo) | P | Médio |
| UX-G-F10 | `diagram.page.html:35-45`, `diagram.page.ts:91` | A visão consolidada perde a edição ao sair sem salvar | Guarda de rota e `beforeunload` com "Salvar, sair ou ficar" | Médio | M | Alto |
| UX-G-F11 | `survey.page.html:106-113` | "Excluir levantamento…" colado ao "Editar fluxo"; "Validar mudança" em dois lugares | Excluir para outro lugar; validar num lugar só | Alto (reordena) | M | Médio |
| UX-G-F12 | `flow-canvas.component.html:160-163` | "Resumo do fluxo" (filtro) colide com o resumo em texto do painel | "Só o método de entrada" | Alto (texto) | P | Médio |
| UX-G-F13 | `flow-canvas.component.html:115` | O glifo de recolher é pequeno e só por mouse; as teclas + e − só no `aria-label` | Área de 24 px e as teclas na ajuda do grafo | Baixo | P | Médio |
| UX-G-F14 | `flow-canvas.component.html:140, 211-212` | Os atalhos (Alt+setas, setas, Ctrl+F) só na legenda e na ajuda escondida | Linha curta de atalhos na primeira visita | Médio | P | Médio |
| UX-G-F15 | `validation-dialog.component.html:16` | O texto cita o "botão Validação", que se chama "Última validação" | Nome certo do botão | Alto (texto) | P | Baixo |
| UX-G-F16 | `validation-dialog.component.html:5`, `c4-dialog.component.html:6` | Nome de ferramenta do MCP (`check_change`, `get_c4`) no texto do cliente | "Um agente conectado obtém o mesmo resultado" | Alto (texto) | P | Baixo |
| UX-G-F17 | `c4-dialog.component.html:70-92` | O comando do Structurizr só vale no cmd do Windows (`%CD%`); o download fica ativo com pessoas não salvas | Variante `$PWD` e download travado com `unsaved()` | Baixo | P | Médio |
| UX-G-F18 | `flow-map-view.component.html:34-82` | O mapa por sistemas não tem zoom, minimapa nem busca | Reaproveitar o `diagram-stage` | Médio | G | Médio |
| UX-G-F19 | `diagram.page.html:32, 47, 73-78` | "Gerar de novo" com o mesmo rótulo e consequências diferentes | "Atualizar pelo fluxo atual" no aviso de desatualizado | Alto (texto) | P | Baixo |
| UX-G-F20 | `diagram.page.html:84-86` | O texto Mermaid sem ajuda do formato | `field-help` curta e `aria-describedby` | Baixo | P | Baixo |
| UX-G-F21 | `_flow.scss:340-350` | Marca da ligação com 10 px em círculo de 14 px | 16 px, com `aria-label` | Baixo | P | Baixo |
| UX-G-F22 | `survey.page.html:238-258` | A busca do grafo só mostra "n de m", sem lista nem dica do Esc | Lista curta de resultados e "Esc fecha" | Médio | M | Baixo |
| UX-G-F23 | `survey.page.html:359-370` | O aviso do que vai à IA no prompt não fica no mesmo lugar dos outros | `app-ai-disclosure` logo depois de cada botão de IA | Baixo | P | Baixo |
| UX-G-B01 | `database.page.html:185-222` | Banco grande: ao escolher a tabela o foco fica no cartão e o painel vem depois de centenas de cartões | Foco no título do painel; "Fechar" volta ao cartão | Médio | M | Alto |
| UX-G-B02 | `db-panel.component.html:89-99`, `database.page.ts:376-380` | Clicar em "A view X lê esta tabela" deixa o foco no `body` | Foco no título do painel novo | Baixo | P | Alto |
| UX-G-B03 | `database.page.html:55-80, 226-229` | Com erro no esquema, a aba de consulta some | Manter as abas e a análise sem esquema | Médio | M | Alto |
| UX-G-B04 | `query-analysis.component.html:102-105` | Sem banco vivo, nada diz que o plano estimado existe | Linha discreta com "Conectar banco vivo" | Médio | P | Alto |
| UX-G-B05 | `query-analysis.component.ts:150-156` | "Editar a consulta" apaga a sugestão da IA, que custou tokens | Guardar a última sugestão por consulta, ou avisar | Médio | M | Médio |
| UX-G-B06 | `database.page.html:176-183` | Cada seta do mapa tem um "1:N" lido pelo leitor de tela | `aria-hidden` nos rótulos; relações no `aria-label` do cartão | Baixo | P | Médio |
| UX-G-B07 | `database.page.ts:44-46` | Zoom de 50% a 150%, sem "ajustar à tela" | Botão "Ajustar" e mínimo de 25% | Médio | M | Médio |
| UX-G-B08 | `database.page.scss:201-219` | Em 360, a lista inteira vem antes do mapa | Altura máxima de 40vh com rolagem | Baixo | P | Médio |
| UX-G-B09 | `query-plan.component.html:11-13` | A região viva do plano nasce com o texto | Região fixa e "Pedindo o plano…" anunciado | Baixo | P | Médio |
| UX-G-B10 | `query-analysis.component.ts:136-139` | Depois de "Sugerir melhora", só rola; o foco fica no botão | Foco no título da sugestão (e do erro) | Baixo | P | Médio |
| UX-G-B11 | `query-analysis.component.html:66-94` | Enquanto o estado da IA carrega, aparece "ligue o uso de sugestões" por um instante | Não mostrar nada enquanto carrega | Baixo | P | Médio |
| UX-G-B12 | `database.page.html:149-155` | O recorte desabilitado só explica no `title`; "N fora do recorte" sem saída | Texto visível e "Mostrar tudo" | Baixo | P | Médio |
| UX-G-B13 | `database.page.html:56-64`, `database.page.ts:353-356` | Abas sem Home e End | Home e End; quebra do texto de apoio em 480 px | Baixo | P | Baixo |
| UX-G-B14 | `database.page.html:36-50` | O menu Exportar não foca o primeiro item nem fecha ao sair com Tab | Foco no primeiro item e fechar no `focusout` | Médio | M | Baixo |
| UX-G-B15 | `database.page.html:17-31` | Barra do banco com promessa repetida ("só leitura" duas vezes) | Promessa numa linha sob as abas | Alto (texto e remove) | P | Médio |
| UX-G-B16 | `live-connection-dialog.component.html:20-25` | A string de conexão em campo de senha sem "Mostrar" | Botão "Mostrar" com `aria-pressed` | Médio | M | Baixo |
| UX-G-B17 | `live-connection-dialog.component.html:34-36` | Depois de salvar, o foco fica no "Salvar" desabilitado e nada sugere o teste | Foco em "Testar conexão" com a frase do próximo passo | Baixo | P | Baixo |
| UX-G-B18 | `database.page.html:96-110` | Banco grande: a lista inteira no DOM e "N itens encontrados" anunciado a cada pausa | `content-visibility: auto`; anúncio com atraso maior | Baixo | P | Médio |
| UX-G-B19 | `database.page.html:76-80` | A carga do esquema é uma frase solta | O mesmo estado de carga das outras telas | Baixo | P | Baixo |
| UX-G-B20 | `db-panel.component.html:34-61` | Tabela de campos sem cabeçalho fixo; "Nulo" e "Chave" sem explicação | `thead` fixo e `title` nos cabeçalhos | Baixo | P | Baixo |
| UX-G-B21 | `theme/_database.scss:30-47` | Cartão esmaecido a 0,35 de opacidade, abaixo de 3:1 (não medido) | Medir; se falhar, `--ink-soft` no lugar da opacidade | Baixo | P | Baixo |
| UX-G-B22 | `database.page.scss:22-37` | `::before` do selo do banco vivo sem `content` (regra morta) | Ponto com `content: ''`, ou apagar a regra | Baixo | P | Baixo |
| UX-G-B23 | `query-analysis.component.html:46-58` | "Ver no mapa" troca de aba sem caminho de volta à análise | "Voltar à análise" no painel quando se chegou por aqui | Médio | M | Baixo |
| UX-G-B24 | `query-analysis.component.html:34` | O trecho marcado na consulta depende só de cor | Sublinhado diferente por gravidade | Baixo | P | Baixo |
| UX-G-B25 | `db-panel.component.html:154-155` | "Levantar o fluxo deste repositório" sem volta garantida ao banco | Passar a origem para o "←" voltar ao banco | Médio | M | Baixo |
| UX-G-N02 | tela inicial, primeiro uso (visto) | "Seus repositórios" sem nenhum, e os passos de boas-vindas dentro de "Levantamentos recentes" | Sem repositório, um bloco só de primeiro uso, sem a seção "Levantamentos recentes" | Alto (reordena) | P | Alto |
| UX-G-N04 | diálogo "Novo levantamento" (visto) | "Seguir chamadas HTTP para: o próprio repositório" com um repositório só | Com um repositório, a opção vira uma frase ou some | Alto (remove) | P | Baixo |
| UX-G-N05 | tela do banco em 360 (visto) | A barra "Mostrar" fica por cima da lista de tabelas | Corrigir o empilhamento abaixo de 900 px | Baixo | P | Médio |
| UX-G-N06 | fluxo em 1280 e 360 (visto) | A legenda abre por padrão e cobre quase metade do grafo; em 360, "Esconder legenda" fica sobre o "Buscar" | Legenda fechada por padrão (ou em painel lateral) e sem sobrepor os controles | Alto (esconde conteúdo) | P | Alto |

**Total:** 77 achados: 35 de risco baixo, 22 de risco médio e 20 de risco alto (o L20 conta como baixo na parte do código; a parte do texto entra na Fase C).

## Fases

### Fase A: estados, foco, anúncios e tema (risco baixo)

- [x] UX-G-L02, L06, L07, L09, L13, L15, L16, L18, L20 (só o código), L21, L22, L23 e L25.
- [x] UX-G-F01, F13, F17, F20, F21 e F23.
- [x] UX-G-B02, B06, B08, B09, B10, B11, B12, B13, B17, B18, B19, B20, B21, B22 e B24.
- [x] UX-G-N05.
- **Aceite:** um primário por tela nas Configurações; nenhum foco no `body` depois de ação no banco; toda região de resultado com `role="status"` fixa; em 360, sem sobreposição na tela do banco; clique de verdade em cada botão novo.
- **Métricas-alvo:** 0 erro de console; 0 caso de foco no `body` nas ações do banco e da consulta.
- **Registro (2026-11-07):** feito. O cadastro de repositório virou um componente só (`app-register-repository`), usado nas Configurações e no diálogo da tela inicial, com a regra do nome na ajuda e o aviso de cadastrado. O título da aba do fluxo e da visão consolidada traz o ponto de entrada. O cartão esmaecido do mapa do banco tinha 2,1:1 de contraste no Papel claro e 2,8:1 no escuro; com `--ink-soft` no lugar da opacidade, passou a 4,7:1 e 6,9:1. A causa da barra "Mostrar" por cima da lista em 360 era a lista encolhendo na coluna flex. O token `--edge-mark-size` (16 px) entrou no `_tokens.scss`. Frontend 495/495.

### Fase B: comportamento novo (risco médio)

- [x] UX-G-L03, L04, L08 e L12.
- [x] UX-G-F02, F03, F06, F07, F08, F10, F14, F18 e F22.
- [x] UX-G-B01, B03, B04, B05, B07, B14, B16, B23 e B25.
- **Aceite:**
  - o diálogo abre no primeiro campo;
  - clique no fundo não apaga o digitado;
  - sair da visão consolidada com edição pergunta antes;
  - arrastar um passo se desfaz;
  - a origem da ligação aparece no grafo;
  - com erro no esquema, a análise de consulta continua alcançável;
  - sem banco vivo, a aba diz que o plano estimado existe.
- **Riscos:**
  - o F06 precisa conviver com o Ctrl+Z da edição;
  - o L03 muda o foco inicial de todos os diálogos e precisa conferir os que abrem sem campo (confirmações).
- **Registro (2026-11-07):** feito. O F08 ficou em parte: o Foblex desenha a ligação pelo conector, então fora da edição só ficam os conectores que alguma ligação usa (na edição, os quatro de cada passo); o layout de 200 passos em cadeia levou 4 ms. O "Sair sem salvar?" da visão consolidada é uma guarda de rota (`core/leave-guard.ts`) com o diálogo do tema, e o `app-confirm-dialog` ganhou o rótulo do cancelar. O desfazer das posições guarda até 50 estados e só vale fora da edição. O "Levantar o fluxo deste repositório" do banco abre o formulário com "Voltar ao banco". Frontend 534/534.

### Fase C: alto risco, requer aprovação do dono

Cada item tem a pergunta a fazer ao dono.

- [x] **UX-G-L01:** um primário por contexto na tela inicial e um rótulo só para levantar. Pergunta: "Novo levantamento" no topo como único primário, e os cartões com "Levantar fluxo" em botão comum?
- [x] **UX-G-N02:** sem repositório, a tela inicial vira um bloco só de primeiro uso. Pergunta: tirar "Seus repositórios" e "Levantamentos recentes" até o primeiro cadastro?
- [x] **UX-G-N06:** a legenda do fluxo fechada por padrão. Pergunta: fechada na primeira visita, com o botão "Legenda" à vista?
- [x] **UX-G-L14:** a IA sai dos três passos e vira nota, e o cadastro mostra "Repositório X cadastrado". Pergunta: pode mudar o texto?
- [x] **UX-G-F04 e F05:** "nó" vira "passo" no cabeçalho, no Comparar, na legenda e nas regras. Pergunta: pode trocar?
- [x] **UX-G-L10 e L11:** o selo da IA diz o estado ("IA desligada") e a tela inicial tem um nome só. Pergunta: qual nome para a tela inicial, "Levantamentos" ou "Repositórios"?
- [x] **UX-G-L05:** Aparência por último nas Configurações. Pergunta: pode reordenar?
- [x] **UX-G-F09:** a dica da edição aberta na primeira vez. Pergunta: pode mostrar sozinha uma vez?
- [x] **UX-G-F11:** "Excluir levantamento…" longe do "Editar fluxo" e "Validar mudança" num lugar só. Pergunta: para onde vai o Excluir?
- [x] **UX-G-F12, F15, F16 e F19:** textos do filtro "Só o método de entrada", do botão "Última validação", sem nome de ferramenta do MCP e "Atualizar pelo fluxo atual". Pergunta: pode mudar os quatro textos?
- [x] **UX-G-L17, L19 e L20 (texto):** "Solution ou tsconfig", um nome só para a IA nas Configurações e a hora local do orçamento. Pergunta: pode mudar?
- [x] **UX-G-L24 e N04:** tirar "Banco deste fluxo" da linha da lista e o "Seguir chamadas HTTP" com um repositório só. Pergunta: pode remover os dois?
- [x] **UX-G-B15:** a promessa da barra do banco numa linha sob as abas. Pergunta: pode mover?
- **Registro (2026-11-07):** feito, com as escolhas do coordenador nos itens que pediam decisão:
  - a tela inicial se chama "Levantamentos" (como o menu e a aba), com os h2 "Repositórios" e "Levantamentos recentes", e "Novo levantamento" é o único primário;
  - sem repositório (e sem levantamento guardado), a tela é só o bloco "Boas-vindas ao Rastro", com dois passos e a IA como nota;
  - o "Excluir levantamento…" foi para o fim da "Visão geral" do painel, e o "Validar mudança" ficou só na barra de edição (validar um rascunho salvo pede entrar em "Editar fluxo");
  - o "Seguir chamadas HTTP" com um repositório só não sumiu, porque a opção serve à chamada dentro do próprio repositório: virou um checkbox só;
  - o selo da IA diz "IA desligada", "IA sem chave" ou "IA: <provedor>".
- **Conferido no Chrome**, com a API em produção e um `RASTRO_HOME` vazio: primeiro uso, cadastro pela tela com o aviso de cadastrado, levantamento, legenda fechada, "10 passos", título da aba com o ponto de entrada e a promessa do banco sob as abas. A conferência achou três coisas, corrigidas nesta fase:
  - o exemplo de pasta do cadastro saía sem as barras ("C:codigomeu-sistema"), porque o template do componente da Fase A mora no `.ts`;
  - a linha da lista ainda dizia "nós";
  - o h2 "Repositórios" ficava longe do cartão.

  A linha de atalhos some abaixo de 600 px, onde cobria os controles do grafo. Prints em `docs/telas/ux-levantamento/`. Frontend 559/559.

## Fora do escopo

- O multi-idioma (Fase 15 do `plano-mvp.md`): vem depois que os textos da Fase C estabilizarem.
- A virtualização do mapa do banco e do grafo: só se a medida da Fase B (F08, B18) passar do limite.
- O plano estimado e a sugestão da IA na tela: pedem banco de verdade e chave de provedor; ficam para a próxima passada com esses recursos.

## Riscos

- **UX-G-L03** muda o foco inicial de todos os diálogos, e os testes que esperam o foco no primeiro botão mudam junto.
- **UX-G-F06** e o Ctrl+Z da edição usam o mesmo atalho: o desfazer das posições só vale fora da edição.
- **UX-G-F04 e F05** trocam texto que os testes procuram.
- **UX-G-N06** esconde a legenda por padrão. Quem nunca abriu o Rastro perde a explicação das cores, então o botão "Legenda" precisa ficar à vista.
