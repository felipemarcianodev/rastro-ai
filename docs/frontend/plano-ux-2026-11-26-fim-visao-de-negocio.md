# Plano de UX: fim do plano da visão de negócio (Fases 54 a 61)

**Status:** EM EXECUÇÃO (as Fases A, B e C entram no branch `feat/ux-visao-de-negocio`; a Fase D espera a aprovação do dono, item a item).
**Data:** 2026-11-26.
**Escopo:** tudo o que as Fases 54 a 61 criaram ou mudaram na tela: a visão de negócio (`business-view.component.*`, `_business.scss`), os quadros (`boards.page`, `board.page`, `board-create-dialog`, `add-to-board-dialog`, a criação pela lista de levantamentos) e o que mudou na página do levantamento (seletor de desenho, menu Exportar com draw.io, PNG, SVG e Mermaid, "Adicionar a um quadro", "Mostrar na visão de negócio" no menu do nó). Substitui as Fases B e C que sobraram do [plano de UX da Fase 57](plano-ux-2026-11-26-visao-de-negocio.md); a Fase D de lá continua valendo e foi repetida aqui.

## Cobertura

| Tela ou funcionalidade | Lido no código | Visto na tela | Fora e por quê |
|---|---|---|---|
| Lista de quadros (vazia e com um quadro) | sim | sim, 1920 | |
| Criar quadro pela lista de levantamentos (caixas, barra, diálogo) | sim | sim, clique de verdade, 1920 | |
| Tela do quadro (faixas, setas, renomear, exportar, glossário) | sim | sim, 1920, 1280 e 360 | arrastar a ordem pelo mouse não foi feito |
| Visão de negócio do levantamento | sim | sim, 1920, 1280 e 360 | painel do passo visto só no quadro (o levantamento usado não tinha passo-chave) |
| Menu Exportar no grafo, no mapa e na visão de negócio | sim | sim, o PNG no mapa | o `.drawio` já foi aberto no draw.io nas Fases 54 e 61 |
| "Adicionar a um quadro" | sim | sim, 1920 | |
| "Mostrar na visão de negócio" no menu do nó | sim | não | coberto pelo teste do componente e pela Fase 59 |
| Sugestão de textos pela IA | sim | não | IA desligada nesta máquina |
| Console | | um 404 por levantamento (`draft` e `diagram` sem rascunho salvo) | anterior a estas fases, fora do escopo |

A passada no navegador usou o Chrome instalado pelo Playwright (a extensão do Chrome não estava conectada), com clique de verdade pelo mouse no centro do elemento e o `document.elementFromPoint` conferido antes de cada clique. Dados: três levantamentos do próprio Rastro (`SettingsController.SetLanguage`, `SurveysController.Create`, `RepositoriesController.Add`) numa pasta de dados temporária (`RASTRO_HOME`), sem tocar no `~/.rastro`.

## Confiabilidade

O coordenador conferiu na tela, com clique de verdade: o foco caindo no `body` depois da seta da faixa, do "Renomear" e do Esc no menu Exportar do quadro (UX-FIM-201, 202, 203); as faixas com 560 px numa área de 1920 (UX-FIM-105); os pontos de dobra candidatos sempre à vista (UX-FIM-001); o PNG no mapa por sistemas falhando com "O grafo ainda não está desenhado" e o aviso por cima do seletor de desenho (UX-FIM-208); o Esc que não fecha o "Renomear" (UX-FIM-202); os rótulos de caso do `switch` sobrepostos e em código (UX-FIM-213, D-10); e a criação do quadro, que desta vez foi para o quadro novo. O resto vem da leitura do código por três agentes, com a linha citada, e foi conferido por amostragem. Sem usuário real nem dados de uso, os achados são prováveis, não comprovados.

## Descoberta

