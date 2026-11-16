# Plano de UX: fim do plano Rastro multilíngue

**Status:** PROPOSTA (as correções de risco baixo marcadas como "na Fase 53" já entraram; o resto espera execução, e a Fase C espera a aprovação do dono item a item).
**Data:** 2026-11-15.
**Escopo:** o que a tradução mudou nas telas (Fases 45 a 52 do [`plano-mvp.md`](../arquitetura/plano-mvp.md)): texto que corta ou estoura em inglês e espanhol, rótulos mais longos apertando barras e cartões, termos entre telas pelo [glossário](../i18n/glossario.md), frases montadas em pedaços e acessibilidade da tradução.

## Cobertura

| Tela | Código | Na tela (1440 e 360 px, nos três idiomas) |
|---|---|---|
| Lista de levantamentos | lido | visto |
| Tela do levantamento (grafo, painel) | lido | visto |
| Visão consolidada | lido | visto |
| Banco (mapa) | lido | visto; a análise de consulta, só pelo código |
| Configurações | lido | visto |
| Diálogos (validação, C4, banco vivo) | lido | não abertos nesta passada (sem mudança de layout nas Fases 45 a 52) |
| CLI e MCP | recursos `.resx` lidos | não se aplica |

Fora: 1920 px (é a mesma grade de 1440, sem texto novo que dependa da largura) e as paletas além da Papel (a tradução não muda cor).

## Confiabilidade

- **Medido:** rolagem horizontal e texto cortado, por script no navegador (Chrome sem janela, CDP) em cinco telas, dois tamanhos e três idiomas. Nenhuma tela rola na horizontal. O único corte que depende do idioma foi o título do levantamento em pt e es na largura cheia.
- **Conferido no código pelo coordenador:** o defeito da moldura do laço (UX-I18N-F01), a consistência de termos (contagem por script nos JSON e nos `.resx`) e a ausência de português nas traduções.
- **Estimativa:** a naturalidade das frases em inglês e espanhol; não houve falante nativo de espanhol revisando.

## Descoberta

| Funcionalidade | Entrada | Cliques da tela inicial |
|---|---|---|
| Trocar o idioma | Configurações > Aparência > Idioma (cada idioma no próprio nome) | 1 clique e rolagem até Aparência (ou o índice da página) |

Quem abre o Rastro num idioma que não lê precisa reconhecer "Configurações" no idioma atual. O caso é raro, porque o idioma inicial é o do sistema operacional.

## Diagnóstico

Nenhuma tela rola na horizontal e nenhuma chave aparece no lugar do texto. Os problemas são de três tipos:

1. **Largura em 360 px:** a barra de modos da tela do levantamento e os controles do canvas apertam, nos três idiomas, e o espanhol piora (L01, L02).
2. **Termos:** o servidor ainda diz "nós" e "arestas" onde a tela diz "passos" e "ligações" (T04); o espanhol mistura "Quitar" e "Eliminar" (T05); "drawing" foi calcado do português (T06).
3. **Frases:** a legenda do mapa do banco é montada em pedaços e perde o sentido fora do português (F04).

## Achados

