using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rastro.Extractor;

// Registros de DI encontrados no código: AddScoped/AddTransient/AddSingleton (e TryAdd*)
// nas formas <TService, TImpl> e (typeof(TService), typeof(TImpl)).
// Serve para escolher a implementação quando uma interface tem mais de uma.
internal sealed class DiRegistrationIndex
{
    private static readonly HashSet<string> RegistrationMethods =
    [
        "AddScoped", "AddTransient", "AddSingleton",
        "TryAddScoped", "TryAddTransient", "TryAddSingleton",
        "AddKeyedScoped", "AddKeyedTransient", "AddKeyedSingleton"
    ];

    private readonly Dictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>> _map = new(SymbolEqualityComparer.Default);

    public IReadOnlySet<INamedTypeSymbol> ImplementationsOf(INamedTypeSymbol service) =>
        _map.TryGetValue(service.OriginalDefinition, out var impls)
            ? impls
            : new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

    public static async Task<DiRegistrationIndex> BuildAsync(Solution solution, CancellationToken ct)
    {
        var index = new DiRegistrationIndex();

        foreach (var project in solution.Projects)
        foreach (var document in project.Documents)
        {
            var root = await document.GetSyntaxRootAsync(ct);
            if (root is null) continue;

            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Where(i => RegistrationMethods.Contains(MethodName(i) ?? string.Empty))
                .ToList();
            if (invocations.Count == 0) continue;

            var model = await document.GetSemanticModelAsync(ct);
            if (model is null) continue;

            foreach (var invocation in invocations)
            {
                var (service, impl) = ReadPair(invocation, model);
                if (service is null || impl is null) continue;

                var key = service.OriginalDefinition;
                if (!index._map.TryGetValue(key, out var set))
                    index._map[key] = set = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                set.Add(impl.OriginalDefinition);
            }
        }

        return index;
    }

    private static string? MethodName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        SimpleNameSyntax s => s.Identifier.ValueText,
        _ => null
    };

    private static (INamedTypeSymbol? Service, INamedTypeSymbol? Impl) ReadPair(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax m => m.Name,
            SimpleNameSyntax s => s,
            _ => null
        };

        if (name is GenericNameSyntax { TypeArgumentList.Arguments.Count: 2 } generic)
        {
            return (model.GetTypeInfo(generic.TypeArgumentList.Arguments[0]).Type as INamedTypeSymbol,
                    model.GetTypeInfo(generic.TypeArgumentList.Arguments[1]).Type as INamedTypeSymbol);
        }

        var typeofs = invocation.ArgumentList.Arguments
            .Select(a => a.Expression)
            .OfType<TypeOfExpressionSyntax>()
            .ToList();
        if (typeofs.Count >= 2)
        {
            return (model.GetTypeInfo(typeofs[0].Type).Type as INamedTypeSymbol,
                    model.GetTypeInfo(typeofs[1].Type).Type as INamedTypeSymbol);
        }

        return (null, null);
    }
}
