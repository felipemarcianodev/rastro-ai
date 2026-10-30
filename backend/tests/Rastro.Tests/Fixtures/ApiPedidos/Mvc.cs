// O workspace em memória não referencia o ASP.NET: estes são os atributos mínimos que o código de
// exemplo usa. O índice de rotas lê só a sintaxe, então o nome do atributo é o que importa.
namespace Microsoft.AspNetCore.Mvc;

[AttributeUsage(AttributeTargets.Class)]
public class ApiControllerAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RouteAttribute(string template) : Attribute
{
    public string Template { get; } = template;
}

public abstract class HttpMethodAttribute(string? template = null) : Attribute
{
    public string? Template { get; } = template;
    public string? Name { get; set; }
}

[AttributeUsage(AttributeTargets.Method)]
public class HttpGetAttribute(string? template = null) : HttpMethodAttribute(template);

[AttributeUsage(AttributeTargets.Method)]
public class HttpPostAttribute(string? template = null) : HttpMethodAttribute(template);

public abstract class ControllerBase;
