# Segurança

## Como relatar uma falha

Não abra issue pública para falha de segurança. Use o relato privado do GitHub: aba **Security**, botão **Report a vulnerability** neste repositório.

Inclua a versão (`rastro --version`), o passo a passo para reproduzir e o impacto que você enxerga. A resposta inicial vem em até 7 dias.

## O que o Rastro promete

- **Só na sua máquina:** a API escuta em `127.0.0.1` e recusa `Host` que não seja a própria máquina.
- **Pedido de outro site:** POST, PUT, PATCH e DELETE com `Origin` de fora são recusados.
- **Disco:** só lê as pastas cadastradas; caminho absoluto e `..` vindos da requisição são recusados.
- **Processos:** o build e os testes da validação rodam sem shell, com argumentos separados, tempo limite e o comando que está na configuração (nunca o que vem da requisição).
- **Chave de API dos provedores de IA:** fica cifrada no banco local e nunca volta na resposta nem no log.
- **IA:** só envia ao provedor o trecho de código necessário, e só quando você liga o modo provedor.

Uma falha em qualquer um desses pontos é um relato bem-vindo.
