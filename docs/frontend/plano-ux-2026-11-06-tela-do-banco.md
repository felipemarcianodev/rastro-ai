# Levantamento de UX: tela do banco (Fase 38)

**Status:** EXECUTADO nos riscos baixo e médio; o item de risco alto espera aprovação.
**Escopo:** `frontend/src/app/pages/database/` (página, painel e tema `_database.scss`) e as duas entradas novas (levantamento novo e barra do levantamento).
**Data:** 2026-11-06. Análise do código e verificação no navegador (Chrome, paleta Papel no claro, larguras 1440 e 1280) com a loja fictícia, a API local e um `RASTRO_HOME` temporário.

## Diagnóstico

| Tela | Elementos visíveis | Profundidade do DOM | Botões primários |
|---|---|---|---|
| Mapa do banco (6 tabelas, painel aberto) | 259 | 10 | 0 (a tela não tem ação principal: é leitura) |

- Console sem erro nas visitas.
- Clique real conferido: bloco do mapa, item da lista, seleção do recorte (teclado), botão de zoom e os quatro itens do menu "Exportar" por cima do fundo que fecha o menu (`document.elementFromPoint` no centro de cada um).
- Ida e volta: a tela tem o "←" para os levantamentos, e se alcança pelo link no levantamento novo e pelo "Banco" na barra do levantamento.
- Estados: carregando, erro com "Tentar de novo", repositório sem banco no código e esquema só com views e procedures.

## Achados

| ID | Onde | Problema | Sugestão | Risco | Esforço | Destino |
|---|---|---|---|---|---|---|
| UX-DB-01 | `database.page.html`, lista de views e procedures | Item escolhido sem `aria-pressed` (as tabelas tinham) | Mesmo atributo das tabelas | Baixo | P | Feito |
| UX-DB-02 | bloco do mapa | Coluna com tipo divergente aparecia como texto comum ("divergente") no bloco | Mesma cor e peso do painel, com a dica "As fontes divergem" | Baixo | P | Feito |
| UX-DB-03 | lista, busca sem resultado | "Nenhuma tabela com esse nome", mas a busca também procura pelo campo | "Nenhuma tabela com esse nome ou campo" | Baixo | P | Feito |
| UX-DB-04 | painel, campos | "int" e "identidade" colados ("intidentidade") | Identidade e padrão em linha própria, em letra menor | Baixo | P | Feito |
| UX-DB-05 | painel, divergência | Caminho longo do arquivo estourava a largura do painel e criava rolagem lateral | Quebra em qualquer ponto (`overflow-wrap: anywhere`) | Baixo | P | Feito |
| UX-DB-06 | painel, achados | Quebra de linha sobrando entre o título e o texto do achado | Título em bloco, sem `<br>` | Baixo | P | Feito |
| UX-DB-07 | mapa | Sem zoom: num banco maior, o desenho não cabe e só rola | Zoom de 50% a 150% na barra do mapa, com a volta a 100% | Médio | M | Feito |
| UX-DB-08 | bloco do mapa | O nome "email" ficava cortado ao lado do selo "pessoal" | Bloco de 272px | Baixo | P | Feito |
| UX-DB-09 | cabeçalho do Rastro | A tela só se alcança escolhendo o repositório antes (levantamento novo ou barra de um levantamento) | Entrada "Banco" no cabeçalho | Alto (mexe no menu) | M | `[ ]` no plano, espera aprovação |

## Fora do escopo

- A aba "Analisar consulta" (Fase 40) e o "Fluxos que usam esta tabela" (Fase 39).
- Enquadrar e minimapa para bancos com centenas de tabelas: registrado no plano até aparecer um caso real.

## Fase 39: banco nos fluxos

**Escopo:** o painel da tabela e da procedure ("Fluxos que usam esta tabela", achados dos fluxos), o selo do bloco e a seção "No banco" do painel do passo. Verificação no navegador com dois levantamentos de verdade da loja fictícia.

- Clique real conferido: o link do passo no painel da tabela abre o levantamento com o passo escolhido, e o link da tabela no "No banco" do passo volta ao mapa com a tabela escolhida. Console sem erro.

