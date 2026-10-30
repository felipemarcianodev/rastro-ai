// Declaração mínima do Dialog do CDK só para a fixture compilar sem o Angular CDK instalado.
// O extrator reconhece o Dialog pelo nome e pelo módulo '@angular/cdk/dialog'.

declare module '@angular/cdk/dialog' {
  export class DialogRef<R = unknown> {
    close(resultado?: R): void;
    closed: { subscribe(observador?: (valor: R | undefined) => void): unknown };
  }

  export class Dialog {
    open<R = unknown, D = unknown, C = unknown>(componente: new (...args: any[]) => C, configuracao?: D): DialogRef<R>;
  }
}
