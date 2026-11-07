# Levantamento de UX: fim do plano do banco (Fases 36 a 42)

**Status:** PROPOSTA, com 16 dos 54 achados já feitos pela repaginação de UX (conferido em 2026-11-07; veja os `[x]` nas fases); as fases esperam a aprovação do dono, e a Fase E (risco alto) é aprovada item a item.
**Escopo:** o frontend inteiro, com foco no que as Fases 36 a 42 tocaram: tela do banco (mapa, painel, recorte, exportar), aba "Analisar consulta", banco vivo (diálogo, selo, plano estimado), sugestão da IA, PostgreSQL e a seção "No banco" do painel do passo. Também a coerência dessas telas com o resto do app (cabeçalho, lista de levantamentos, levantamento, configurações, visão consolidada, diálogos e avisos).
**Data:** 2026-11-06.
**Como:** três agentes de leitura (análise estática, uma fatia cada: tela do banco e banco vivo; consulta, IA e painel do passo; coerência com o app) e a verificação no navegador (Chrome, paleta Papel no claro, janela de 1920 por 911) com a loja fictícia (`LojaBanco` e `LojaBancoPg`), a API local e um `RASTRO_HOME` temporário. Clique real no item da lista, na aba "Analisar consulta", na caixa da consulta, em "Analisar" e em "Conectar banco vivo"; Esc no diálogo.

Os achados por fase que já foram corrigidos estão em [`plano-ux-2026-11-06-tela-do-banco.md`](plano-ux-2026-11-06-tela-do-banco.md) e não se repetem aqui. Os itens que já esperavam aprovação (UX-DB-09, UX-DBQ-05, UX-DBA-06, UX-PG-05 e UX-PGL-05) continuam lá e no [`plano-mvp.md`](../arquitetura/plano-mvp.md).

## Diagnóstico

### Métricas por tela

| Tela | Elementos visíveis | Profundidade do DOM | Botões primários visíveis |
|---|---|---|---|
| Mapa do banco (6 tabelas, painel aberto) | 259 (medido na Fase 38) | 10 | 0 (tela de leitura) |
| Analisar consulta, depois de analisar (4 achados) | 87 | 13 | 0 ("Analisar" some com o resultado) |
| Diálogo "Banco vivo", sem conexão | 1 diálogo de 415px numa janela de 911px, corpo sem rolagem, rodapé visível | | 1 ("Salvar") |
| Mapa do banco no PostgreSQL (7 tabelas) | | | 0; 6 tipos cortados nos blocos |

- Console sem erro no mapa do banco (SQL Server e PostgreSQL), na análise de consulta, nas configurações e na lista de levantamentos.
- O diálogo "Banco vivo" cabe na janela e o foco vai para o campo da string ao abrir. A janela baixa não foi medida: o redimensionamento da janela não teve efeito nesta máquina. Pelo CSS, `.dialog` tem `max-height: 100%` e o corpo rola.
- Primeiro uso: a configuração de desenvolvimento desta máquina já traz um repositório cadastrado, então o Rastro vazio foi avaliado pelo código (UX-FIM-C10).
- A seção "No banco" do painel do passo foi avaliada pelo código; a verificação dela no navegador pede um levantamento com SQL cru, já feito na Fase 40a.

### Os 10 principais problemas, por impacto