| ID | Onde | Problema | Sugestão | Risco | Esforço | Destino |
|---|---|---|---|---|---|---|
| UX-DBF-01 | bloco do mapa e lista | Com achado de fluxo junto, o selo virava "2 achados" e perdia o nome "FK sem índice" | Nome do achado do esquema com "+N" dos outros | Baixo | P | Feito |
| UX-DBF-02 | abertura por `?tabela=` | A tabela vinda do passo ganhava o foco do teclado ao abrir a tela | Só escolher e trazer para a vista, sem foco | Baixo | P | Feito |
| UX-DBF-03 | painel da tabela | Operação, link do passo e linha quebravam em três linhas | O link ocupa o espaço que sobra; a linha fica numa coluna só | Baixo | P | Feito |
| UX-DBF-04 | lista de tabelas | O selo "FK sem índice +1" quebrava em duas linhas | Selo numa linha só | Baixo | P | Feito |
| UX-DBF-05 | grafo do fluxo | A tabela só aparecia no painel do passo; no nó, só a etiqueta "banco" | A dica e o rótulo de acessibilidade da etiqueta dizem a operação e a tabela ("Lê pedidos") | Médio | P | Feito; o nome no próprio bloco fica `[ ]` no plano |

## Fase 40a: aba "Analisar consulta"

**Escopo:** a aba nova da tela do banco (`query-analysis.component`), as abas da página e o link "Analisar a consulta" no painel do passo. Verificação no navegador (Chrome, paleta Papel no claro, largura 1440) com a loja fictícia, um levantamento de verdade com SQL cru e a API local com `RASTRO_HOME` temporário. Clique real no link do passo, no "Analisar", no achado, no "ver no mapa", na aba e no "Editar a consulta"; Ctrl+Enter conferido.

| ID | Onde | Problema | Sugestão | Risco | Esforço | Destino |
|---|---|---|---|---|---|---|
| UX-DBQ-01 | aba, coluna da consulta | Em 1440px, a caixa e os achados ocupavam a largura toda, com linhas de motivo longas demais para ler | Coluna da consulta até 960px, com "Tabelas da consulta" logo ao lado | Baixo | P | Feito |
| UX-DBQ-02 | "Tabelas da consulta" | A tabela do esquema é um botão para o mapa, mas parecia texto comum | Sublinhado de link | Baixo | P | Feito |
| UX-DBQ-03 | consulta sem achado | "0 achados" no título e "Nenhum achado nesta consulta" logo abaixo | Título "Sem achados" | Baixo | P | Feito |
| UX-DBQ-04 | origem da consulta | "Veio do passo ..." continuava depois de trocar o texto por outra consulta | A origem só aparece enquanto o texto é o do passo | Médio | P | Feito |
| UX-DBQ-05 | entrada pelo passo | Quem chega pelo "Analisar a consulta" do passo ainda precisa clicar em "Analisar" | Analisar sozinho ao chegar pelo passo | Alto (muda o fluxo aprovado no protótipo, que tem o botão) | P | `[ ]` no plano, espera aprovação |

## Fase 40b: banco vivo

**Escopo:** o diálogo "Banco vivo", o selo e a promessa da barra e o cartão "Plano estimado pelo banco". Verificação no navegador (Chrome, Papel no claro, 1440) com um SQL Server descartável em contêiner e um usuário só de leitura; clique real em "Conectar banco vivo", "Salvar", "Testar conexão", "Analisar" e "Pedir o plano estimado".

| ID | Onde | Problema | Sugestão | Risco | Esforço | Destino |
|---|---|---|---|---|---|---|
| UX-DBL-01 | cartão do plano | A falha do banco mostrava só "O banco vivo não respondeu", sem o motivo (era permissão) | Mostrar o motivo que a API escreve (title do problema), como no plano recusado | Baixo | P | Feito |
| UX-DBL-02 | diálogo, permissão mínima | O texto prometia que `VIEW DEFINITION` e `SHOWPLAN` bastavam, e o plano falhava por falta do `SELECT` | Texto corrigido: catálogo com `VIEW DEFINITION`; plano com `SHOWPLAN` e o `SELECT` nas tabelas da consulta | Médio | P | Feito |
| UX-DBL-03 | cartão do plano | Coluna de 320px quebrava o nome do operador e o objeto em três linhas | Coluna lateral até 400px | Baixo | P | Feito |
| UX-DBL-04 | cartão do plano | Operador com 0% ("Laços aninhados") ocupa linha sem dizer nada | Esconder os operadores abaixo de 1% atrás de "mais N operações" | Médio | P | Feito |

