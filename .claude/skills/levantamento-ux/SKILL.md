---
name: levantamento-ux
description: Faz o levantamento de melhorias de UX e UI do frontend do Rastro AI (análise estática do código, sem usuário real) e gera um PLANO FASEADO em `docs/frontend/` pronto para a skill `executar-plano-faseado`. Use quando o usuário pedir "analise a UX", "levante melhorias de UX", "auditoria de UX", ao fim de uma fase com mudança grande no frontend, e sempre no fim de um plano.
---

# Levantamento de UX para plano faseado

Adaptada da skill de mesmo nome do `meu-portfolio` (a que gerou o plano de UX do blog). Revista na Fase 17 (2026-10-17), quando o dono olhou a tela como cliente e achou dado dele no produto, tela sem volta e diálogo passando da tela, nada disso apontado pelos levantamentos anteriores. Revista de novo em 2026-11-06, quando o dono não achou a tela do banco: ela existia desde a Fase 38, mas a entrada era um link pequeno na barra do levantamento, e nenhum levantamento contou quantos cliques e quanta procura levavam até ela. A passada de navegação e a tabela de cobertura vieram da skill do `meu-portfolio`.

## Antes de começar (bloqueante)
1. Se o MCP Knowledge estiver disponível, ler `mcp__knowledge__get_pattern("ux-refactor-process")` (inventário, classificação, lint UX, plano com aprovação, aplicação por risco, métricas antes e depois). Fora do ar: avisar o dono e seguir as etapas abaixo.
2. Ler o `CLAUDE.md`, `docs/arquitetura/visao.md` e o design system do frontend (`docs/frontend/design-system.md`, quando existir).
3. Escopo: as telas tocadas pela fase, ou o frontend inteiro no fim do plano. **Descobrir as telas pelo código, nunca por lista fixa:** as rotas do `app.routes.ts`, os links do cabeçalho e as funcionalidades da `visao.md` (cada item de "o que o Rastro faz" precisa ter um caminho até a tela). Conferir também os planos de UX anteriores (`docs/frontend/plano-ux-*.md`) no código de hoje: item "esperando aprovação" pode ser justamente o problema que o dono sentiu.

## Coleta (paralela, só leitura)
Até 3 agentes `Explore` (sonnet), cada um com uma fatia de telas, por exemplo: (1) lista de levantamentos e painel do nó; (2) canvas do grafo (visualização, edição, drag and drop); (3) prompt, validação, sugestões, C4 e feature flags. Cada um devolve linhas no formato:

`ID | tela/arquivo:linha | problema | evidência | sugestão concreta | risco | esforço (P/M/G) | impacto`

Checagens obrigatórias:
- **Grafo:** legibilidade com 50+ nós (zoom, minimapa, foco no caminho selecionado), contraste das cores de `confidence` (extraído, inferido, confirmado) também para daltônicos (cor + ícone ou traço), não resolvidos fáceis de achar.
- **Drag and drop:** affordance visível, alvo de arraste e de soltura claros, desfazer à mão, operação equivalente por teclado.
- **IA plugável:** fica claro na tela de onde veio cada aresta (agente, provedor, manual) e se a IA está desligada; sem IA, a tela não mostra botão morto.
- Estados de carregando, vazio e erro; feedback depois de cada ação (toast, confirmação).
- Consistência entre telas (cabeçalho, botões, modais, toasts); um botão primário por tela.
- Acessibilidade (aria-label, label associado, foco visível, ordem de tabulação, contraste) e larguras 1280 e 1920 (é uma ferramenta de desktop; 768 só sem quebrar).
- Desempenho percebido em grafos grandes (layout, re-render, virtualização).