1. **UX-FIM-B01:** salvar ou remover a conexão do banco vivo recarrega a tela inteira, e quem estava na aba "Analisar consulta" perde a consulta digitada e o resultado.
2. **UX-FIM-B03:** um clique no fundo do diálogo "Banco vivo" fecha o diálogo e apaga a string de conexão já colada, que fica num campo de senha e não dá para conferir.
3. **UX-FIM-Q02:** depois de "Analisar", o foco cai no `body` (conferido no navegador) e o resultado não é anunciado ao leitor de tela.
4. **UX-FIM-Q01:** com a IA desligada, a aba "Analisar consulta" não diz como ligar a sugestão, ao contrário do C4 e do prompt de refatoração, que levam às configurações.
5. **UX-FIM-N01:** no PostgreSQL, os tipos longos ficam cortados no bloco do mapa (`character vary…`, `timestamp with…`), e o tamanho da coluna some da vista.
6. **UX-FIM-B12:** o "←" da tela do banco sempre vai para a lista, mesmo quando se chegou do passo de um levantamento; o das configurações volta pelo histórico.
7. **UX-FIM-C10:** no primeiro uso, a lista de levantamentos pede para escolher um repositório que não existe, e o caminho para cadastrar (configurações) fica em letra pequena.
8. **UX-FIM-B02:** remover a conexão fecha o diálogo sem nenhum aviso de sucesso; a pessoa só percebe pelo selo que some.
9. **UX-FIM-C03:** não há link "Ir para o conteúdo", e o teclado passa pelo cabeçalho inteiro em toda tela.
10. **UX-FIM-C02:** a falha ao carregar a configuração da IA não tem "Tentar de novo", única tela sem essa saída.

## Achados

Os IDs guardam a fatia de origem: B (tela do banco e banco vivo), Q (consulta, IA e painel do passo), C (coerência com o app), N (só no navegador) e A (parte de risco alto separada de outro achado). Os achados que duas fatias acharam juntos ficaram num ID só.