| Funcionalidade | Entrada | Cliques da tela inicial |
|---|---|---|
| Visão de negócio | botão "Visão de negócio" no seletor de desenho do levantamento | 2 |
| Exportar para o draw.io | menu Exportar do levantamento ou do quadro | 3 |
| Criar quadro | caixas na lista de levantamentos e "Criar quadro", ou "Quadros" no cabeçalho e "Novo quadro" | 3 a 4 |
| Adicionar um levantamento a um quadro | botão "Adicionar a um quadro" no levantamento | 3 |
| Editar o texto de um passo | clique no balão e campo do painel, ou duplo clique no balão | 4 |

Nenhuma entrada depende só do hover. A criação do quadro pela lista é pouco achável: a caixa de seleção não tem rótulo visível e a barra "Criar quadro" só aparece depois da primeira caixa marcada (D-06).

## Diagnóstico

Métricas por tela (1920):

| Tela | Botões primários visíveis | Observação |
|---|---|---|
| Quadro | 0 | quatro botões iguais na barra, "Excluir quadro" ao lado de "Exportar" |
| Visão de negócio | 1 ("Editar fluxo", da página) | |
| Lista de quadros | 1 ("Novo quadro") | |

Os dez principais problemas:

1. UX-FIM-201 a 204: o foco se perde em quase toda ação da tela do quadro (seta, renomear, menu Exportar, tirar faixa).
2. UX-FIM-001: os pontos de dobra candidatos poluem toda ligação.
3. UX-FIM-208: no mapa por sistemas, PNG, SVG e Mermaid ficam no menu e falham com mensagem falsa.
4. UX-FIM-101: nenhum aviso depois de renomear, adicionar, tirar ou mover faixa.
5. UX-FIM-002: o aviso de erro da visão de negócio tem cara de sucesso.
6. UX-FIM-105: faixas de largura fixa sobrando metade da tela em 1920.
7. UX-FIM-206: a página às vezes não vai para o quadro recém-criado e a seleção já foi apagada.
8. UX-FIM-003: o texto secundário do balão (referência) não passa de 4,5:1 em quatro tipos no tema claro.
9. UX-FIM-102: o aviso de erro some em 4 s, no quadro e no levantamento.
10. UX-FIM-004: painel do passo sem regra para tela estreita nem limite em tela larga.

## Achados

Origem: NB (agente da visão de negócio), QD (agente dos quadros), LV (agente do levantamento) e o coordenador no navegador.

### Visão de negócio