## Fase 41: a IA sugere a melhora

**Escopo:** a seção "Sugestão da IA" da aba "Analisar consulta" (`query-analysis.component`) e o resultado (`query-suggestion.component`). Verificação no navegador (Chrome, Papel no claro, janela de 1920) com a loja fictícia e a API local com `RASTRO_HOME` temporário; clique real em "Analisar", "Sugerir melhora com a IA", "Pedir outra sugestão" e "Copiar a consulta reescrita". O caminho de erro foi com uma chave falsa de teste (o provedor recusou, nada foi gasto); a resposta aceita e a recusada foram respostas da IA simuladas, conferidas pelo núcleo de verdade. Com a IA desligada, a tela não tem botão de IA. O comparativo de custo (banco vivo) ficou só nos testes, sem SQL Server nesta verificação.

| ID | Onde | Problema | Sugestão | Risco | Esforço | Destino |
|---|---|---|---|---|---|---|
| UX-DBA-01 | seção da sugestão | A resposta (e o erro) chegava abaixo dos achados, fora da vista: parecia que nada tinha acontecido | Rolar até o começo da seção quando a resposta chega | Médio | P | Feito |
| UX-DBA-02 | código (tema) | A ligadura da fonte de código desenhava `>=` como `≥` na consulta reescrita: quem lê copiaria outro texto | `font-variant-ligatures: none` no `code` e no `.code` do tema | Baixo | P | Feito |
| UX-DBA-03 | estado da reescrita e do índice | "Reescrita aceita pela conferência" e "Aceito", com borda, pareciam botões ao lado do "Copiar" | Estado em texto, sem borda | Baixo | P | Feito |
| UX-DBA-04 | reescrita recusada | A lista "Mudanças" descrevia o texto recusado como se valesse | Título "Mudanças da reescrita recusada" | Baixo | P | Feito |
| UX-DBA-05 | botão | Com uma sugestão na tela, o botão continuava "Sugerir melhora com a IA" | "Pedir outra sugestão" quando já há uma | Baixo | P | Feito |
| UX-DBA-06 | erro do provedor (todas as telas de IA) | `AI_PROVIDER_ERROR` mostra sempre "O provedor de IA não respondeu", e o motivo que o Rastro escreve (chave inválida, limite de uso) se perde; as mensagens dos provedores ainda falam em "nó" | Mostrar o title do problema, como no plano recusado, e tirar "nó" das mensagens dos provedores | Médio (muda todas as telas de IA, fora do escopo desta fase) | P | `[ ]` no plano |

## Fase 42a: PostgreSQL pelo código

**Escopo:** o mapa do banco e a aba "Analisar consulta" com um repositório PostgreSQL (loja fictícia `LojaBancoPg`). Verificação no navegador (Chrome, Papel no claro, 1440) com a API local e `RASTRO_HOME` temporário; clique real na aba "Analisar consulta", em "Analisar" e em "Editar a consulta". Sem banco vivo (Fase 42b).

| ID | Onde | Problema | Sugestão | Risco | Esforço | Destino |
|---|---|---|---|---|---|---|
| UX-PG-01 | rótulo da caixa da consulta | Dizia "Consulta SQL (SQL Server)" num repositório PostgreSQL | O dialeto do repositório no rótulo | Baixo | P | Feito |
| UX-PG-02 | erro de leitura | "O leitor de T-SQL não entendeu" numa consulta do PostgreSQL | O leitor do dialeto da análise | Baixo | P | Feito |
| UX-PG-03 | painel da tabela, índices | A chave sem nome escrito aparecia como "chave primária (id), chave primária" | O nome que o PostgreSQL dá (`produtos_pkey`), o mesmo do catálogo | Baixo | P | Feito |
| UX-PG-04 | título do achado | "Função na coluna do filtro" para a conversão `::date` | "Função ou conversão na coluna do filtro"; o curinga vira "LIKE ou ILIKE" | Baixo | P | Feito |
| UX-PG-05 | erro de leitura | A mensagem do leitor do PostgreSQL vem em inglês ("Expected an expression, found ="), como a do leitor de T-SQL | Traduzir as mensagens mais comuns dos dois leitores | Médio (muda o texto dos dois dialetos) | M | `[ ]` no plano |
