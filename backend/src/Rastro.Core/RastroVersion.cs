using System.Reflection;

namespace Rastro.Core;

// Versão do produto (arquivo VERSION na raiz, lido pelo Directory.Build.props), sem o sufixo +commit.
public static class RastroVersion
{
    public static string Current { get; } =
        typeof(RastroVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "0.0.0";
}