| ID | Onde | Problema | Sugestão | Risco | Esforço | Impacto |
|---|---|---|---|---|---|---|
| UX-FIM-001 | `business-view.component.html` (`f-connection-waypoints`) | pontos de dobra candidatos sempre à vista (NB-001, visto na tela) | candidatos só no hover da ligação e com foco no desenho; os pontos reais ficam | baixo | P | alto |
| UX-FIM-002 | `_controls.scss` (`.toast`) | `.toast.is-error` só existe dentro de `.toast.is-fixed`; o erro parece sucesso (NB-004) | `is-error` em qualquer `.toast` | baixo | P | alto |
| UX-FIM-003 | `business-view.component.scss` (`.biz-ref`) | `--ink-soft` sobre o fundo de recusa, início, fim e notificação no claro: 3,93 a 4,66 (UX-BIZ-006) | tinta por tipo (`--biz-*-ink`) com opacidade que passe de 4,5:1, ou token novo | baixo | P | médio |
| UX-FIM-004 | `_business.scss` (`.biz-panel`) | painel sem `max-width` em tela larga e sem regra abaixo de 40rem (UX-BIZ-014, NB-009) | `max-height: min(45%, 22rem)`, uma coluna em tela estreita, conteúdo até 72rem | baixo | M | médio |
| UX-FIM-005 | `business-view.component.html` | nota do resumo com `role="note"` fora de região viva; `aria-describedby` da ajuda em cada balão; `aria-live` no título inteiro; portas do Foblex sem `aria-hidden` (UX-BIZ-017, NB-010, 012, 017) | `role="status"` na nota; ajuda só no desenho; anúncio curto oculto; `aria-hidden` nas portas | baixo | P | médio |
| UX-FIM-006 | `business-view.component.ts` (`open`) | "Abrir no host" e "Abrir no editor" sem retorno (UX-BIZ-010) | aviso "Abrindo…" e aviso de recusa | baixo | P | médio |
| UX-FIM-007 | `business-view.component.ts` (`flushSave`) | salvar posição sem retorno de sucesso (NB-006) | "Posição salva" discreto, em região `role="status"` | baixo | P | médio |
| UX-FIM-008 | `business-view.component.ts` (sugerir) | o aviso "N propostas" sai antes de o fluxo recarregar (NB-008) | anunciar depois de aplicar o fluxo | baixo | P | médio |
| UX-FIM-009 | `business-view.component.scss` e `_business.scss` | medidas soltas (6px, 12px, 1.5px, anel de 3px repetido) (NB-014) | tokens `--space-*` e um token para o anel | baixo | P | baixo |
| UX-FIM-010 | `business-view.component.ts` | `KIND_DETECTED` é texto misturado com número; `syncScale` com `setTimeout` que não se limpa (NB-019, NB-020, UX-BIZ-020) | valor numérico; timer guardado e limpo | baixo | P | baixo |
| UX-FIM-011 | `_business.scss` (`.biz-badge` no painel) | selo do painel em `--text-2xs` itálico (NB-015) | `--text-xs` no painel | baixo | P | baixo |
| UX-FIM-012 | `survey.page.html` (seletor) | descrição de cada desenho só no `title` (UX-BIZ-019, LV-015) | `aria-describedby` com o mesmo texto | baixo | P | baixo |

### Quadros

| ID | Onde | Problema | Sugestão | Risco | Esforço | Impacto |
|---|---|---|---|---|---|---|
| UX-FIM-101 | `board.page.ts` (`move`, `renameLane`, `confirmRemove`, `submitAdd`, `commitRename`) | nenhum aviso depois da ação (QD-005, QD-003) | avisos novos "Quadro renomeado", "Faixa adicionada", "Faixa tirada do quadro", "Faixa movida para a posição N" | baixo | P | alto |
| UX-FIM-102 | `board.page.ts` e `survey.page.ts` (`showFeedback`, `showExport`) | aviso de erro some em 4 s (QD-011, LV-003) | erro fica até fechar; sucesso continua em 4 s | baixo | P | médio |
| UX-FIM-103 | `board.page.html` | "Abrir", "Tirar do quadro", "Levantar de novo" e o título repetidos em cada faixa, sem o nome dela (QD-006) | `aria-label` com o título da faixa | baixo | P | médio |
| UX-FIM-104 | `board-create-dialog`, `add-to-board-dialog` | erros sem `role="alert"` nem `aria-describedby`; foco não vai ao campo com erro; "Criar quadro" ativo sem levantamento pronto (QD-014, LV-011) | ligar erro e dica, focar o campo, desabilitar sem levantamento | baixo | P | médio |
| UX-FIM-105 | `board.page.scss` (`.lane`) | faixa com 560 px fixos; em 1920 sobra metade da tela (QD-002, visto na tela) | faixa cresce até um máximo (tokens `--lane-min` e `--lane-max`) | baixo | P | alto |
| UX-FIM-106 | `boards.page.ts` (`openCreate`) | "Novo quadro" busca os levantamentos sem estado de carregando (QD-015) | botão desabilitado com "Abrindo…" | baixo | P | médio |
| UX-FIM-107 | `board.page.html` (glossário) | a contagem do resumo segue o rascunho e parece salva; nada avisa alteração não salva (QD-018) | contagem pelo salvo e "Alterações não salvas" | baixo | P | médio |
| UX-FIM-108 | `board.page.ts` (efeito da rota) | ao trocar de quadro, erro, aviso e "levantado de novo" do anterior continuam (QD-023) | zerar o estado no efeito | baixo | P | baixo |
| UX-FIM-109 | `board.page.html` | dois padrões de aviso na mesma tela (cópia do Mermaid sem "Fechar") (QD-024) | usar o mesmo aviso | baixo | P | baixo |
| UX-FIM-110 | `board.page.ts` (exportar) | sem faixa desenhada, os itens ficam ativos e dão erro; prefixo do arquivo em texto fixo (QD-010) | itens desabilitados com a dica; prefixo em constante | baixo | P | médio |
| UX-FIM-111 | `board.page.html` (voltar) | a seta de voltar não tem `title` (QD-021) | `title` com "Voltar aos quadros" | baixo | P | baixo |
| UX-FIM-112 | `board.page.ts` e `boards.page.ts` (excluir) | excluir pela tela do quadro não avisa na lista; erro de exclusão na lista esconde os quadros (QD-013) | aviso pela navegação; erro de ação separado do erro de carga | baixo | M | médio |
| UX-FIM-113 | `board.page.html` (estado da faixa) | "Levantando…" sem o nome da faixa, uma região viva por faixa (QD-019) | título no texto e uma região só | baixo | M | baixo |
| UX-FIM-114 | `_boards.scss`, `board.page.scss` | medidas soltas (2px, 3px, 56px, larguras) (QD-020) | tokens | baixo | P | baixo |

