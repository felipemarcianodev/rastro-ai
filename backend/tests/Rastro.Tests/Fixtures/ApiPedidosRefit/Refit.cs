// O workspace em memória não referencia o Refit: este é o mínimo do namespace Refit que o código de
// exemplo usa. O extrator reconhece o atributo pelo símbolo (nome e namespace), não pelo texto.
namespace Refit;

public abstract class HttpMethodAttribute(string path) : Attribute
{
    public string Path { get; } = path;
}

[AttributeUsage(AttributeTargets.Method)]
public class GetAttribute(string path) : HttpMethodAttribute(path);

[AttributeUsage(AttributeTargets.Method)]
public class PostAttribute(string path) : HttpMethodAttribute(path);

[AttributeUsage(AttributeTargets.Method)]
public class PutAttribute(string path) : HttpMethodAttribute(path);

[AttributeUsage(AttributeTargets.Method)]
public class PatchAttribute(string path) : HttpMethodAttribute(path);

[AttributeUsage(AttributeTargets.Method)]
public class DeleteAttribute(string path) : HttpMethodAttribute(path);

[AttributeUsage(AttributeTargets.Parameter)]
public class BodyAttribute : Attribute;
