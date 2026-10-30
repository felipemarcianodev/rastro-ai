import { Component, inject } from '@angular/core';
import { confirmar } from './confirmacao';
import { PedidosService } from './pedidos.service';

@Component({
  selector: 'app-detalhe-pedido',
  templateUrl: './detalhe-pedido.component.html',
})
export class DetalhePedidoComponent {
  private readonly pedidosService = inject(PedidosService);

  pedido = { id: 7, enviado: false };
  erro = '';

  cancelar(): void {
    if (!confirmar()) return;

    this.pedidosService.cancelar(this.pedido.id).subscribe({
      next: () => this.recarregar(),
      error: (e) => this.mostrarErro(e),
    });
  }

  recarregar(): void {
    this.pedido = { id: this.pedido.id, enviado: false };
  }

  mostrarErro(e: unknown): void {
    this.erro = String(e);
  }
}
