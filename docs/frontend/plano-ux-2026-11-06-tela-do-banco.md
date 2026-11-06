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