### Página do levantamento

| ID | Onde | Problema | Sugestão | Risco | Esforço | Impacto |
|---|---|---|---|---|---|---|
| UX-FIM-151 | `survey.page.ts` (`downloadDrawio`) | sem a rota de links, o `.drawio` sai sem link e o aviso diz só "Download iniciado" (LV-004) | aviso próprio "sem os links para o código" | baixo | P | médio |
| UX-FIM-152 | `survey.page.html` | "Adicionar a um quadro" e "Exportar" sem `aria-haspopup` (LV-013, QD-009) | `aria-haspopup="dialog"` e `"menu"` | baixo | P | baixo |
| UX-FIM-153 | `survey.page.scss`, `node-panel.component.scss` | orçamento de 6 kB estourado (7560 B e 7544 B) (LV-008) | toast, segmentado e popover para o tema; o resto do painel para o tema | baixo | M | médio |
| UX-FIM-154 | `survey.page.scss`, `_controls.scss` | camadas por número solto (40 a 70) e medidas soltas (LV-007, LV-009) | tokens `--z-*`, `--panel-width`, `--header-h` | baixo | P | médio |
| UX-FIM-155 | `survey.page.ts`, `core/graph/export.ts` | MIME e cercado do Mermaid como texto solto (LV-016) | constantes em `export.ts` | baixo | P | baixo |

### Médio risco (interação e foco)

