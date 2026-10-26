# Plano de UX: telas tocadas pela Fase 12 (2026-10-16)

**Status:** EXECUTADO em 2026-10-16 (Fases E, F e G; a G com aprovação do dono).

**Escopo:** o que a Fase 12 acrescentou. Na página do levantamento e no canvas: busca, recolher, caminhos, menus "Comparar com…" e "Exportar", comparação, layout vertical e marcas do nó. No painel: anotação e "Abrir no VS Code". Também o cartão do editor nas Configurações e o rodapé do diálogo de validação. Levantamento estático do código (padrão `ux-refactor-process`) feito por 2 agentes de leitura.

## Diagnóstico

### Métricas (estimadas a partir do código)

| Tela | Elementos visíveis ao mesmo tempo | Botões primários |
|---|---|---|
| Barra superior, leitura (completo, desatualizado, com validação e rascunho) | 14 (7 botões) | 1 |
| Menu de contexto do passo | título e até 6 itens (~250px) | 0 |
| Faixas acima do canvas com tudo ligado (foco, caminho, recolhido, comparação, busca) | até 6 faixas, ~300px; canvas com ~260px em 1280×720 | 0 |
| Painel com nó cheio | ~50 (~15 botões); 55 a 60 na edição | 0 |
| Rodapé do diálogo de validação | até 4 ações e o × | 1, às vezes 2 (UX-136) |

### Os 10 problemas de maior impacto

1. **Menus suspensos não recebiam clique** (UX-90): o fundo que fecha o menu cobria a lista. **Corrigido** em `34846a1`, confirmado com clique real.
2. **Buscar seleciona a cada tecla** (UX-101): move a câmera, troca o painel e pode gravar o destino de um caminho sem querer.
3. **Faixas empilhadas** (UX-91): com vários modos ligados, sobra pouco grafo.
4. **Barra superior estoura em 1280** (UX-93): 9 itens sem quebra de linha.
5. **Editar a anotação perde o texto em erro** (UX-123): o editor fecha antes da resposta.
6. **Apagar anotação sem confirmação** (UX-121).
7. **Pasta errada do editor não se percebe** (UX-129, UX-130): o link sai e nada acontece.
8. **Resultados da busca apagados pela trilha** (UX-102).
9. **Esc fecha a coisa errada** (UX-94) e menus suspensos sem teclado (UX-95).
10. **Mesmo azul para busca, passo novo e decisão** (UX-104), e a legenda sem os modos da Fase 12.

## Achados

