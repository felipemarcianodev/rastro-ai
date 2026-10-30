namespace Rastro.Core.Http;

// Uma rota HTTP que um repositório atende. Target é o método que a atende (Namespace.Tipo.Metodo);
// File é relativo à raiz do repositório, com "/".
public record RouteEntry(string Method, string Template, string Target, string File, int Line);
