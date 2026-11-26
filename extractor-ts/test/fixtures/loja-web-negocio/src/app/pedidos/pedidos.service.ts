import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class PedidosService {
  private readonly http = inject(HttpClient);

  cancelar(id: number) {
    return this.http.post(`${environment.apiUrl}/api/pedidos/${id}/cancelamento`, {});
  }
}