| ID | Onde | Problema | Sugestão | Risco | Esforço | Impacto |
|---|---|---|---|---|---|---|
| UX-FIM-B01 | `database.page.ts:212, 334-337` | Salvar ou remover a conexão zera o esquema e desmonta a análise de consulta: a consulta e o resultado se perdem, junto com a tabela escolhida e o recorte | Trocar o esquema quando o novo chegar, sem zerar o estado; guardar a consulta no estado da página | Médio | M | Alto |
| UX-FIM-B02 | `live-connection-dialog.component.ts:87-97` | Remover a conexão fecha o diálogo sem aviso; "Conexão salva" só aparece dentro do diálogo | Aviso da página: "Conexão removida. O Rastro volta a ler só o código." | Baixo | P | Alto |
| UX-FIM-B03 | `live-connection-dialog.component.html:1` | O clique no fundo fecha o diálogo e apaga a string digitada; o rodapé não tem "Fechar" como os outros diálogos têm "Cancelar" (também UX-FIM-C05) | Não fechar pelo fundo com texto no campo; "Fechar" antes de "Salvar" | Médio | P | Médio |
| UX-FIM-B04 | `live-connection-dialog.component.html:54-62` | "Remover conexão" some ao ser clicado e o foco se perde; a confirmação tem desenho próprio, fica no fim do corpo e o "Salvar" continua ativo ao lado (também UX-FIM-C06) | Usar o `app-confirm-dialog`, ou focar "Manter" ao abrir a pergunta e devolver o foco ao fechar | Médio | P | Médio |
| UX-FIM-B05 | `live-connection-dialog.component.html:68` | "Testar conexão" testa a conexão salva, não a digitada, sem dizer isso | Ajuda "Testa a conexão já salva" e o botão desabilitado com texto novo não salvo | Baixo | P | Médio |
| UX-FIM-B06 | `live-connection-dialog.component.scss:14-19` | A permissão mínima, o trecho mais importante do diálogo, está em 12px e em cinza suave; a promessa "Nenhum dado das tabelas é lido" também (também UX-FIM-C07) | `--text-sm` e `--ink` nos dois textos (a lista com "Copiar" é a UX-PGL-05) | Baixo | P | Médio |
| UX-FIM-B07 | `database.page.ts:250-254` | Se a leitura da conexão falha, a barra promete "Nenhum banco é consultado" mesmo com conexão salva | Estado de erro da conexão com "Tentar de novo" e sem a promessa | Baixo | P | Médio |
| UX-FIM-B08 | `database.page.ts:308-312` | Escolher uma tabela na lista leva o foco para o bloco do mapa, e quem navega a lista pelo teclado perde a posição | Na lista, escolher e rolar o mapa sem mover o foco | Médio | P | Médio |
| UX-FIM-B09 | `database.page.ts:327-332` | Fechar o painel de uma view ou procedure deixa o foco no `body` | Devolver o foco ao item da lista que abriu o painel | Médio | P | Médio |
| UX-FIM-B10 | `database.page.html:164-168`, `db-map.ts:15` | Banco grande: um bloco tabulável por tabela (300 paradas de Tab com 300 tabelas), a busca refiltra a cada tecla | Blocos fora do recorte ou do filtro com `tabindex="-1"`, atraso de 150 ms na busca; a virtualização fica com a pendência de banco grande do plano | Médio | M | Médio |
| UX-FIM-B11 | `database.page.scss:262-280` | Abaixo de 900px, o painel abre embaixo do mapa, fora da vista | Rolar o painel para a vista ao escolher | Médio | P | Médio |
| UX-FIM-B12 | `database.page.html:3` | O "←" vai sempre para a lista, mesmo vindo de um levantamento; o das configurações volta pelo histórico. O Esc fecha o painel do mapa com a aba de consulta ativa, e o menu "Exportar" não devolve o foco ao botão (também UX-FIM-C08) | Voltar pelo histórico do app quando houver (a lógica de `settings.page.ts:152` sobe para `core/`); Esc do painel só na aba do mapa; foco de volta ao "Exportar" | Médio | M | Alto |
| UX-FIM-B13 | `database.page.html:71-78` | Na carga e no erro, as abas apontam (`aria-controls`) para painéis que não existem | `aria-controls` só com o painel na tela | Baixo | P | Baixo |
| UX-FIM-B14 | `database.page.html:140-144` | O botão "100%" do zoom só tem o número como nome, e a mudança não é anunciada | `aria-label` "Voltar o mapa a 100%" e o valor em `aria-live` | Baixo | P | Baixo |
| UX-FIM-B15 | `database.page.html:97-112` | Com a busca sem resultado, "Views 0" e "Procedures 0" ficam sem linha nenhuma | `@empty` com "Nenhuma view com esse nome" e o mesmo nas procedures | Baixo | P | Baixo |
| UX-FIM-B16 | `_database.scss:3-37`, `database.page.scss:168, 231`, `query-plan.component.scss` | Valores soltos (10px, 3px, 6px, 60px) e um `!important`; o 10px fica abaixo do menor token de texto | Tokens do tema e a exceção das medidas do mapa documentada no `design-system.md` | Baixo | P | Baixo |
| UX-FIM-B17 | `db-panel.component.html:12-13, 34`, `db-map.ts:111-114` | Strings mágicas: `r.mark === 'FK'`, `c.nullable === 'divergente'` | Constante ou enum (`KeyMark`) e um booleano de divergência no modelo | Baixo | P | Baixo |
| UX-FIM-B18 | `query-plan.component.html:12, 49-55` | O `role="status"` envolve o plano inteiro, e o leitor de tela lê tudo a cada pedido; avisos sem título | `role="status"` só num resumo; título "Avisos do banco" | Baixo | P | Baixo |
| UX-FIM-B19 | `database.page.html:24` | O selo do banco vivo, com altura fixa, estoura com nome longo de servidor | Reticências com o texto inteiro no `title` | Baixo | P | Baixo |
| UX-FIM-B20 | `db-panel.component.html:3-4, 34` | Painel com rótulo genérico "Item escolhido", tabela de campos sem nome e três "Fechar" iguais | Rótulos com o nome do item ("Campos de pedidos", "Fechar o painel de pedidos") | Baixo | P | Baixo |
| UX-FIM-Q01 | `query-analysis.component.html:60` | Sem IA, a seção "Sugestão da IA" some sem dizer como ligar; o C4 e o prompt de refatoração levam às configurações | `@else` com "Para a IA sugerir melhora na consulta, ligue a IA e o uso "Sugestões" em Configurações" (texto, sem botão morto) | Baixo | P | Alto |
| UX-FIM-Q02 | `query-analysis.component.html:16, 25-29` | Depois de "Analisar", a caixa e o botão saem da tela e o foco cai no `body` (conferido no navegador) | Foco no título "Consulta analisada" (`tabindex="-1"`, `preventScroll`) e um `role="status"` com "Analisada: N achados" | Médio | P | Alto |
| UX-FIM-Q03 | `query-analysis.component.html:31`, `query-suggestion.component.html:20-86` | `aria-label` em `<pre>` sem papel é ignorado; o trecho marcado só tem o sublinhado colorido | `role="region"` nos `<pre>`; texto escondido "(achado alto)" no `<mark>` | Baixo | P | Médio |
| UX-FIM-Q04 | `query-suggestion.component.html:35, 88` | "Copiado" só no rótulo do botão, sem região viva (o prompt de refatoração tem) | O mesmo `visually-hidden role="status"` da tela do levantamento | Baixo | P | Médio |
| UX-FIM-Q05 | `query-analysis.component.html:64-72` | "Perguntando à IA…" e a resposta não são anunciados; o botão desabilitado perde o foco | Região viva fixa na seção e `aria-disabled` na espera | Baixo | P | Médio |
| UX-FIM-Q06 | `query-analysis.component.html:64-65` | O aviso do que vai ao provedor não mostra o gasto de tokens, e "Pedir outra sugestão" gasta de novo sem avisar; as outras telas de IA nem têm o aviso | Tokens de hoje ao lado do aviso e um componente único de aviso (`app-ai-disclosure`) para C4, refatoração e perguntas | Médio | M | Médio |
| UX-FIM-Q08 | `query-analysis.component.html:67-69` | Erro do provedor na sugestão sem "Tentar de novo"; orçamento esgotado sem link para as configurações | O mesmo aviso de erro da análise, com "Tentar de novo" e o link quando for chave ou orçamento | Baixo | P | Médio |
| UX-FIM-Q09 | `node-panel.component.html:151` | Vários "Analisar a consulta" no mesmo passo com o mesmo nome acessível | `aria-label` com a tabela e a linha | Baixo | P | Médio |
| UX-FIM-Q10 | `node-panel.component.html:139-153` | Na seção "No banco", o acesso sem consulta extraída não diz por que não há "Analisar a consulta" | "sem consulta no código" em texto discreto | Baixo | P | Baixo |
| UX-FIM-Q11 | `query-analysis.component.html:10-18` | Botão desabilitado com a caixa vazia sem explicação; o Ctrl+Enter só na ajuda pequena | `aria-describedby` da caixa para a ajuda | Baixo | P | Baixo |
| UX-FIM-Q12 | `query-analysis.component.html:53, 93` | "ver no mapa" troca de aba sem levar o foco à tabela | Foco no bloco da tabela ao chegar ao mapa (a maiúscula do rótulo vai com a UX-FIM-Q07) | Médio | M | Baixo |
| UX-FIM-Q13 | `query-analysis.component.scss`, `query-suggestion.component.scss` | Valores soltos (1px 4px, 3px, 2px, 960px) | Tokens; largura de leitura como token; o selo de gravidade sobe para o tema | Baixo | P | Baixo |
| UX-FIM-Q14 | os mesmos dois SCSS | Linha flexível, título de seção e bloco de código repetidos nos dois componentes | `.row-wrap`, `.section-title` e `.code-block` no tema | Baixo | M | Baixo |
| UX-FIM-Q16 | `query-suggestion.component.html:3-56` | Resposta sem reescrita, mudança nem índice fica vazia; o aviso de recusa não é anunciado | "A IA não achou o que melhorar nesta consulta" em `role="status"`; `role="status"` na recusa | Baixo | P | Médio |
| UX-FIM-Q17 | `query-analysis.component.ts:69-72` | Se o estado da IA recarrega como nulo, a seção some com a sugestão já recebida | Manter a sugestão na tela e esconder só o botão; o provedor sai da própria sugestão | Médio | M | Baixo |
| UX-FIM-C01 | `settings.page.html:34` | Build e testes do repositório só se configuram pelo terminal, removendo e cadastrando de novo | "Copiar" ao lado do comando já; campos de build e testes na tela pedem rota de edição e decisão (ADR-003 guarda o comando no terminal) | Médio | M | Alto |
| UX-FIM-C02 | `settings.page.html:78-79` | Falha ao carregar a configuração da IA sem "Tentar de novo" | O mesmo aviso dos repositórios, com o botão | Baixo | P | Alto |
| UX-FIM-C03 | `app.html:1` | Sem "Ir para o conteúdo"; o `<main>` não recebe foco | Link de pular no começo e `<main id="conteudo" tabindex="-1">` | Baixo | P | Alto |
| UX-FIM-C04 | `dialog.directive.ts`, `settings.page.html:73-76` | O Esc fecha diálogo só nas telas que tratam o atalho; "Remover o repositório?" não fecha com Esc | Esc tratado no `appDialog` | Médio | M | Alto |
| UX-FIM-C09 | `database.page.html:74-78` | "Nenhum banco encontrado" sem ação | Link para conferir a pasta nas configurações e "Analisar uma consulta mesmo assim" | Baixo | P | Alto |
| UX-FIM-C10 | `surveys.page.html:20, 107` | Primeiro uso: pede para escolher um repositório que não existe; "Levantar o fluxo" ativo sem repositório | Sem repositório, o estado vazio leva às configurações e o botão fica desabilitado | Médio | P | Alto |
| UX-FIM-C11 | `app.routes.ts:6-27` | Título da aba fixo por rota ("Banco \| Rastro AI" para qualquer repositório) | "Banco de loja-banco \| Rastro AI" pela `TitleStrategy` | Baixo | P | Médio |
| UX-FIM-C12 | `surveys.page.ts:192` | Só a lista de levantamentos leva o foco ao título ao trocar de rota | Foco no `h1` depois de cada navegação, num lugar só (`app.ts`) | Médio | M | Médio |
| UX-FIM-C13 | `settings.page.html:60`, `models.ts:414` | Exemplo de pasta só no formato do Windows | Os dois formatos no texto de ajuda e no erro | Baixo | P | Baixo |
| UX-FIM-C14 | `settings`, `surveys` e `survey` (SCSS) | Quatro estilos de "salvo" e pendência com a mesma regra; `.has-holes` duplicado | Subir para `_controls.scss` e documentar o padrão de aviso | Baixo | P | Médio |
| UX-FIM-C15 | `database.page.scss:6-8` | `[hidden] { display: none !important; }` só na tela do banco | Subir para `_base.scss` | Baixo | P | Baixo |
| UX-FIM-C16 | `app.scss:12-63` | Altura da barra e do selo da IA em pixels soltos | `--topbar-h`, `--radius-pill` e a altura de controle pequeno | Baixo | P | Baixo |
| UX-FIM-C17 | `diagram.page.html:33`, `survey.page.html:114` | "Refazer" anunciado como Ctrl+Y numa tela e Ctrl+Shift+Z na outra | Os dois atalhos nas duas telas, com o mesmo `title` | Médio | P | Baixo |
| UX-FIM-C18 | `surveys.page.html:110-112` | O link para o mapa do banco no levantamento novo parece texto de ajuda; falta um espaço no atributo | Sublinhado de link no tema e o espaço corrigido | Baixo | P | Médio |
| UX-FIM-N01 | `_database.scss` (bloco do mapa) | No PostgreSQL, 6 tipos ficam cortados no bloco (`character vary…`, `timestamp with…`), e o tamanho da coluna só aparece na dica | Tipo do bloco com o nome curto do catálogo (`varchar(120)`, `timestamptz`), o nome inteiro no painel | Médio | P | Alto |
| UX-FIM-N02 | `query-analysis.component.scss` | Em 1920, a aba de consulta ocupa 1360px à esquerda e deixa uma faixa vazia à direita | Centralizar o conjunto ou deixar a coluna lateral crescer | Baixo | P | Baixo |
| UX-FIM-Q07 | botões de IA (`query-analysis`, `c4-dialog`, `survey.page`, `node-panel`) | Rótulos diferentes para o mesmo gesto ("Sugerir melhora com a IA", "Pedir sugestão de refatoração à IA", "Propor destinos com IA"); "ver no mapa" em minúscula | Um padrão só, registrado no `design-system.md` | Alto (muda texto) | P | Médio |
| UX-FIM-Q15 | `query-analysis.component.html:3-7, 17` | "Nada vai a banco nenhum" é coloquial e não fala do plano estimado ao lado; "Veio do passo X" sem aspas | "A análise não consulta o banco." e o passo entre aspas | Alto (muda texto) | P | Baixo |
| UX-FIM-A01 | `live-connection-dialog.component.html:20-36` | O campo, que é o que a pessoa veio preencher, fica abaixo do parágrafo de permissões | Campo antes do texto de permissões | Alto (reordena) | P | Médio |