| ID | Onde | Problema | Sugestão | Risco | Esforço | Situação |
|---|---|---|---|---|---|---|
| UX-I18N-F01 | `core/graph/timeline-layout.ts` | A moldura do laço repetia o verbo em en e es ("↻ Repeats repeats while X"): o teste só reconhecia "Repete" | Reconhecer o verbo nos três idiomas | baixo | P | na Fase 53 |
| UX-I18N-T01 | `es.json` `flowMap.questions.gap` | "Laguna" é falso amigo | "Hueco:" | baixo | P | na Fase 53 |
| UX-I18N-T02 | `Rastro.Api/Resources/Messages*.resx` | A CLI dizia "survey" e "análisis" no lugar de "trace" e "rastreo" | Alinhar ao glossário | baixo | P | na Fase 53 |
| UX-I18N-T03 | `es.json` `surveys.empty.hint` | "tu primer rastro" (colide com o nome do produto) | "rastreo" | baixo | P | na Fase 53 |
| UX-I18N-F02 | `graph.plain.branchThen`/`Else` | "If yes / If not" e "Si sí" soam traduzidos | "If true / If false", "Si se cumple / Si no se cumple" | baixo | P | na Fase 53 |
| UX-I18N-F06 | `query.aiOffAfter` | "Without it" sem referente depois do link | "Without suggestions" e "Sin las sugerencias" | baixo | P | na Fase 53 |
| UX-I18N-F07 | `enum.surveyStatus.completed` (en) | "Done" no selo e "Ready" no filtro para o mesmo estado | "Ready" nos dois | baixo | P | na Fase 53 |
| UX-I18N-T10, T11 | `en-US.json` | "Pick" e "See only" ao lado de "Choose" e "Show only" | "Choose" e "Show only" | baixo | P | na Fase 53 |
| UX-I18N-L05 | `survey.page.html` | Título do levantamento cortado em pt e es, sem o nome inteiro | `title` com o ponto de entrada | baixo | P | na Fase 53 |
| UX-I18N-L06 | `en-US.json` `…badge` | "FK without index" cortado na lista de tabelas (240 px) | "FK, no index" | baixo | P | na Fase 53 |
| UX-I18N-F05 | `nodePanel.needUse*`, `c4.needUse`, `survey.prompt.turnOnAi`, `query.aiGone` | "the 'Write rules' use" e "el uso" soam traduzidos | "the 'Write rules' option under AI uses" e "la opción" | baixo | P | Fase A |
| UX-I18N-F08 | `liveDb.testTitle`, `testHelp`, `testHelpTyped` (en) | Ordem de palavras calcada ("Tests the connection already saved") | "Tests the connection that is already saved" | baixo | P | Fase A |
| UX-I18N-F09 | `settings.repos.removeText`, `settings.ai.offIntro` (en, es) | "The traces already made", "When on, it does the following:" | "Traces you already ran stay saved", "Once on, AI does the following:" | baixo | P | Fase A |
| UX-I18N-F10 | `survey.filters.path*` | "Paths" no plural para um caminho só; chave começando com ":" | "Path", e a frase inteira numa chave | baixo | P | Fase A |
| UX-I18N-F12 | `surveys.welcome.aiNote` e afins (en) | "open the Settings" | "open Settings" | baixo | P | Fase A |
| UX-I18N-F16 | `Failures.es.resx`, `failure.*.unexpected` (es) | "Falla inesperada" é calco | "Error inesperado" | baixo | P | Fase A |
| UX-I18N-T05 | `es.json` e CLI | "Quitar" e "Eliminar" para o mesmo ato | Regra no glossário (tirar da lista ou do grafo = quitar; apagar dado salvo = eliminar) e aplicar | baixo | M | Fase A |
| UX-I18N-T06 | ~20 chaves com "drawing" (en) e "dibujo" (es) | Calco de "desenho" | "view/diagram" e "vista/diagrama"; "draw" só para o verbo da edição | baixo | M | Fase A |
| UX-I18N-T07 | `error.ENTRY_POINT_REQUIRED`, `surveys.form.entry.text2`, `surveys.errors.entry` | O exemplo de ponto de entrada aparece em três formas | Um exemplo só por idioma | baixo | P | Fase A |
| UX-I18N-T08 | `flowCanvas.brief.*`, `surveys.welcome.step2` | "entry method" ao lado de "entry point" | Registrar no glossário ou padronizar | baixo | P | Fase A |
| UX-I18N-T09 | 11 chaves em es | "solutions" no plural inglês dentro do espanhol | Registrar "solution" no glossário e evitar o plural | baixo | P | Fase A |
| UX-I18N-L04 | `surveys.page` (es, 360 px) | Placeholder da busca cortado | Texto mais curto em es; o completo no `aria-label` | baixo | P | Fase A |
| UX-I18N-L08 | `index.html` | `lang="en"` até o `LanguageService` subir | Aplicar o `lang` antes do primeiro desenho | baixo | P | Fase A |
| UX-I18N-L10 | `core/database/db-map.ts` | Corte do nome e do selo no SVG por número de caracteres | Medir a largura do texto | médio | M | Fase B |
| UX-I18N-L12 | vários `.scss` | Larguras fixas em rótulos traduzidos (`.legend dt`, `.paint-label`, `.search-count`) | `min-width` em `ch` ou `fit-content` | baixo | M | Fase A |
| UX-I18N-L01 | `survey.page` (360 px) | Barra de modos quebra em duas ou três linhas ("Vista consolidada") | Rolagem horizontal própria do controle segmentado em tela estreita | médio | M | Fase B |
| UX-I18N-T04 | `.resx` do MCP, da CLI e do núcleo | "nós/arestas" (nodes, nodos) no servidor; "passos/ligações" na tela | Alinhar os três idiomas, inclusive o português, sem mudar o nome das ferramentas do MCP | médio | M | Fase B |
| UX-I18N-L02 | canvas (360 px) | "Só o método de entrada" passa por cima de "Buscar"; o minimapa cobre o passo | Esconder o minimapa abaixo de 480 px e empilhar os controles | alto | M | Fase C |
| UX-I18N-L07 | cabeçalho | Sem atalho para o idioma | Item pequeno ao lado do selo da IA, com o código do idioma, levando a Configurações > Aparência | alto | M | Fase C |
| UX-I18N-L09 | Configurações > Aparência | O rótulo "Idioma" só no idioma atual | Rótulo fixo bilíngue ("Idioma / Language") | alto | P | Fase C |
| UX-I18N-F04 | legenda do mapa do banco | Frase em pedaços com os selos no meio perde o sentido em en e es | Reescrever como chaves independentes, com os selos fora da frase | alto | M | Fase C |

## Fases

### Fase A: traduções e larguras (risco baixo)
- Itens: F05, F08, F09, F10, F12, F16, T05, T06, T07, T08, T09, L04, L08, L12.
- Arquivos: `frontend/src/i18n/en-US.json` e `es.json` (o português não muda), `docs/i18n/glossario.md`, os `.resx` es do núcleo, `index.html` e o `i18n.ts`, e três `.scss`.
- Aceite: teste de paridade, varredura de texto fixo e `verificar-traducoes` verdes; prints em 360 px do espanhol sem o placeholder cortado.

### Fase B: comportamento (risco médio)
- Itens: L01, L10, T04.
- Aceite: em 360 px, a barra de modos fica numa linha só com rolagem própria, nos três idiomas; os testes do MCP e da CLI conferem "passos" e "ligações"; o SVG exportado do mapa do banco sem sobreposição em es.

### Fase C: alto risco, requer aprovação (uma pergunta por item)
- **L02:** esconder o minimapa e empilhar os controles do canvas abaixo de 480 px? (some um elemento na tela estreita)
- **L07:** pôr no cabeçalho um atalho para o idioma (código do idioma, levando a Configurações > Aparência)? (elemento novo no cabeçalho)
- **L09:** deixar o rótulo do seletor fixo e bilíngue ("Idioma / Language")? (texto fixo fora da tradução)
- **F04:** reescrever a legenda do mapa do banco em frases independentes? (muda o texto e o template)

## Fora do escopo e riscos

- O texto gravado antes do plano (regra escrita pela IA, levantamentos antigos) fica no idioma em que nasceu, por decisão da ADR-009.
- Risco: a revisão do espanhol continua sem falante nativo; a correção por PR fica aberta no README.