| ID | Onde | Problema | Sugestão | Risco | Esforço | Impacto |
|---|---|---|---|---|---|---|
| UX-90 | `theme/_controls.scss` | Fundo do menu cobria os itens | `.popover-menu` acima do fundo | médio | P | alto (**feito**) |
| UX-91 | `survey.page.html` faixas | Até 6 faixas empilhadas | Uma faixa de "filtros ativos" com chips | alto | M | alto |
| UX-92 | `flow-canvas.component.scss` | Coluna de ferramentas cruza a legenda em canvas baixo | Botões de texto numa linha ao lado dos ícones | médio | M | médio |
| UX-93 | `survey.page.html` `.bar-side` | 9 itens sem quebra em 1280 | Juntar Comparar e Exportar num menu; `flex-wrap` | alto | M | alto |
| UX-94 | `survey.page.ts` `onEscape` | Esc limpa a busca com o menu aberto; não fecha "Comparar com…" | Fechar primeiro o que está por cima | baixo | P | médio |
| UX-95 | `survey.page.html` menus suspensos | Sem setas, sem foco no 1º item, foco não volta | Reaproveitar as setas do menu de contexto | baixo | P | médio |
| UX-96 | `survey.page.ts` `openMenu` | Menu de contexto sai da janela | Medir depois de abrir e abrir para cima ou para a esquerda | baixo | P | médio |
| UX-97 | canvas ⤢, menu e faixa de foco | "Mostrar o grafo inteiro" nomeia duas ações | ⤢ vira "Enquadrar o grafo na tela" | alto | P | médio |
| UX-98 | canvas ⇊/⇉ | `aria-pressed` com rótulo que muda | Rótulo fixo com `aria-pressed` | baixo | P | baixo |
| UX-99 | canvas `nodeAria` | Leitor de tela não ouve novo/saiu, achados, anotação, busca | Sufixos no `aria-label` do nó | baixo | P | médio |
| UX-100 | nó "+N passos" | Pouco clicável: sem glifo e sem dica | Glifo ⊕ e título "Expandir N passos" | baixo | P | médio |
| UX-101 | `survey.page.ts` `onSearchInput` | Cada tecla seleciona; pode gravar o destino do caminho | Destacar ao digitar, selecionar só com Enter | médio | P | alto |
| UX-102 | canvas `nodeClasses` | Resultado da busca fica apagado pela trilha | Busca vence o apagado | baixo | P | médio |
| UX-103 | faixa da busca | "Nenhum passo" sem dizer que há filtro ativo | Avisar recorte, recolhido e chamadas .NET escondidas | baixo | M | médio |
| UX-104 | `_flow.scss`, legenda | Mesmo azul em busca, passo novo e decisão; legenda sem a Fase 12 | Busca na cor da trilha com contorno duplo; legenda mostra o modo ativo | baixo | P | médio |
| UX-105 | menu "Comparar com…" | Sem carregando nem erro | Estado no próprio menu | baixo | P | médio |
| UX-106 | exportar | Retorno inconsistente; o parágrafo nunca some | Toast único com tempo | baixo | P | médio |
| UX-107 | barra e canvas | "Não resolvidos" duas vezes com a mesma ação | Manter só o do canvas | alto | P | baixo |
| UX-108 | itens de menu | Foco do teclado só com fundo cinza | Anel de foco no `:focus-visible` | baixo | P | médio |
| UX-109 | `survey.page.ts` `nodeLabel` | `find` em todo ciclo; 3 Maps por seleção | `computed` com Map de rótulos | baixo | M | médio |
| UX-120 | painel | Anotação entre o título e o símbolo | Depois da regra, antes do arquivo | alto | P | alto |
| UX-121 | painel | "Apagar anotação" sem confirmação | "Anotação apagada. Desfazer" | médio | P | alto |
| UX-122 | painel | Salvar vazio apaga sem avisar | Salvar desabilitado com texto vazio | médio | P | médio |
| UX-123 | painel | Editor fecha antes da resposta; erro perde o texto | Fechar só no sucesso | médio | M | alto |
| UX-124 | painel | Foco cai no body ao fechar a edição | Devolver ao botão | baixo | P | médio |
| UX-125 | painel | Mensagem da anotação não é anunciada nem some | Região viva fixa e sucesso some em 3 s | baixo | P | médio |
| UX-126 | painel, edição | Dois textareas iguais seguidos | Estilo próprio da nota no editor | médio | P | médio |
| UX-127 | `editor-links.ts` | "Salvo" mesmo quando não salvou ou apagou | Resultado salvo, removido ou só nesta visita | baixo | P | médio |
| UX-128 | Configurações | Mensagem toma o lugar da ajuda e não some | Ajuda fixa e status separado | baixo | P | baixo |
| UX-129 | `editor-links.ts` | Pasta que não é absoluta passa | Avisar na configuração; pasta no title do botão | baixo | P | alto |
| UX-130 | painel | "Abrir no VS Code" sem retorno | "Abrindo no VS Code…" e dica se não abrir | baixo | P | médio |
| UX-131 | Configurações | Cartão do editor some se a lista falhar | Estado de erro com "Tentar de novo" | baixo | P | médio |
| UX-132 | painel | Link da dica vai ao topo das Configurações | Âncora no cartão do editor | baixo | P | médio |
| UX-133 | painel | Dica do editor em todo nó, sempre | Dispensar a dica | alto | P | baixo |
| UX-134 | painel e menu | "Copiar" e "Copiar arquivo:linha e método" copiam coisas diferentes | Mesmo texto e conteúdo | alto | P | médio |
| UX-135 | painel | "Fechar" em texto, diálogos em ×; explicação só no title | Padronizar | alto | P | baixo |
| UX-136 | diálogo de validação | Dois primários possíveis | "Validar de novo" secundário com documento | baixo | P | médio |
| UX-137 | diálogo de validação | Dois links para a mesma rota | Juntar ou texto visível | alto | M | médio |
| UX-138 | exportar | Falha em silêncio sem canvas; "Baixado" sem garantia | Erro visível e "Download iniciado" | baixo | P | baixo |
| UX-139 | `survey.page.scss` | Painel de 460px fixo em 1280 e 1920 | `clamp(420px, 28vw, 600px)` | baixo | P | médio |

## Fases