**Total:** 54 achados: 33 de risco baixo, 18 de risco médio e 3 de risco alto.

## Fases

Cada fase é commitável sozinha. Nenhuma foi executada.

### Fase A: estados, avisos e acessibilidade (risco baixo)

- [x] UX-FIM-B02 (feito na repaginação, Fase C: a tela avisa "Conexão removida").
- [ ] UX-FIM-B05, B06, B07, B13, B14, B15, B18, B19 e B20 (tela do banco e banco vivo). Conferidos no código de 2026-11-07: ainda valem. Do B18, o `role="status"` já é só do resumo; falta o título "Avisos do banco".
- [x] UX-FIM-Q01 (feito na repaginação, Fase A), Q05 (Fase A), Q09 (Fase A) e Q11 (Fase B).
- [ ] UX-FIM-Q03, Q04, Q08, Q10 e Q16 (consulta, IA e painel do passo). Ainda valem. Do Q08, o "Tentar de novo" já existe; falta o link para as configurações quando o erro é de chave ou de orçamento.
- [x] UX-FIM-C02 (feito na repaginação, Fase A), C09 (Fase A), C18 (Fase A) e N02 (Fase A).
- [ ] UX-FIM-C03, C11 e C13 (app). Ainda valem: sem "Ir para o conteúdo", título da aba fixo por rota e exemplo de pasta só do Windows.
- **Arquivos:** `pages/database/*`, `pages/survey/node-panel.component.html`, `pages/settings/settings.page.html`, `pages/surveys/surveys.page.html`, `app.html`, `app.routes.ts`.
- **Aceite:** sem IA, a aba de consulta diz como ligar a sugestão; remover a conexão dá aviso; toda ação de copiar e de pedir à IA é anunciada; o "Ir para o conteúdo" aparece no primeiro Tab; nenhum estado de erro sem "Tentar de novo".
- **Métricas-alvo:** 0 erro de console; 0 região de resultado sem `role="status"` ou `aria-live`.

