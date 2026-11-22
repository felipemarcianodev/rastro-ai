# Contexto do Rastro AI

Estado e contexto que antes ficavam na memória da IA (proibida desde 21/11/2026, ver `CLAUDE.md`). Atualizar a seção no commit da tarefa que mudar o estado; data em cada item. Este repositório é público: nada de nome de cliente, de sistema de terceiro ou de outro repositório do dono sem autorização.

## Código aberto (desde 26/10/2026)

- Público em https://github.com/felipemarcianodev/rastro-ai, licença MIT (Fase 24 do `plano-mvp.md`, ADR-004).
- O `origin` local é o GitHub; o Azure DevOps virou o remoto `azure`, e o histórico antigo ficou na branch local `main-azure`.
- Commits saem como `Felipe Santos Marciano <67729798+felipemarcianodev@users.noreply.github.com>` (configuração local do repositório).
- A `main` é protegida também para administrador: só PR, checks `backend` (Windows) e `frontend` (Linux) obrigatórios, histórico linear e merge squash.
- Pendências da Fase 24: publicar no NuGet (conta do dono), o card no site do portfólio e o dono arquivar o repositório do Azure DevOps.

## Histórico reescrito (04/11/2026)

- A pedido do dono, o histórico da `main` foi reescrito para tirar 16 linhas `Co-authored-by`. Os commits antigos ainda abrem por hash até o suporte do GitHub limpar, e outra máquina precisa clonar de novo.