| ID | Onde | Problema | Sugestão | Risco | Esforço | Impacto |
|---|---|---|---|---|---|---|
| UX-FIM-201 | `board.page.ts` (`move`) | depois da seta, o foco cai no `body` (QD-001, visto na tela) | devolver o foco à seta da mesma faixa (ou à oposta, se desabilitada) e anunciar a posição | médio | M | alto |
| UX-FIM-202 | `board.page.ts` (renomear) | "Renomear" não foca o campo, Esc não cancela e o foco se perde ao fechar (QD-003, visto na tela) | foco e seleção no campo; Esc cancela; foco volta a "Renomear" | médio | P | alto |
| UX-FIM-203 | `board.page.ts` e `survey.page.ts` (menus) | o foco não volta ao Exportar do quadro; Tab sai do menu sem fechar (QD-009, LV-005, visto na tela) | navegação de menu compartilhada: Esc e Tab fecham, o foco volta ao botão | médio | M | médio |
| UX-FIM-204 | `board.page.ts` (`confirmRemove`) e `add-to-board-dialog` | foco perdido depois de tirar a faixa e depois de adicionar ao quadro (QD-005, LV-010) | foco na próxima faixa ou em "Adicionar levantamento"; foco em "Abrir o quadro" | médio | P | médio |
| UX-FIM-205 | `board.page.html` (arrastar) | sem destino visível durante o arraste (QD-016) | destaque da faixa alvo no `dragover` | médio | M | médio |
| UX-FIM-206 | `surveys.page.ts`, `boards.page.ts` (`onBoardCreated`) | a seleção e o diálogo somem antes de a navegação terminar; navegação cancelada deixa a pessoa sem aviso (QD-004; a falha da Fase 60 não se repetiu) | navegar primeiro e limpar só se der certo; senão, aviso com link "Abrir o quadro" | médio | P | alto |
| UX-FIM-207 | `board.page.ts` (`resurvey`) | "Levantar de novo" sem aviso de início, e o foco some com o botão (QD-012) | aviso de início e foco no estado da faixa | médio | P | médio |
| UX-FIM-208 | `survey.page.ts` e `.html` (menu Exportar) | no mapa por sistemas e no consolidado, PNG, SVG e Mermaid ficam ativos e falham com "O grafo ainda não está desenhado"; o aviso cobre o seletor (LV-001, visto na tela) | itens desabilitados nesses desenhos (`aria-disabled`) com a dica nova "Disponível no grafo e na visão de negócio"; aviso abaixo da barra | médio | M | alto |
| UX-FIM-209 | `business-view.component.html` (avisos) | `notice` e `feedback` são dois avisos no mesmo lugar e se sobrepõem (NB-005, NB-011) | um aviso só | médio | M | médio |
| UX-FIM-210 | `business-view.component.ts` (editar texto) | o texto grava no blur, sem desfazer (NB-007) | "Desfazer" no aviso de texto salvo | médio | M | médio |
| UX-FIM-211 | `business-view.component.html` (balão) | a origem do texto (agente, IA, editado) só aparece no painel (NB-003) | selo curto no balão quando a origem não é a regra | médio | P | médio |
| UX-FIM-212 | `business-view.component.scss` (losango) | losango escolhido e com foco perde o traço da escolha (NB-016) | anel da escolha atrás do traço do foco | médio | M | baixo |
| UX-FIM-213 | `business-layout.ts` (rótulo do ramo) | rótulos de caso do `switch` se sobrepõem (visto no quadro) | afastar os rótulos de ligações irmãs | médio | M | médio |
| UX-FIM-214 | `board-create-dialog` | a ordem escolhida vira a ordem das faixas, mas nenhuma tela a mostra (QD-008) | número da posição ao lado de cada caixa marcada | médio | P | médio |
| UX-FIM-215 | `survey.page.ts` (menu do nó) | "Mostrar na visão de negócio" aparece tarde (consulta a cada abertura) e o aviso não leva à visão (LV-006) | resultado guardado por levantamento; ação "Ver" no aviso | médio | M | médio |
| UX-FIM-216 | `survey.page.ts` (`showDrawing`) | trocar de desenho não é anunciado ao leitor de tela (LV-020) | região `aria-live` com o nome do desenho | médio | P | baixo |

Fica de fora, por decisão já tomada: UX-BIZ-003 pedia que as setas andassem pelas ligações; a Fase 58 decidiu que as setas movem o balão em 10 px, e o Tab anda na ordem do fluxo.

## Fases

### Fase A: risco baixo, visão de negócio e tema

UX-FIM-001 a 012, 153, 154 e 155. Arquivos: `business-view.component.*`, `_business.scss`, `_controls.scss`, `_tokens.scss`, `survey.page.scss`, `node-panel.component.scss`, `core/graph/export.ts`. Aceite: build e suítes verdes; candidatos de dobra invisíveis sem hover; aviso de erro vermelho; contraste da referência de 4,5:1 para cima nos sete tipos, nos dois temas; os `.component.scss` tocados abaixo de 6 kB.