### Fase B: tema e tokens (risco baixo, sem efeito visível)

- [ ] UX-FIM-B16, B17, Q13, Q14, C14, C15 e C16. Conferidos em 2026-11-07: ainda valem (`'FK'` no template, `[hidden]` só na tela do banco, `.has-holes` duplicado, altura da barra sem token).
- **Arquivos:** `theme/_tokens.scss`, `_controls.scss`, `_base.scss`, `_database.scss`, os SCSS dos componentes citados e `docs/frontend/design-system.md`.
- **Aceite:** sem pixel solto nos componentes do banco fora das medidas do mapa documentadas; sem string mágica no template do mapa; prints antes e depois iguais.
- **Métricas-alvo:** `database.page.scss` abaixo de 4,5 kB (hoje 4,7 kB).

### Fase C: banco vivo, foco e estado da tela do banco (risco médio)

- [x] UX-FIM-B01 (feito na repaginação, Fase B, B24) e B03 (Fase C, B23).
- [ ] UX-FIM-B04, B08, B09, B11 e B12. Ainda valem. No B12, o "←" já volta ao fluxo de origem (Fase B, B04) e as Configurações já voltam pelo histórico; falta o Esc do painel só na aba do mapa e o foco de volta ao "Exportar". O B09 só funciona para tabela: view e procedure deixam o foco no `body`.
- [x] UX-FIM-Q02 (feito na repaginação, Fase B, B12) e Q12 (`showInMap` foca o bloco da tabela).
- [ ] UX-FIM-Q17. Ainda vale.
- [x] UX-FIM-N01 (feito na repaginação, Fase B, B26: tipo curto no bloco, nome inteiro no `title` e no painel).
- **Arquivos:** `pages/database/*`, `core/database/db-map.ts` e um serviço de volta em `core/`.
- **Aceite:** salvar a conexão não apaga a consulta digitada; clique no fundo com texto no campo não fecha o diálogo; depois de "Analisar", o foco está no título do resultado; o "←" volta ao levantamento de origem; no PostgreSQL, nenhum tipo cortado na loja fictícia. Clique real no navegador para cada um.
- **Métricas-alvo:** 0 caso de foco no `body` depois de ação na tela do banco.

