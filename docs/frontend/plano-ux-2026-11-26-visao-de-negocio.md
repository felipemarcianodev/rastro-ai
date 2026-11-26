# Plano de UX: visão de negócio (Fase 57)

**Status:** PROPOSTA (os itens de risco baixo da Fase A entraram na própria Fase 57; os demais esperam execução ou aprovação).
**Data:** 2026-11-26.
**Escopo:** a tela nova da visão de negócio (`frontend/src/app/pages/survey/business-view.component.*`) e o que ela mudou na página do levantamento (botão no seletor de desenho e exportação para o draw.io). Prints em [`docs/telas/fase-57/`](../telas/fase-57/).

## Cobertura

| Tela ou funcionalidade | Lido no código | Visto na tela | Fora e por quê |
|---|---|---|---|
| Visão de negócio com decisão, recusas e fim (`SetLanguage` do próprio Rastro) | sim | sim, 1920, claro e escuro | 1280 e 360: a janela do navegador não redimensionou (continuou com 1920 por dentro) |
| Painel do passo (decisão e recusa) | sim | sim, com clique de verdade | |
| Estado vazio (`SurveysController.Create`, sem passo-chave) | sim | sim | |
| Carregando e erro | sim | não | não forcei a rota a falhar no navegador; coberto pelos testes do componente |
| Exportar para o draw.io | sim | sim, aberto no draw.io | |
| Teclado (Tab entre balões, Enter) | sim | sim | |
| Console | | sem erro | |

## Confiabilidade

O coordenador conferiu no navegador o desenho, as cores nos dois temas, o clique real no balão e no losango, o painel, o estado vazio, o `.drawio` no draw.io, o Tab e o console, e no código o UX-BIZ-001 (menu Exportar). Os demais achados vêm da leitura do código por um agente, com a linha citada. Sem usuário real nem dados de uso, os achados são prováveis, não comprovados.

## Descoberta

| Funcionalidade | Entrada | Cliques da tela inicial |
|---|---|---|
| Visão de negócio de um levantamento | botão "Visão de negócio" no seletor de desenho do levantamento | 2 (o levantamento na lista e o botão) |
| Painel do passo | clique no balão | 3 |
| Exportar a visão de negócio | menu Exportar, "Baixar para o draw.io" | 4 |

## Diagnóstico: os principais problemas

1. UX-BIZ-001: PNG, SVG e Mermaid ficam no menu Exportar e falham com uma mensagem que engana.
2. UX-BIZ-022: Tab a partir de um balão clicado pula os outros balões (o Foblex leva o balão clicado para o fim da lista).
3. UX-BIZ-021 e 004: o leitor de tela ouve o tipo repetido ("Recusa: Recusa: ...") e não ouve "proposto" nem a referência.
4. UX-BIZ-002: o painel muda sem aviso para o leitor de tela.
5. UX-BIZ-023: o balão escolhido fica meio escondido atrás do painel de baixo.
6. UX-BIZ-018: "proposto, ainda não confirmado por você" sem lugar para confirmar nesta tela (a confirmação é da Fase 59).
7. UX-BIZ-007: os selos "proposto" e "inferido" não parecem selos.
8. UX-BIZ-014: painel de baixo sem limite de largura em 1920 e sem regra para tela estreita.
9. UX-BIZ-009: `catch` e `finally` aparecem em código nos ramos.
10. UX-BIZ-015: texto longo cortado no balão, inteiro só no painel.

## Achados

