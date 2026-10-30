# Levantamento de UX: revisão geral do frontend (2026-10-30)

**Status:** EXECUTADO nos riscos baixo e médio; os itens de risco alto esperam a aprovação do dono (fase A no fim).
**Escopo:** o frontend inteiro (lista de levantamentos, fluxo com os quatro desenhos e o painel do passo, visão consolidada e a edição dela, configurações, diálogos), a pedido do dono: "testa tudo e corrija a UI se tiver ruim e a UX também, eu vi lá botão desalinhado".
**Como:** dois agentes de leitura (análise estática, uma fatia cada) e a verificação no navegador com a loja fictícia, com clique de verdade, nas larguras 1920 e 1280. Um verificador no console mediu, em cada linha de controles lado a lado, a altura e o centro vertical de botões, campos e seletores.

## Diagnóstico

- **O botão desalinhado:** "Mostrar chamadas do .NET" tinha `margin-top` de 8px numa linha alinhada pelo topo e ficava abaixo dos vizinhos. Aparece em todo levantamento .NET (na loja Angular ele não aparece, por isso não saiu nos prints).
- **Três alturas de controle sem token:** campo com 38px, botão com 36px e botão pequeno com 32px. Lado a lado, os topos e as bases ficavam de 1 a 6px fora: o seletor do painel e o botão "Aplicar", e o botão "Criar passo" ficava abaixo dos campos do novo passo.
- **Texto do passo:** o Foblex centraliza o texto do nó. A linha de baixo curta ("confirmacao.ts:3") ia para o meio e a longa, cortada com reticências, encostava à esquerda.
- **Abertura do fluxo:** a moldura do primeiro sistema começava fora da tela, e o rótulo "loja-web · Angular" abria cortado atrás da coluna de botões.
- **Desenho consolidado (Fase 28):** a barra ficava à direita e a do canvas, à esquerda; o botão de desenho mudava de posição entre os desenhos; o botão de zoom em % era mais largo que a coluna; o carregamento era um texto pequeno no pé; e, aberto direto no consolidado, o painel perdia o "Resumo do fluxo".

## Achados e situação

| ID | Onde | Problema | O que foi feito | Risco | Situação |
|---|---|---|---|---|---|
| R1 | `flow-canvas.component.scss` | "Mostrar chamadas do .NET" 8px abaixo dos vizinhos | Sem a margem; a linha centraliza; os botões só mudam cor e borda | baixo | feito |
| R2 | `_tokens`, `_controls` | Três alturas de controle (38, 36, 32) | `--control-h` (36px) e `--control-h-sm` (32px) para campo, botão e botão pequeno; `.input.is-small` para a busca do grafo e o seletor de paleta | médio | feito |
| R3 | `_flow.scss` | Texto do passo centralizado pelo Foblex | `text-align: left` no nó | baixo | feito |
| R4 | `flow-canvas.component.ts` | Rótulo da moldura cortado na abertura | A abertura conta a moldura que contém a entrada | baixo | feito |
| R5 | `survey.page` | "Criar passo" abaixo dos campos | Alinhado pelo topo, com rótulo vazio sobre o botão | baixo | feito |
| R6 | `node-panel.component.scss` | Seletor e "Aplicar" desalinhados | `align-items: center` | baixo | feito |
| R7 | `survey.page.scss` | "?" da edição com 28px entre botões de 32px; ações da edição sem quebra de linha | 32px do tema; `flex-wrap` e `align-items` | baixo | feito |
| R8 | `app.scss` | Seletor de paleta fora do centro dos ícones | Rótulo em `inline-flex` e seletor pequeno do tema | baixo | feito |
| R9 | `c4-dialog` | "Acrescentar" no meio do texto corrido | Linha flex com o texto e o botão (`.ai-suggestion-row`) | baixo | feito |
| R10 | `settings.page.scss` | "Remover…" colado no topo da linha | Centralizado | baixo | feito |
| R11 | `_controls.scss` | "i" dos campos com 20px | 24px (alvo mínimo do WCAG 2.5.8) | baixo | feito |
| R12 | consolidado | Barra à direita, botão de desenho em outra posição, % mais largo | Barra à esquerda como a do canvas; desenho na 4ª posição; % com 32px | baixo | feito |
| R13 | consolidado | Carregamento só em texto | O mesmo estado do canvas (a trilha andando), que subiu para o tema com o `.walking` duplicado | médio | feito |
| R14 | consolidado | Aberto direto, o painel perdia o "Resumo do fluxo" | O resumo saiu do canvas para `core/graph/summary.ts` (`flowSummary`, `readingLabel`), e o consolidado também o monta | médio | feito |
| R15 | palco do Mermaid | Instruções longas no `aria-label` | Texto escondido em `aria-describedby` | baixo | feito |
| R16 | `_controls.scss` | Botão que liga e desliga sem estado visível | `.btn-g[aria-pressed='true']` afundado e com borda forte | baixo | feito |

## Fase A: alto risco, espera aprovação do dono

Nada aqui foi feito. Cada item muda texto, ordem, posição ou remove algo.

- [ ] **A1. Escolha do desenho:** trocar o botão que gira pelos quatro desenhos (o ícone mostra o próximo, não o atual) por um seletor de quatro opções com o atual marcado.
- [ ] **A2. Barra do fluxo:** separar informação (status, contagens) de ações, com "Editar fluxo" sempre no fim.
- [ ] **A3. Configurações:** "← Voltar" vira a mesma barra das outras telas (`page-bar` com a seta e o título ao lado). Também: um botão primário só ("Salvar configuração"; "Adicionar repositório" vira secundário).
- [ ] **A4. Menus "⋯" e "Mais":** o mesmo rótulo nos dois modos.
- [ ] **A5. Botões que trocam de texto e de estado ao mesmo tempo** ("Resumo"/"Completo", "Mostrar"/"Esconder chamadas do .NET"): rótulo fixo e o estado só no `aria-pressed`.
- [ ] **A6. Cabeçalho do painel:** o × fixo no canto, com "Ver somente este fluxo" numa linha própria.
- [ ] **A7. Barra da edição da visão consolidada:** agrupar Desfazer e Refazer, "Descartar edição" com o estilo de ação destrutiva e "Salvar" em tamanho normal.
- [ ] **A8. Cabeçalho do topo:** o selo "IA desligada" e a engrenagem levam ao mesmo lugar; ficar com um só.
- [ ] **A9. Diálogo de validação:** trocar "3 → 5" por "de 3 para 5".

## Fora do escopo

- Tokens para os tamanhos de fonte soltos do grafo (10 e 11px) e para os números de posição (`right: 220px`, minimapa): refatoração sem efeito visível.
- Navegação por setas no grupo de cores da edição e na coluna de botões (roving tabindex).
- Legenda e minimapa no desenho consolidado.