### Fase D: coerência do app (risco médio)

- [x] UX-FIM-C10 (feito na repaginação, Fase B, H09 e H10).
- [ ] UX-FIM-C04, C12 e C17. Ainda valem: o `appDialog` não trata Esc, só a lista de levantamentos leva o foco ao título e o "Refazer" ainda anuncia Ctrl+Y numa tela e Ctrl+Shift+Z na outra.
- [ ] UX-FIM-Q06 (componente único de aviso do que vai para o provedor).
- [ ] UX-FIM-B10 (banco grande: tabulação e atraso da busca).
- [ ] UX-FIM-C01, só o "Copiar" do comando; os campos de build e testes na tela pedem decisão do dono, porque a ADR-003 deixa o comando só no terminal.
- **Aceite:** o Esc fecha todo diálogo; no primeiro uso, a lista leva às configurações; o foco vai ao título a cada troca de rota; os dois atalhos de refazer nas duas telas.

### Fase E: alto risco, requer aprovação

Não é executada sem aprovação do dono, item a item.

- [ ] **UX-FIM-Q07:** um padrão só para o rótulo dos botões de IA, com "Ver no mapa" em maiúscula.
- [ ] **UX-FIM-Q15:** "A análise não consulta o banco." no lugar de "Nada vai a banco nenhum", e o passo de origem entre aspas.
- [x] **UX-FIM-A01:** no diálogo "Banco vivo", o campo antes do texto de permissões (feito na repaginação, Fase C, B23).

