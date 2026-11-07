# Plano de UX: os acréscimos que ficaram abertos (2026-11-07)

**Status:** EXECUTADO em 2026-11-07 (Fases A a F, PRs #84 a #88 e o da Fase F).
**Origem:** os itens que continuaram `[ ]` depois da passada de 2026-11-07 nos planos antigos:
- [`plano-ux-2026-11-06-fim-plano-banco.md`](plano-ux-2026-11-06-fim-plano-banco.md) (UX-FIM);
- [`plano-ux-2026-11-06-tela-do-banco.md`](plano-ux-2026-11-06-tela-do-banco.md) (UX-DBF, UX-DBQ, UX-DBA, UX-PG, UX-PGL);
- [`plano-ux-2026-10-30-revisao-geral.md`](plano-ux-2026-10-30-revisao-geral.md) (A3, A5, A6, A7 e A9).

**Aprovação:** em 2026-11-07 o dono aprovou todos os itens, de qualquer risco: "pode executar todos os achados de UX, seja alto, médio, baixo, crítico, pode executar tudo". Os itens de risco alto (Fase E) e o build e os testes pela tela (Fase F, que muda a ADR-003) entram por essa aprovação.

O problema e a sugestão de cada item estão no plano de origem; aqui fica só o que fazer e o registro.

## Fase A: estados, avisos e acessibilidade (risco baixo)

- [x] Banco vivo e tela do banco: UX-FIM-B05 (o "Testar conexão" diz que testa a conexão salva), B06 (permissão mínima e promessa em `--text-sm` e `--ink`), B07 (erro ao ler a conexão com "Tentar de novo" e sem a promessa), B13 (`aria-controls` só com o painel na tela), B14 (nome e anúncio do "100%"), B15 (`@empty` nas views e nas procedures), B18 (título "Avisos do banco"), B19 (selo com reticências e o texto no `title`) e B20 (rótulos do painel com o nome do item).
- [x] Consulta, IA e painel do passo: UX-FIM-Q03 (`role="region"` nos `<pre>` e a gravidade escrita no `<mark>`), Q04 ("Copiado" anunciado), Q08 (link para as configurações quando o erro é de chave ou de orçamento), Q10 ("sem consulta no código") e Q16 (resposta vazia e recusa anunciadas).
- [x] App: UX-FIM-C03 ("Ir para o conteúdo"), C11 (título da aba com o repositório) e C13 (exemplo de pasta nos dois formatos).
- **Registro (2026-11-07):** feito. O título da aba usa uma `TitleStrategy` própria (`core/page-title.strategy.ts`) que troca `:nome` pelo parâmetro da rota; no levantamento, que não tem o repositório na rota, o título continua fixo. No Q08, os dois códigos que só as Configurações resolvem são `AI_NOT_CONFIGURED` e `AI_BUDGET_EXCEEDED`. Frontend 420/420.

## Fase B: tema e tokens (risco baixo, sem efeito visível)

- [x] UX-FIM-B16, B17, Q13, Q14, C14, C15 e C16: tokens no lugar dos valores soltos, `KeyMark` no lugar de `'FK'`, divergência como booleano, `.row-wrap`, `.section-title` e `.code-block` no tema, avisos de "salvo" num estilo só, `[hidden]` no `_base.scss` e `--topbar-h` e `--radius-pill`.
- **Registro (2026-11-07):** feito. Tokens novos com o mesmo valor em px (`--text-2xs`, `--space-0-5`, `--space-1-5`, `--radius-xs`, `--radius-pill`, `--control-h-xs`, `--topbar-h`, `--reading-w` e `--db-op-w`); a única diferença é o raio de 3px, que virou `--radius-sm` (4px) na chave, no selo "pessoal" e no trecho da consulta. `KeyMark` é enum numérico com `KEY_MARK_LABEL`, e a divergência de nulo virou booleano. `database.page.scss` caiu de 5,1 kB para 3,6 kB. Frontend 422/422.

## Fase C: foco e estado da tela do banco (risco médio)

- [x] UX-FIM-B04 (confirmação de remover a conexão pelo diálogo do tema), B08 (escolher na lista sem mover o foco), B09 (foco de volta ao item da lista também para view e procedure), B11 (painel rola para a vista abaixo de 900px) e B12 (Esc do painel só na aba do mapa e foco de volta ao "Exportar").
- [x] UX-FIM-Q17 (a sugestão recebida fica na tela se o estado da IA recarregar vazio).
- [x] UX-FIM-B10 (banco grande: blocos fora do filtro sem parada de Tab e atraso de 150 ms na busca).
- **Registro (2026-11-07):** feito. No B04, a pergunta de remover a conexão continua dentro do diálogo (seria diálogo sobre diálogo), com o foco em "Manter" ao abrir, de volta em "Remover conexão" ao fechar e o "Salvar" desabilitado enquanto ela está aberta. No B10, o recorte por saltos já tira os blocos do mapa; só a busca precisou do `tabindex="-1"`. No Q17, sem IA a sugestão recebida fica com a linha "A IA está desligada agora" e o link para as Configurações, sem botão. Frontend 431/431.

## Fase D: coerência do app (risco médio)

- [x] UX-FIM-C04 (Esc no `appDialog`, sem tirar o Esc que limpa a seleção quando não há diálogo), C12 (foco no `h1` a cada troca de rota, num lugar só, sem perder `?passo=` e `?tabela=`) e C17 (os dois atalhos de refazer nas duas telas).
- [x] UX-FIM-Q06 (`app-ai-disclosure`: um aviso só do que vai para o provedor, com os tokens de hoje, no C4, na refatoração, nas perguntas e na sugestão de consulta).
- [-] UX-FIM-C01, primeira parte: "Copiar" ao lado do comando de build e de testes. Sem sentido com a Fase F, que põe o comando editável na tela.
- **Registro (2026-11-07):** feito. O `appDialog` trata o Esc, emite `dialogClose` e não deixa o toque subir para o documento: só o diálogo de cima fecha, e as telas que limpam a seleção com o Esc não agem no mesmo toque. O foco vai ao `h1` a cada página nova, pelo `app.ts`, menos na primeira carga, na troca só de query ou fragmento e quando a página já pôs o foco num item do conteúdo. O `app-ai-disclosure` (`core/ai-disclosure.component.ts`) diz o provedor, o que vai, o que nunca vai e os tokens de hoje, na sugestão de consulta, no C4, nas perguntas sobre o fluxo, na refatoração e nos dois botões de IA do painel do passo. Os dois atalhos de refazer já funcionavam nas duas telas; faltava só o mesmo `title`. Frontend 442/442.

## Fase E: risco alto, aprovados pelo dono

- [x] UX-FIM-Q07: um padrão só para o rótulo dos botões de IA, registrado no `design-system.md`, com "Ver no mapa" em maiúscula.
- [x] UX-FIM-Q15: "A análise não consulta o banco." e o passo de origem entre aspas.
- [x] UX-DBQ-05: quem chega pelo "Analisar a consulta" do passo já vê a análise, sem clicar em "Analisar".
- [x] UX-DBF-05, segunda parte: o nome da tabela no próprio bloco do passo.
- [x] UX-DBA-06: o motivo do erro do provedor (o `title` do problema) em todas as telas de IA, e as mensagens dos provedores sem "nó".
- [x] UX-PG-05: as mensagens mais comuns dos leitores de T-SQL e de PostgreSQL em português.
- [x] UX-PGL-05: a permissão mínima em lista, um comando por linha, com "Copiar".
- [x] Revisão geral A3 ("← Voltar" das Configurações vira `page-bar`), A5 (rótulo fixo em "Resumo/Completo" e nas chamadas do .NET, estado só no `aria-pressed`), A6 (× fixo no canto do painel e "Ver somente este fluxo" numa linha própria), A7 (barra da edição da visão consolidada) e A9 ("de 3 para 5").
- **Registro (2026-11-07):** feito. Padrão dos botões de IA: verbo no infinitivo, objeto e "com a IA" ("Sugerir refatoração com a IA", "Propor destinos com a IA", "Escrever regra com a IA", "Sugerir pessoas com a IA"; a pergunta livre ficou "Perguntar à IA"), registrado no `design-system.md`. A barra da edição do A7 fica na tela da visão consolidada (`diagram.page`), e o `.bar-group` subiu para o `_page.scss`. As mensagens dos leitores de SQL passam por `SqlMessageTranslator` (por padrão, sem IA; o que não casa fica com o texto original), conferido também pelo leitor de verdade nos dois dialetos. O `AI_PROVIDER_ERROR` já levava o motivo no `title`; a tela passou a mostrá-lo. Backend 699/699, frontend 454/454.

## Fase F: build e testes pela tela (risco alto, muda a ADR-003)

- [x] UX-FIM-C01, segunda parte: editar o comando de build e o de testes do repositório nas Configurações. Rota nova de edição, com a mesma proteção das outras rotas que mudam dado (`AllowedHosts` e `Origin`), o comando continua rodando só pelo `ProcessRunner`, sem shell, e a ADR nova registra a mudança da ADR-003.
- **Registro (2026-11-07):** feito. `PUT api/repositories/{nome}/commands`, com o texto conferido (uma linha, sem caractere de controle, até 500 caracteres) e `REPOSITORY_COMMAND_INVALID`; a lista devolve os dois comandos. Nas Configurações, "Editar build e testes" abre os dois campos em linha, com a ajuda do "sem shell". Conferido no navegador com a API de verdade e um `RASTRO_HOME` temporário: o `config.json` ficou com os dois comandos. A [ADR-008](../adr/adr-008-comandos-pela-tela.md) substitui o item 2 da ADR-003. Também saíram três textos de tela que ainda falavam em "nó" (painel do passo e Configurações). Backend 706/706, frontend 458/458.

## Fim do plano

- [x] Documentação: `visao.md`, ADR, `SECURITY.md`, READMEs, `design-system.md`, `plano-mvp.md` e os planos de origem com os itens fechados.
- [x] Prints das telas tocadas em `docs/telas/ux-acrescimos/` (índice em `docs/telas/README.md`), com clique de verdade no Chrome: "Editar build e testes", "Salvar comandos", "Conectar banco vivo" e o Esc, o passo do grafo e a chegada pela consulta do passo.
- [ ] Levantamento de UX completo do frontend (pedido pela skill no fim de cada plano): fica para uma passada própria, depois do merge, para olhar a tela inteira com estes acréscimos.
