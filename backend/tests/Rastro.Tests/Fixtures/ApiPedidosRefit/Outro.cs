// Atributo com o mesmo nome do Refit, mas de outro namespace.
namespace Outro;

[AttributeUsage(AttributeTargets.Method)]
public class GetAttribute(string path) : Attribute;
