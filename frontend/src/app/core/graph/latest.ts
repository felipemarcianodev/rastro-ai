// Só a resposta do pedido mais recente vale: um layout antigo que chega depois do novo
// é descartado (senão o grafo volta a um estado que o usuário já mudou).
export class Latest {
  private version = 0;

  run<T>(work: Promise<T>, apply: (value: T) => void): Promise<void> {
    const mine = ++this.version;
    return work.then((value) => {
      if (mine === this.version) apply(value);
    });
  }
}
