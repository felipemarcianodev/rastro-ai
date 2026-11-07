# ADR-008: comando de build e de testes editado pela tela

**Data:** 2026-11-07
**Status:** Accepted
**Contexto:** Substitui o item 2 ("Comando só pelo terminal") da [ADR-003](adr-003-repositorios-pela-tela.md). O resto da ADR-003 continua valendo.

---

## Contexto

A validação de uma mudança roda o build e os testes do repositório. Até aqui, os dois comandos só se informavam pelo terminal (`rastro repo add ... --build "..." --testes "..."`), e quem já tinha cadastrado o repositório pela tela precisava removê-lo e cadastrá-lo de novo. O levantamento de UX do fim do plano do banco apontou isso como o maior atrito das Configurações (UX-FIM-C01), e o dono aprovou a mudança em 2026-11-07.

A ADR-003 deixou o comando fora da tela para que nenhuma requisição levasse um comando ao Rastro. Desde então, a API ganhou duas proteções em todas as rotas: `AllowedHosts` só com a própria máquina (fecha o DNS rebinding) e a recusa de POST, PUT, PATCH e DELETE com `Origin` de fora (fecha o pedido forjado por outro site). Quem consegue mandar uma requisição sem `Origin` para `127.0.0.1` já é um programa rodando na máquina, que pode gravar o `config.json` direto.

## Decisão

1. **Rota de edição.** `PUT api/repositories/{nome}/commands`, com o comando de build e o de testes. Vazio tira o comando. A rota tem a mesma proteção das outras que mudam dado (`AllowedHosts` e `Origin`).
2. **O texto é conferido antes de gravar:** uma linha só, sem caractere de controle e com até 500 caracteres. Fora disso, `REPOSITORY_COMMAND_INVALID`.
3. **O jeito de rodar não muda.** O comando continua rodando pelo `ProcessRunner`: o primeiro termo é o executável e o resto são argumentos separados, sem shell e com tempo limite. `&&`, `|` e redirecionamento não funcionam, e a tela diz isso.
4. **A lista de repositórios devolve os dois comandos em texto,** para a tela mostrar e editar. Comando não é segredo; mesmo assim, a rota não grava o comando no log.
5. **O terminal continua valendo:** `rastro repo add ... --build --testes` grava o mesmo `config.json`, pelo mesmo serviço.

## Consequências

- Quem usa só a tela configura a validação sem abrir o terminal e sem recadastrar o repositório.
- Um site aberto no navegador continua sem conseguir trocar o comando: o pedido dele leva `Origin` de fora e é recusado. Um programa local já tinha como fazer isso pelo `config.json`; a rota não abre uma porta que não existia.
- No Docker, quem publica a API em outro nome amplia o `AllowedHosts` e passa a confiar nesse nome também para editar o comando.
