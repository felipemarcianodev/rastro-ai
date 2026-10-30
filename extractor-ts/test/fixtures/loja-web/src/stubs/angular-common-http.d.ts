// Declaração mínima do Angular só para a fixture compilar sem o Angular instalado.
// O extrator reconhece o HttpClient pelo nome e pelo módulo '@angular/common/http'.

declare module '@angular/common/http' {
  export interface Observador<T> {
    next?: (valor: T) => void;
    error?: (erro: unknown) => void;
    complete?: () => void;
  }

  export class Observavel<T> {
    subscribe(observador?: Observador<T> | ((valor: T) => void)): unknown;
    pipe(...operadores: unknown[]): Observavel<T>;
  }

  export class HttpClient {
    get<T>(url: string): Observavel<T>;
    post<T>(url: string, corpo: unknown): Observavel<T>;
    put<T>(url: string, corpo: unknown): Observavel<T>;
    patch<T>(url: string, corpo: unknown): Observavel<T>;
    delete<T>(url: string): Observavel<T>;
    request<T>(verbo: string, url: string): Observavel<T>;
  }
}

declare module '@angular/core' {
  export function Component(config: { selector?: string; template?: string; templateUrl?: string }): ClassDecorator;
  export function Injectable(config?: { providedIn?: string }): ClassDecorator;
  export function inject<T>(token: abstract new (...args: never[]) => T): T;
}
