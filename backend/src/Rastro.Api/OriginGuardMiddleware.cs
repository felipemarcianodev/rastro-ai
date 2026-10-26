using Microsoft.AspNetCore.Mvc;
using Rastro.Api.Controllers;

namespace Rastro.Api;

// ADR-003: pedido que muda dado e vem de uma página de outro site (cabeçalho Origin de fora da máquina)
// é recusado. Sem Origin (curl, testes, MCP) passa, porque não vem de navegador.
public class OriginGuardMiddleware(RequestDelegate next, ILogger<OriginGuardMiddleware> logger)
{
    private static readonly string[] LocalHosts = ["localhost", "127.0.0.1", "[::1]", "::1"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (ChangesData(context.Request.Method)
            && context.Request.Headers.TryGetValue("Origin", out var origin)
            && !IsLocal(origin.ToString()))
        {
            // O corpo do pedido nunca é lido nem logado.
            logger.LogWarning("Pedido {Method} {Path} recusado: Origin {Origin} não é desta máquina.",
                context.Request.Method, context.Request.Path.Value, origin.ToString());

            var problem = new ProblemDetails { Status = StatusCodes.Status403Forbidden, Title = "Origem não permitida." };
            problem.Extensions["code"] = ErrorCodes.OriginNotAllowed;
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
            return;
        }

        await next(context);
    }

    private static bool ChangesData(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    // "null" (página sandbox, arquivo local) e qualquer valor que não seja uma URL são recusados.
    private static bool IsLocal(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && LocalHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
}
