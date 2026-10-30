# Levantamento de UX: visão consolidada (Fase 27)

**Status:** EXECUTADO na própria Fase 27 (riscos baixo e médio); o item de risco alto espera a aprovação do dono.
**Escopo:** a tela nova `pages/diagram/` (rota `surveys/:id/visao`) e o link para ela na barra do fluxo.
**Data:** 2026-10-30. Análise estática com um agente de leitura, mais a verificação no navegador com o levantamento do Mirante (clique real num passo da prévia e troca de cor).

## Diagnóstico

- Botões primários visíveis: 1 com o painel de cor fechado, mas 2 com um passo escolhido ("Salvar" e "Só este passo"), contra a regra de um por tela.
- Elementos interativos visíveis: cerca de 22 sem passo escolhido e cerca de 64 com o painel de cor aberto (36 amostras).
- Os principais problemas: o passo da prévia só se escolhia com o mouse; o painel de cor empurrava a prévia para baixo; a prévia grande não tinha zoom; o erro de salvar sumia sozinho em 4 segundos.

## Achados

| ID | Onde | Problema | Sugestão | Risco | Situação |
|---|---|---|---|---|---|
| D1 | `diagram.page.html`, `_diagram.scss` | Passo da prévia só se escolhe com o mouse; leitor de tela não ouve a instrução | `tabindex`, `role="button"` e `aria-label` em cada passo; Enter e Espaço escolhem; foco visível | médio | feito |
| D2 | `diagram.page.html` | Erro de render mostra a mensagem crua do Mermaid, sem dizer o que fazer | Linha "Corrija o texto à esquerda ou use Desfazer." e `pre` com altura limitada | baixo | feito |
| D3 | `diagram.page.scss` | O painel de cor entra no fluxo e empurra a prévia para baixo | Painel flutuante sobre a prévia | médio | feito |
| D4 | `diagram.page.html` | Amostras de 20px, sem nome acessível útil e com doze paradas de Tab | Amostras de 24px em `radiogroup` | baixo | feito |
| D5 | `confirm-dialog.component.ts` | "Gerar de novo" usa o botão vermelho, de ação destrutiva | Tom do botão de confirmar (perigo ou primário) | baixo | feito |
| D6 | `diagram.page.html` | Estado de "desenhando" duplicado e sem estado vazio | Um estado só, dentro da prévia, e o texto do vazio | baixo | feito |
| D7 | `_diagram.scss` | Prévia grande sem zoom nem ajuste à largura | "Ajustar", "100%", "−", "+" e Ctrl+roda | médio | feito |
| D8 | `diagram.page.html` | Dois botões primários com um passo escolhido | "Só este passo" vira secundário | baixo | feito |
| D9 | `diagram.page.html` | A barra não mostra o repositório, como a do fluxo | Repositório na linha de dados da barra | médio | feito (só essa parte) |
| D10 | `diagram.page.html` | SVG e PNG desabilitados sem explicação; PNG sem estado de "gerando" | `title` "Aguarde a prévia" e "Gerando PNG…" | baixo | feito |
| D11 | `diagram.page.ts` | Mensagem de erro some sozinha e empurra o layout | Erro fica até ser fechado (`role="alert"`) e a mensagem é sobreposta; "Tentar de novo" na tela de erro | baixo | feito |
| D12 | `diagram.page.html`, `_diagram.scss` | O seletor de cor não mostra o hexadecimal; o realce do passo escolhido é só sombra | Hexadecimal ao lado do seletor e contorno no passo escolhido | baixo | feito |
| D13 | `diagram.page.scss` | Divisão fixa entre o texto e a prévia | Divisória ajustável | baixo | fora (o zoom resolve a leitura) |
| D14 | `diagram.page.html` | "Salvar" habilitado sem nada para salvar | Desabilitar quando o texto é igual ao gerado e não há nada salvo | baixo | feito |
| D15 | `survey.page.html` | Texto de ajuda do link com jargão ("Mermaid") | Trocar o texto | alto | espera aprovação (item `[ ]` na Fase 27 do plano) |

## Fora do escopo

- Agrupar "Desfazer", "Refazer" e "Descartar edição" num menu da barra (reorganiza a tela: risco alto, sem pedido).
- Números de linha no editor do texto.
