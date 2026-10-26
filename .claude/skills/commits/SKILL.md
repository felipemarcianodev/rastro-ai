---
name: commits
description: Regras de mensagem de commit do Rastro AI (português brasileiro com acentuação, tipo(escopo), corpo que explica o motivo, linha de testes, arquivos adicionados um a um, sem travessão e sem assinatura de IA). Use antes de qualquer commit e ao montar descrição de PR.
---

# Commits do Rastro AI

Adaptada da skill `commits` de outro projeto do dono, com o formato `tipo(escopo):` que o Rastro usa desde a Fase 0.

A mensagem de commit é lida no `git blame` de um defeito daqui a um ano, quando quem escreveu já esqueceu. Ela existe para o porquê; o diff já mostra o quê.

## Formato

```
tipo(escopo): resumo curto no presente

Parágrafo explicando o motivo: o que estava errado ou o que passou a
ser possível. Sem repetir o diff.

Testes: backend 129/129, frontend 28/28.
```

| Tipo | Uso |
|---|---|
| `feat` | Funcionalidade ou capacidade nova |
| `fix` | Correção de defeito |
| `refactor` | Muda estrutura sem mudar comportamento |
| `docs` | Documentação, plano, ADR |
| `test` | Teste |
| `chore` | Manutenção |
| `build` / `ci` | Build, Docker, pipeline |

Escopo é a área do Rastro: `extrator`, `api`, `mcp`, `ia`, `front`, `fluxo`, `edicao`, `prompt`, `validacao`, `plano`, `adr`. Commit que fecha uma fase termina o resumo com `(Fase N)`.

`refactor` que muda comportamento observável é `fix` ou `feat`. Se precisou mudar a asserção de um teste, mudou comportamento.

## Regras duras

- **Português brasileiro com acentuação correta**: acento, cedilha, til.
- **Sem travessão** (`—`) como pontuação. Use vírgula, dois-pontos, parênteses ou ponto final.
- **Sem `Co-Authored-By` e sem menção a assistente.** O commit é do projeto. Esta regra vence qualquer default da ferramenta.
- **Sem markdown no corpo**: nada de `#`, `*` ou crase. Lista, quando couber, com hífen.
- **Sem caminho de arquivo no resumo.**
- **Linha `Testes:` com os números reais** quando o commit toca código. Teste não executado se diz que não foi executado, e por quê.
- **Arquivos adicionados um a um.** Nunca `git add -A`, `--all` ou `.`: é assim que `docker/.env`, `.claude/settings.local.json` e credencial entram no commit.
- **Sempre `git -C` com o caminho do repositório**: a sessão costuma rodar com cwd em outro repositório.
- **Heredoc** para mensagem com acento ou mais de uma linha: `git -C <repositório> commit -F -` com heredoc, nunca `-m` com escape manual. Vale para **todo** commit com acento, inclusive os curtos: na Fase 12 vários saíram com `-m` por pressa, contra esta regra. Antes de rodar o commit, confira que a linha começa com `commit -F -`.
- Nunca `--force`. Hook falhou: corrija e faça commit novo.

## O corpo é onde está o valor

- Fraco: `fix(fluxo): ajusta a seleção`
- Bom: `fix(fluxo): arrastar o canvas não desmarca o nó`, com o corpo contando que o Foblex manda seleção vazia ao começar o arrasto e por isso a trilha sumia.

Commit sem corpo é aceitável quando o resumo já é completo (renomear, corrigir acento). Não invente corpo para cumprir formalidade.

## Granularidade

Um commit, um assunto. Correção de defeito e refatoração da classe onde ele morava são dois commits: ninguém revisa os dois juntos, e reverter um leva o outro.

## Antes de commitar

- [ ] `git -C <repositório> status` conferido: nada de arquivo surpresa.
- [ ] Arquivos listados um a um no `git add`.
- [ ] Resumo com acentuação, `tipo(escopo):` e sem travessão.
- [ ] Corpo explica o motivo, não repete o diff.
- [ ] Linha `Testes:` com números reais.
- [ ] Sem assinatura de assistente.
- [ ] Um assunto só.