### Fase B: risco baixo, quadros e levantamento

UX-FIM-101 a 114, 151 e 152. Arquivos: `board.page.*`, `boards.page.*`, `board-create-dialog`, `add-to-board-dialog`, `_boards.scss`, `survey.page.ts` e `.html` (só o aviso do draw.io e o `aria-haspopup`). Aceite: cada ação do quadro tem aviso; erro fica até fechar; faixas ocupam a largura em 1920.

### Fase C: risco médio

UX-FIM-201 a 216. Aceite: com clique de verdade, o foco fica num lugar útil depois de cada ação do quadro (`document.activeElement` diferente de `body`); no mapa, PNG, SVG e Mermaid desabilitados com a dica; o quadro novo abre depois de criar, e a navegação cancelada mostra o aviso com o link.

### Fase D: alto risco, requer aprovação

Nada daqui entra sem o dono aprovar o item.

- **D-01 (UX-BIZ-008):** "Ver no grafo" vira o botão primário do painel do passo e "Ver só este trecho" ganha um `title` dizendo que sai da visão de negócio?
- **D-02 (UX-BIZ-009):** `catch` e `finally` passam a aparecer como "Se der erro (X)" e "Sempre ao final" em todo o Rastro (grafo, mapa e visão de negócio)?
- **D-03 (UX-BIZ-015):** o balão cresce com o texto, em vez de cortar em duas ou três linhas?
- **D-04 (UX-BIZ-018):** o selo "proposto" leva a dica "confirme no painel do passo" (a confirmação já existe desde a Fase 59), ou o texto passa a dizer "proposto pela regra"?
- **D-05 (NB-002):** o texto por regra do início e do passo comum deixa de ser o nome do método (`SettingsController.SetLanguage`) e passa a ser o nome em palavras ("Set language")? Muda o texto de todos os fluxos, das exportações e do MCP.
- **D-06 (QD-007, QD-008):** a lista de quadros vazia ganha um botão "Escolher levantamentos" e o texto passa de "dois ou mais" para "um ou mais" (o diálogo aceita um)? A lista de levantamentos ganha uma dica fixa "Marque dois ou mais levantamentos prontos para juntar num quadro"?
- **D-07 (QD-017):** na tela do quadro, "Adicionar levantamento" vira o botão primário e "Excluir quadro" vai para o fim da barra, separado?
- **D-08 (QD-022):** "Adicionar a um quadro" deixa de escolher o primeiro quadro por padrão (a opção "Criar um quadro novo" vem primeiro, ou o botão diz o nome do quadro escolhido)?
- **D-09 (LV-002, LV-014, LV-017):** trocar textos: "O grafo ainda não está desenhado" vira uma mensagem por desenho; "Abrir no VS Code" do menu do nó vira "Abrir no editor" (o link segue o editor configurado); o sufixo "-recorte" do nome do arquivo passa a ser traduzido?
- **D-10:** o rótulo do caso do `switch` (`Caso RepositoryConfigOutcome.FolderNotFound`) passa a mostrar só o último nome em palavras ("Caso folder not found")?

## Fora do escopo e riscos

- A visão de negócio de `SurveysController.Create` sai vazia, embora o método recuse com `Error(ErrorCodes..., "...")`: o atalho `Error(código, mensagem)` do controller não leva a constante 4xx que a regra de recusa procura. É da derivação (núcleo), não da tela, e foi para as pendências do `plano-mvp.md`.
- O 404 de `draft` e `diagram` no console de todo levantamento sem rascunho é anterior a estas fases.
- Riscos: a navegação de menu compartilhada mexe no menu Exportar e no Comparar do levantamento; o foco depois de mover a faixa depende do `track` do `@for`.
