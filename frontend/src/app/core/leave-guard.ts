import { CanDeactivateFn } from '@angular/router';

// Tela com edição não salva: pergunta antes de sair (UX-G-F10). Fica fora da página para a rota
// carregar a tela sob demanda sem puxá-la para o pacote principal.
export interface LeaveGuarded {
  canLeave(): boolean | Promise<boolean>;
}

export const leaveGuard: CanDeactivateFn<LeaveGuarded> = (component) => component.canLeave();