## Fora do escopo

- Os itens que já esperam aprovação no plano do banco: entrada "Banco" no cabeçalho (UX-DB-09), analisar sozinho ao chegar pelo passo (UX-DBQ-05), motivo do erro do provedor em todas as telas de IA (UX-DBA-06), mensagens dos leitores de SQL em pt-BR (UX-PG-05) e permissão mínima em lista com "Copiar" (UX-PGL-05).
- Os itens A1 a A9 da [revisão geral de 2026-10-30](plano-ux-2026-10-30-revisao-geral.md).
- Virtualização do mapa do banco para centenas de tabelas: fica com a pendência da Fase 38 até aparecer um caso real.

## Riscos

- **UX-FIM-B01** mexe no ciclo de carga da página do banco, que também atende a troca de repositório: o teste precisa cobrir as duas recargas.
- **UX-FIM-N01** troca o texto do tipo no bloco; a divergência entre fontes compara pelo nome do catálogo, então o nome curto é só de exibição e não pode entrar na comparação.
- **UX-FIM-C04** tira o Esc de cada tela e põe no `appDialog`: as telas que usam o Esc também para limpar a seleção (fluxo, banco) precisam continuar fazendo isso quando não há diálogo aberto.
- **UX-FIM-C12** muda o foco em toda navegação: conferir que o `?passo=` e o `?tabela=`, que já escolhem um item ao abrir, não perdem a seleção.