### [x] Fase E: baixo risco (aria, foco, feedback, estados, estilo)
**Executada:** Esc fecha de cima para baixo (diálogos, menus, busca, caminho, seleção, recorte); menus "Comparar com…", "Exportar" e "Mais" com foco no 1º item, setas e foco de volta ao botão; menu de contexto medido depois de abrir; ⇊ com rótulo fixo; `aria-label` do nó com mudança, achados, anotação e busca; "+N passos" com ⊕; resultado da busca não apaga; busca avisa os passos escondidos e oferece "Buscar em tudo"; contorno da busca em tinta e legenda com os modos ligados; "Comparar com…" com carregando e erro; retorno de exportar num aviso que some, com erro quando o grafo não está pronto e "Download iniciado"; anel de foco nos itens de menu; rótulos por `computed`; pasta do editor recusa caminho que não é completo e diz se salvou, removeu ou vale só nesta visita; cartão do editor sempre visível com estados e âncora `#editor` (o painel leva direto ao campo); "Abrindo no VS Code…" com a dica; um primário só no rodapé da validação; painel com `clamp(420px, 28vw, 600px)`. Os itens da anotação (UX-124, UX-125) foram com a Fase F, por estarem no mesmo bloco.
IDs: UX-94, UX-95, UX-96, UX-98, UX-99, UX-100, UX-102, UX-103, UX-104, UX-105, UX-106, UX-108, UX-109, UX-124, UX-125, UX-127, UX-128, UX-129, UX-130, UX-131, UX-132, UX-136, UX-138, UX-139.
Aceite: build e testes verdes; Esc fecha de cima para baixo; menus suspensos por setas; nenhuma mensagem de exportar ou da pasta fica para sempre; um primário no rodapé da validação.

### [x] Fase F: médio risco (interação nova)
**Executada:** digitar na busca só destaca e centraliza o atual (sem trocar o painel); Enter escolhe; busca e "próximo não resolvido" não gravam o destino de um caminho; anotação fecha só quando salvar dá certo (erro mantém o texto), foco volta ao botão, "Salvar" desabilitado com campo vazio, apagar mostra "Desfazer" por 8 s, região viva fixa e sucesso some em 3 s, editor com a cara da nota (UX-121 a UX-126); botões de texto do canvas numa linha ao lado dos ícones (UX-92). Estilos da anotação subiram para o tema (orçamento de 6 kB do painel).
IDs: UX-92, UX-101, UX-121, UX-122, UX-123, UX-126.
Aceite: digitar na busca não seleciona nem grava destino de caminho; apagar anotação tem desfazer; erro ao salvar mantém o texto.

### [x] Fase G: alto risco, aprovada pelo dono em 2026-10-16 (todos os itens)
**Executada:** recorte, comparação, caminho e recolhidos numa faixa só, em chips com × e nomes cortados com reticências (UX-91); "Comparar com…" e "Exportar" num menu "Comparar e exportar", com o grupo Exportar primeiro para o foco não pular quando a lista chega, e a barra quebra linha (UX-93); anotação depois da regra (UX-120); um link só no rodapé da validação, "Abrir o fluxo novo comparado" (UX-137); contagem de não resolvidos volta a texto (UX-107); "Não mostrar de novo" na dica do editor (UX-133); ⤢ é "Enquadrar o grafo na tela" e o painel diz "Mostrar o grafo inteiro" (UX-97); "Copiar arquivo:linha" igual no painel e no menu (UX-134); painel fecha com × como os diálogos e a explicação de "Ver somente este fluxo" vai para o leitor de tela (UX-135). Junto, corrigido o modo comparação que escondia os achados de qualidade.
- **Reorganizar:** UX-91 (faixa única de filtros ativos), UX-93 (juntar Comparar e Exportar), UX-120 (anotação depois da regra), UX-137 (um link só no rodapé da validação).
- **Esconder ou remover:** UX-107 ("Não resolvidos" só no canvas), UX-133 (dispensar a dica do editor).
- **Texto:** UX-97 ("Enquadrar o grafo na tela"), UX-134 (mesmo "Copiar" no painel e no menu), UX-135 (fechar do painel).

## Fora do escopo
- Telas que a Fase 12 não tocou (lista, C4 e prompt continuam no plano de 2026-10-16, já executado).
- Redesign e paleta.

## Riscos
- UX-101 muda o hábito da busca (Enter passa a ser necessário para ir ao passo).
- UX-104 troca a cor do contorno da busca; quem já usou vai estranhar uma vez.
