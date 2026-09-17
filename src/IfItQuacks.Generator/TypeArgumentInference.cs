using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

internal static class TypeArgumentInference
{
    public static IMethodSymbol? TryConstruct(IMethodSymbol method, INamedTypeSymbol concreteType)
    {
        var shape = (INamedTypeSymbol)method.Parameters[0].Type;
        var bindings = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var member in ShapeMatcher.GetShapeMembers(shape))
        {
            var matched = member switch
            {
                IMethodSymbol shapeMethod => TryBindMethod(method, concreteType, shapeMethod, bindings),
                IPropertySymbol shapeProperty => TryBindProperty(method, concreteType, shapeProperty, bindings),
                _ => true,
            };
            if (!matched) return null;
        }

        if (method.TypeParameters.Any(tp => !bindings.ContainsKey(tp)))
            return null;

        return method.Construct(method.TypeParameters.Select(tp => bindings[tp]).ToArray());
    }

    private static bool TryBindMethod(IMethodSymbol method, INamedTypeSymbol concreteType, IMethodSymbol shapeMethod,
        Dictionary<ITypeParameterSymbol, ITypeSymbol> bindings)
    {
        foreach (var candidate in ShapeMatcher.GetAllMembers(concreteType).OfType<IMethodSymbol>())
        {
            if (candidate.MethodKind != MethodKind.Ordinary) continue;
            if (candidate.Name != shapeMethod.Name) continue;
            if (candidate.DeclaredAccessibility != Accessibility.Public) continue;
            if (candidate.Parameters.Length != shapeMethod.Parameters.Length) continue;

            var attempt = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(bindings, SymbolEqualityComparer.Default);
            if (!Unify(method, shapeMethod.ReturnType, candidate.ReturnType, attempt)) continue;

            var paramsMatch = true;
            for (var i = 0; i < candidate.Parameters.Length && paramsMatch; i++)
            {
                paramsMatch = candidate.Parameters[i].RefKind == shapeMethod.Parameters[i].RefKind &&
                              Unify(method, shapeMethod.Parameters[i].Type, candidate.Parameters[i].Type, attempt);
            }
            if (!paramsMatch) continue;

            Commit(attempt, bindings);
            return true;
        }
        return false;
    }

    private static bool TryBindProperty(IMethodSymbol method, INamedTypeSymbol concreteType, IPropertySymbol shapeProperty,
        Dictionary<ITypeParameterSymbol, ITypeSymbol> bindings)
    {
        var candidate = ShapeMatcher.GetAllMembers(concreteType).OfType<IPropertySymbol>()
            .FirstOrDefault(p => p.Name == shapeProperty.Name && p.DeclaredAccessibility == Accessibility.Public);
        if (candidate is null) return false;

        var attempt = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(bindings, SymbolEqualityComparer.Default);
        if (!Unify(method, shapeProperty.Type, candidate.Type, attempt)) return false;

        Commit(attempt, bindings);
        return true;
    }

    private static bool Unify(IMethodSymbol method, ITypeSymbol pattern, ITypeSymbol actual,
        Dictionary<ITypeParameterSymbol, ITypeSymbol> bindings)
    {
        switch (pattern)
        {
            case ITypeParameterSymbol tp when method.TypeParameters.Contains(tp, SymbolEqualityComparer.Default):
                if (bindings.TryGetValue(tp, out var bound))
                    return SymbolEqualityComparer.Default.Equals(bound, actual);
                bindings[tp] = actual;
                return true;

            case INamedTypeSymbol { IsGenericType: true } p when actual is INamedTypeSymbol a &&
                                                              SymbolEqualityComparer.Default.Equals(p.OriginalDefinition, a.OriginalDefinition):
                for (var i = 0; i < p.TypeArguments.Length; i++)
                    if (!Unify(method, p.TypeArguments[i], a.TypeArguments[i], bindings))
                        return false;
                return true;

            case IArrayTypeSymbol p when actual is IArrayTypeSymbol a && p.Rank == a.Rank:
                return Unify(method, p.ElementType, a.ElementType, bindings);

            default:
                return SymbolEqualityComparer.Default.Equals(pattern, actual);
        }
    }

    private static void Commit(Dictionary<ITypeParameterSymbol, ITypeSymbol> attempt,
        Dictionary<ITypeParameterSymbol, ITypeSymbol> bindings)
    {
        foreach (var pair in attempt)
            bindings[pair.Key] = pair.Value;
    }
}
