using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

internal static class ShapeMatcher
{
    public static string? FindMismatch(INamedTypeSymbol shape, INamedTypeSymbol concreteType)
    {
        foreach (var member in GetShapeMembers(shape).Where(IsRequired))
        {
            var mismatch = FindMemberMismatch(concreteType, member);
            if (mismatch is not null) return mismatch;
        }
        return null;
    }

    public static ISymbol? FindUnsupportedMember(INamedTypeSymbol shape) =>
        shape.GetMembers()
            .Concat(shape.AllInterfaces.SelectMany(i => i.GetMembers()))
            .FirstOrDefault(IsUnsupported);

    public static IEnumerable<ISymbol> GetShapeMembers(INamedTypeSymbol shape) =>
        shape.GetMembers()
            .Concat(shape.AllInterfaces.SelectMany(i => i.GetMembers()))
            .Where(IsRelevant);

    // Members with a default implementation are optional: the adapter forwards them only if the concrete type provides a match.
    public static bool IsRequired(ISymbol member) => member.IsAbstract;

    public static bool IsProvidedBy(ISymbol member, INamedTypeSymbol concreteType) =>
        FindMemberMismatch(concreteType, member) is null;

    private static bool IsRelevant(ISymbol member)
    {
        if (member.IsStatic || member.DeclaredAccessibility != Accessibility.Public) return false;
        if (!member.IsAbstract && !member.IsVirtual) return false;
        return member switch
        {
            IMethodSymbol { MethodKind: MethodKind.Ordinary } => true,
            IPropertySymbol => true,
            IEventSymbol => true,
            _ => false,
        };
    }

    private static bool IsUnsupported(ISymbol member) => member switch
    {
        { IsStatic: true, IsAbstract: true } => true,
        IMethodSymbol { MethodKind: MethodKind.Ordinary, IsStatic: false } method => method.IsGenericMethod || method.RefKind != RefKind.None,
        IPropertySymbol { IsStatic: false } property => property.RefKind != RefKind.None,
        _ => false,
    };

    private static string? FindMemberMismatch(INamedTypeSymbol concreteType, ISymbol member) => member switch
    {
        IMethodSymbol method => HasMatchingMethod(concreteType, method)
            ? null
            : $"missing method '{method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'",
        IPropertySymbol property => FindPropertyMismatch(concreteType, property),
        IEventSymbol @event => HasMatchingEvent(concreteType, @event)
            ? null
            : $"missing event '{@event.Name}' of type '{@event.Type.ToDisplayString()}'",
        _ => null,
    };

    private static bool HasMatchingMethod(INamedTypeSymbol concreteType, IMethodSymbol shapeMethod)
    {
        foreach (var candidate in GetAllMembers(concreteType).OfType<IMethodSymbol>())
        {
            if (candidate.MethodKind != MethodKind.Ordinary) continue;
            if (candidate.Name != shapeMethod.Name) continue;
            if (candidate.DeclaredAccessibility != Accessibility.Public) continue;
            if (!SymbolEqualityComparer.Default.Equals(candidate.ReturnType, shapeMethod.ReturnType)) continue;
            if (ParametersMatch(candidate.Parameters, shapeMethod.Parameters)) return true;
        }
        return false;
    }

    private static string? FindPropertyMismatch(INamedTypeSymbol concreteType, IPropertySymbol shapeProperty)
    {
        var candidate = GetAllMembers(concreteType).OfType<IPropertySymbol>()
            .FirstOrDefault(p => p.Name == shapeProperty.Name &&
                                  p.DeclaredAccessibility == Accessibility.Public &&
                                  SymbolEqualityComparer.Default.Equals(p.Type, shapeProperty.Type) &&
                                  ParametersMatch(p.Parameters, shapeProperty.Parameters));

        var displayName = shapeProperty.IsIndexer
            ? $"indexer '{shapeProperty.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'"
            : $"property '{shapeProperty.Name}'";

        if (candidate is null)
            return $"missing {displayName} of type '{shapeProperty.Type.ToDisplayString()}'";

        if (shapeProperty.GetMethod is not null && candidate.GetMethod is not { DeclaredAccessibility: Accessibility.Public })
            return $"{displayName} has no public getter";

        if (shapeProperty.SetMethod is not null && candidate.SetMethod is not { DeclaredAccessibility: Accessibility.Public })
            return $"{displayName} has no public setter";

        return null;
    }

    private static bool HasMatchingEvent(INamedTypeSymbol concreteType, IEventSymbol shapeEvent) =>
        GetAllMembers(concreteType).OfType<IEventSymbol>()
            .Any(e => e.Name == shapeEvent.Name &&
                      e.DeclaredAccessibility == Accessibility.Public &&
                      SymbolEqualityComparer.Default.Equals(e.Type, shapeEvent.Type));

    private static bool ParametersMatch(IReadOnlyList<IParameterSymbol> candidate, IReadOnlyList<IParameterSymbol> shape)
    {
        if (candidate.Count != shape.Count) return false;
        for (var i = 0; i < candidate.Count; i++)
        {
            if (!SymbolEqualityComparer.Default.Equals(candidate[i].Type, shape[i].Type) ||
                candidate[i].RefKind != shape[i].RefKind)
                return false;
        }
        return true;
    }

    public static IEnumerable<ISymbol> GetAllMembers(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            foreach (var m in current.GetMembers())
                yield return m;
    }
}
