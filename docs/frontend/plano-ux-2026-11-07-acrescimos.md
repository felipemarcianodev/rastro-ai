# Plano de UX: os acréscimos que ficaram abertos (2026-11-07)

**Status:** EM EXECUÇÃO.
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

- [ ] UX-FIM-B16, B17, Q13, Q14, C14, C15 e C16: tokens no lugar dos valores soltos, `KeyMark` no lugar de `'FK'`, divergência como booleano, `.row-wrap`, `.section-title` e `.code-block` no tema, avisos de "salvo" num estilo só, `[hidden]` no `_base.scss` e `--topbar-h` e `--radius-pill`.

## Fase C: foco e estado da tela do banco (risco médio)

- [ ] UX-FIM-B04 (confirmação de remover a conexão pelo diálogo do tema), B08 (escolher na lista sem mover o foco), B09 (foco de volta ao item da lista também para view e procedure), B11 (painel rola para a vista abaixo de 900px) e B12 (Esc do painel só na aba do mapa e foco de volta ao "Exportar").
- [ ] UX-FIM-Q17 (a sugestão recebida fica na tela se o estado da IA recarregar vazio).
- [ ] UX-FIM-B10 (banco grande: blocos fora do filtro sem parada de Tab e atraso de 150 ms na busca).

## Fase D: coerência do app (risco médio)

- [ ] UX-FIM-C04 (Esc no `appDialog`, sem tirar o Esc que limpa a seleção quando não há diálogo), C12 (foco no `h1` a cada troca de rota, num lugar só, sem perder `?passo=` e `?tabela=`) e C17 (os dois atalhos de refazer nas duas telas).
- [ ] UX-FIM-Q06 (`app-ai-disclosure`: um aviso só do que vai para o provedor, com os tokens de hoje, no C4, na refatoração, nas perguntas e na sugestão de consulta).
- [ ] UX-FIM-C01, primeira parte: "Copiar" ao lado do comando de build e de testes.

## Fase E: risco alto, aprovados pelo dono

- [ ] UX-FIM-Q07: um padrão só para o rótulo dos botões de IA, registrado no `design-system.md`, com "Ver no mapa" em maiúscula.
- [ ] UX-FIM-Q15: "A análise não consulta o banco." e o passo de origem entre aspas.
- [ ] UX-DBQ-05: quem chega pelo "Analisar a consulta" do passo já vê a análise, sem clicar em "Analisar".
- [ ] UX-DBF-05, segunda parte: o nome da tabela no próprio bloco do passo.
- [ ] UX-DBA-06: o motivo do erro do provedor (o `title` do problema) em todas as telas de IA, e as mensagens dos provedores sem "nó".
- [ ] UX-PG-05: as mensagens mais comuns dos leitores de T-SQL e de PostgreSQL em português.
- [ ] UX-PGL-05: a permissão mínima em lista, um comando por linha, com "Copiar".
- [ ] Revisão geral A3 ("← Voltar" das Configurações vira `page-bar`), A5 (rótulo fixo em "Resumo/Completo" e nas chamadas do .NET, estado só no `aria-pressed`), A6 (× fixo no canto do painel e "Ver somente este fluxo" numa linha própria), A7 (barra da edição da visão consolidada) e A9 ("de 3 para 5").

## Fase F: build e testes pela tela (risco alto, muda a ADR-003)

- [ ] UX-FIM-C01, segunda parte: editar o comando de build e o de testes do repositório nas Configurações. Rota nova de edição, com a mesma proteção das outras rotas que mudam dado (`AllowedHosts` e `Origin`), o comando continua rodando só pelo `ProcessRunner`, sem shell, e a ADR nova registra a mudança da ADR-003.

## Fim do plano

- [ ] Documentação: `visao.md`, ADR, `SECURITY.md`, READMEs, `design-system.md`, `plano-mvp.md` e os planos de origem com os itens fechados.
- [ ] Prints das telas tocadas em `docs/telas/ux-acrescimos/` e o levantamento de UX completo.