| ID | Onde | Problema | Sugestão | Risco | Esforço | Impacto |
|---|---|---|---|---|---|---|
| UX-BIZ-001 | `survey.page.ts` (exportImage, exportMermaid) | PNG, SVG e Mermaid falham na visão de negócio com "O grafo ainda não está desenhado" | desabilitar os itens nessa visão, com dica de que ela exporta para o draw.io | baixo | P | alto |
| UX-BIZ-002 | `business-view.component.html` (painel) | painel muda sem aviso ao leitor de tela | `aria-live="polite"` no título do painel | baixo | P | alto |
| UX-BIZ-003 | `business-view.component.html` (balões) | uma parada de Tab por balão, sem setas | tabindex itinerante e setas pelas ligações, Esc fecha | médio | M | alto |
| UX-BIZ-004 | `business-view.component.ts` (stepAria) | rótulo do balão sem "proposto", "inferido" e referência | incluir o estado no rótulo | baixo | M | alto |
| UX-BIZ-005 | `business-view.component.scss` (foco) | foco diferente do mapa; retângulo em volta do losango | `var(--focus)` como o mapa; foco que segue a forma do losango | baixo | P | médio |
| UX-BIZ-006 | `business-view.component.scss` (`.biz-ref`) | texto secundário pequeno sobre cor de fundo, contraste não medido | medir e trocar por uma tinta do tipo | baixo | M | médio |
| UX-BIZ-007 | `business-view.component.scss` (`.biz-badge`) | selo sem borda, não parece selo | borda, padding e raio de pílula | baixo | P | médio |
| UX-BIZ-008 | painel ("Ver só este trecho", "Ver no grafo") | botões saem da visão sem dizer | `title` explicando e "Ver no grafo" como primário | alto | P | médio |
| UX-BIZ-009 | `core/graph/plain.ts` | `catch` e `finally` em código nos ramos | texto simples para as seções do try | alto | P | médio |
| UX-BIZ-010 | painel (abrir no host e no editor) | nenhum retorno depois do clique | aviso "Abrindo no GitHub…" ou de falha | baixo | M | médio |
| UX-BIZ-011 | `business-view.component.ts` (links) | falha da rota de links engolida: os botões somem | nota discreta "Links do repositório indisponíveis" | baixo | P | médio |
| UX-BIZ-012 | barra da visão | sem o "100%" que o mapa tem; sem atalhos de zoom | botão "100%" e atalhos `+`, `-` e `0` | médio | M | médio |
| UX-BIZ-013 | área do desenho | nada diz que arrastar move a tela | cursor de arrastar e dica no rodapé | baixo | P | médio |
| UX-BIZ-014 | painel de baixo | 45% da altura, sem limite de largura nem regra para tela estreita | `max-width` em tela larga e uma coluna em tela estreita | baixo | M | médio |
| UX-BIZ-015 | balão | texto cortado em duas ou três linhas | altura do balão pelo texto, ou texto inteiro no foco | alto | G | médio |
| UX-BIZ-016 | `business-view.component.scss` | medidas soltas em vez de tokens | trocar por `--space-*` e `--focus` | baixo | P | baixo |
| UX-BIZ-017 | valor do zoom e nota do resumo | `aria-label` esconde o conteúdo; nota fora de região viva | texto oculto no lugar do rótulo; `role="status"` na nota | baixo | P | baixo |
| UX-BIZ-018 | painel ("proposto") | diz "ainda não confirmado por você" sem lugar para confirmar | dica de onde confirmar até a Fase 59 trazer a confirmação | alto | P | médio |
| UX-BIZ-019 | seletor de desenho | a descrição do botão só aparece com o mouse | `aria-describedby` com o mesmo texto | baixo | P | baixo |
| UX-BIZ-020 | `business-view.component.ts` | foco e centralização por `setTimeout` | `afterNextRender` e `fFullRendered` | médio | M | baixo |
| UX-BIZ-021 | `business-view.component.ts` (stepAria) | tipo repetido no rótulo ("Recusa: Recusa: ...", "Fim: Fim") | não repetir quando o texto já começa com o tipo | baixo | P | alto |
| UX-BIZ-022 | balões no Foblex | Tab a partir do balão clicado pula os outros | ordem de tabulação pela ordem do fluxo, independente da ordem que o Foblex dá aos nós | médio | M | alto |
| UX-BIZ-023 | painel de baixo | o balão escolhido fica meio escondido atrás do painel | trazer o balão para a área visível ao abrir o painel | médio | P | médio |

## Fases

### Fase A: risco baixo (dentro da Fase 57)

UX-BIZ-001, 002, 004, 005, 007, 011, 013 e 021. Aceite: build e suíte do frontend verdes; menu Exportar sem item que falha na visão de negócio; rótulo do balão sem repetição e com o estado; foco visível igual ao do mapa.

### Fase B: risco baixo, depois

UX-BIZ-006, 010, 014, 016, 017 e 019. Aceite: contraste medido (texto de 4,5:1 para cima); painel legível em 1920 e em 360.

### Fase C: risco médio (junto com a tela livre, Fase 58)

UX-BIZ-003, 012, 020, 022 e 023. É o mesmo terreno da Fase 58 (teclado, zoom, centralizar), que já prevê "Tab percorre os balões na ordem do fluxo".

### Fase D: alto risco, requer aprovação

- UX-BIZ-008: "Ver no grafo" vira o botão primário do painel e "Ver só este trecho" ganha um `title` dizendo que sai da visão de negócio? (muda texto e hierarquia)
- UX-BIZ-009: `catch` e `finally` passam a aparecer como "Se der erro (X)" e "Sempre ao final" em todo o Rastro (grafo, mapa e visão de negócio)? (muda texto já conhecido)
- UX-BIZ-015: o balão cresce com o texto, em vez de cortar em duas ou três linhas? (muda o layout)
- UX-BIZ-018: até a Fase 59, o selo "proposto" leva uma dica de onde confirmar? Ou o texto passa a dizer só "proposto pela regra"? (muda texto)

## Fora do escopo e riscos

- Editar e confirmar o texto de negócio é da Fase 59; mover balões e salvar posição, da Fase 58.
- Riscos: a ordem de tabulação depende de como o Foblex ordena os nós; a correção pode precisar de tabindex próprio fora do DOM do Foblex.
