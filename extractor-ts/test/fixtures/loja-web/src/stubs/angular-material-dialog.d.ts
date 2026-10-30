// Declaração mínima do MatDialog só para a fixture compilar sem o Angular Material instalado.
// O extrator reconhece o MatDialog pelo nome e pelo módulo '@angular/material/dialog'.

declare module '@angular/material/dialog' {
  export class MatDialogRef<R = unknown> {
    close(resultado?: R): void;
    afterClosed(): { subscribe(observador?: (valor: R | undefined) => void): unknown };
  }

  export class MatDialog {
    open<T, R = unknown>(componente: new (...args: any[]) => T, configuracao?: unknown): MatDialogRef<R>;
  }
}