- **Olhar de cliente** (o Rastro é produto, ADR-002): nada da máquina do dono na tela. Procurar no código de tela (`frontend/src`, fora dos specs) e nas descrições do MCP nomes de repositórios e classes reais e caminhos da máquina em placeholder, exemplo e mensagem de erro (o `node scripts/verificar-publico.mjs` acha os da lista privada); trocar por exemplo genérico (`PedidosController.Criar`, `C:\codigo\meu-sistema`). E também o que só se faz pelo terminal e o cliente esperaria na tela.
- **Primeiro uso:** subir com `RASTRO_HOME` numa pasta temporária vazia (sem repositório, sem levantamento, sem IA) e conferir que cada tela diz o que fazer em seguida.
- **Ida e volta:** toda tela se alcança pelo cabeçalho ou por um link visível e tem como voltar (botão "Voltar" ou o caminho de origem), sem depender do botão do navegador.
- **Descoberta (navegação):** para cada funcionalidade da `visao.md`, contar quantos cliques ela leva a partir da tela inicial e se a entrada é achável por quem não sabe que ela existe: item no cabeçalho ou botão com rótulo, não link pequeno no meio de outro texto, ícone sem rótulo ou ação só no hover. Funcionalidade principal que só se acha por link de texto numa barra de outra tela é achado de impacto alto. Ver também quantos itens ficam à vista no cabeçalho e se os nomes e grupos fazem sentido.
- **Aparência:** hierarquia visual, densidade, alinhamento, espaço em branco, largura aproveitada (área vazia grande em 1920), estado ativo visível, ícones repetidos e consistência entre telas parecidas.

## Verificação no navegador (obrigatória, além da análise estática)
A análise do código não pega diálogo cortado, clique que não chega ao botão nem funcionalidade que ninguém acha. Com o Rastro no ar e dados reais (API local com `RASTRO_HOME` temporário e repositórios públicos, como as lojas fictícias dos testes; nunca o `~/.rastro` do dono):
- **Como cliente que chega agora:** da tela inicial, tentar chegar a cada funcionalidade só pelo que está à vista, anotando os cliques e onde a procura travou. Print de cada tela, em 1280, 1920 e 360 (se o navegador não redimensionar, registrar que a largura não foi vista).
- **Clique de verdade**, nunca `element.click()` por JavaScript (ver `CLAUDE.md`). Para não errar o alvo, clique pelo `ref` do `find` ou do `read_page`, não por coordenada lida de print reduzido. Botão com efeito (levantar de novo, excluir, validar) só pelo `ref`, e o que for criado por engano é apagado e dito ao dono.
- **Diálogos com conteúdo real e grande** (C4 com o DSL inteiro, prompt longo, validação com muitos itens) nas larguras 1280 e 1920 e numa janela baixa: medir com `getBoundingClientRect` que o `.dialog` cabe no `.overlay`, que só o corpo rola e que o rodapé fica visível. Foi assim que o diálogo do C4 apareceu com 1870px de altura numa janela de 911px.
- **Console** sem erro em cada tela visitada.

Métricas por tela: elementos visíveis simultâneos, profundidade máxima do DOM, nº de botões primários visíveis.

## Classificação de risco
- **Baixo:** espaçamento, tipografia, agrupamento, token do tema no lugar de CSS solto, affordance, feedback, estados, aria e labels.
- **Médio:** novo comportamento de interação (atalho, foco, minimapa), novos estados.
- **Alto (aprovação item a item):** esconder conteúdo atrás de toggle, reordenar seções ou menu, mudar copy, remover elemento.

## Saída: `docs/frontend/plano-ux-AAAA-MM-DD.md`
1. **Status** (PROPOSTA), escopo, data e **cobertura**: tabela com cada tela e funcionalidade, marcando o que foi lido no código, o que foi visto na tela (e em que larguras) e o que ficou de fora e por quê.
2. **Confiabilidade:** o que o coordenador conferiu no código ou na tela, e o que é estimativa. Limite permanente: sem usuário real nem dados de uso, os achados são prováveis, não comprovados.
3. **Descoberta:** tabela com cada funcionalidade da `visao.md`, a entrada dela e quantos cliques leva da tela inicial.
4. **Diagnóstico:** métricas por tela e os 10 principais problemas por impacto.
5. **Achados:** tabela completa com IDs (só o que tem evidência no código ou em print da tela).
6. **Fases** por risco crescente, cada uma commitável sozinha, com IDs, arquivos, critérios de aceite e métricas-alvo. **Última fase: "Alto risco, requer aprovação"**, com cada item separado e a pergunta a fazer ao dono, que não é executada sem aprovação.
7. **Fora do escopo** e **riscos**. Reestruturar uma tela ou o cabeçalho que está ruim não fica fora: entra como proposta na fase de alto risco, e a decisão visual é do dono.

Commit: `docs(ux): plano de melhorias de UX (levantamento)`, sem assinatura de IA.

## Depois
Resumo ao dono (principais problemas, fases, o que exige aprovação). Execução pela skill **`executar-plano-faseado`**.
