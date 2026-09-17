using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

internal static class ShapeMatcher
{
    public static string? FindMismatch(INamedTypeSymbol shape, INamedTypeSymbol concreteType, Compilation compilation)
    {
        foreach (var member in GetShapeMembers(shape).Where(IsRequired))
        {
            var mismatch = FindMemberMismatch(concreteType, member, compilation);
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

    public static ISymbol? FindCounterpart(ISymbol member, INamedTypeSymbol concreteType, Compilation compilation) => member switch
    {
        IMethodSymbol method => FindMethod(concreteType, method, compilation),
        IPropertySymbol property => GetAllMembers(concreteType).FirstOrDefault(m => IsPropertyMatch(m, property, compilation)),
        IEventSymbol @event => GetAllMembers(concreteType).OfType<IEventSymbol>()
            .FirstOrDefault(e => e.Name == @event.Name && IsPublicInstance(e) && SymbolEqualityComparer.Default.Equals(e.Type, @event.Type)),
        _ => null,
    };

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

    private static string? FindMemberMismatch(INamedTypeSymbol concreteType, ISymbol member, Compilation compilation)
    {
        if (FindCounterpart(member, concreteType, compilation) is not null)
            return null;

        return member switch
        {
            IMethodSymbol method => $"missing method '{method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'",
            IPropertySymbol property => DescribePropertyMismatch(concreteType, property),
            IEventSymbol @event => $"missing event '{@event.Name}' of type '{@event.Type.ToDisplayString()}'",
            _ => null,
        };
    }

    private static IMethodSymbol? FindMethod(INamedTypeSymbol concreteType, IMethodSymbol shapeMethod, Compilation compilation)
    {
        IMethodSymbol? assignable = null;
        foreach (var candidate in GetAllMembers(concreteType).OfType<IMethodSymbol>())
        {
            if (candidate.MethodKind != MethodKind.Ordinary || candidate.IsGenericMethod) continue;
            if (candidate.Name != shapeMethod.Name || !IsPublicInstance(candidate)) continue;
            if (candidate.Parameters.Length != shapeMethod.Parameters.Length) continue;

            if (SymbolEqualityComparer.Default.Equals(candidate.ReturnType, shapeMethod.ReturnType) &&
                candidate.Parameters.Zip(shapeMethod.Parameters, (c, s) => c.RefKind == s.RefKind && SymbolEqualityComparer.Default.Equals(c.Type, s.Type)).All(m => m))
                return candidate;

            // A void shape method discards the result, as TypeScript does for functions typed to return void.
            var returnMatches = shapeMethod.ReturnsVoid ||
                                (!candidate.ReturnsVoid && candidate.RefKind == RefKind.None && IsAssignable(candidate.ReturnType, shapeMethod.ReturnType, compilation));
            if (assignable is null && returnMatches && ParametersMatch(candidate.Parameters, shapeMethod.Parameters, compilation))
                assignable = candidate;
        }
        return assignable;
    }

    private static bool IsPropertyMatch(ISymbol candidate, IPropertySymbol shapeProperty, Compilation compilation) => candidate switch
    {
        IPropertySymbol property =>
            property.Name == shapeProperty.Name &&
            IsPublicInstance(property) &&
            (shapeProperty.GetMethod is null || property.GetMethod is { DeclaredAccessibility: Accessibility.Public }) &&
            (shapeProperty.SetMethod is null || property.SetMethod is { DeclaredAccessibility: Accessibility.Public }) &&
            IsValueMatch(property.Type, shapeProperty, compilation) &&
            ParametersMatch(property.Parameters, shapeProperty.Parameters, compilation),
        IFieldSymbol field =>
            !shapeProperty.IsIndexer &&
            field.Name == shapeProperty.Name &&
            IsPublicInstance(field) &&
            (shapeProperty.SetMethod is null || !field.IsReadOnly) &&
            IsValueMatch(field.Type, shapeProperty, compilation),
        _ => false,
    };

    // Reading is covariant and writing contravariant, so a property with both accessors needs a type convertible in both directions.
    private static bool IsValueMatch(ITypeSymbol candidateType, IPropertySymbol shapeProperty, Compilation compilation) =>
        (shapeProperty.GetMethod is null || IsAssignable(candidateType, shapeProperty.Type, compilation)) &&
        (shapeProperty.SetMethod is null || IsAssignable(shapeProperty.Type, candidateType, compilation));

    private static string DescribePropertyMismatch(INamedTypeSymbol concreteType, IPropertySymbol shapeProperty)
    {
        var displayName = shapeProperty.IsIndexer
            ? $"indexer '{shapeProperty.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'"
            : $"property '{shapeProperty.Name}'";

        var candidate = GetAllMembers(concreteType).FirstOrDefault(m => m switch
        {
            IPropertySymbol p => p.Name == shapeProperty.Name && IsPublicInstance(p) && p.Parameters.Length == shapeProperty.Parameters.Length,
            IFieldSymbol f => !shapeProperty.IsIndexer && f.Name == shapeProperty.Name && IsPublicInstance(f),
            _ => false,
        });

        return candidate switch
        {
            IPropertySymbol p when shapeProperty.GetMethod is not null && p.GetMethod is not { DeclaredAccessibility: Accessibility.Public } =>
                $"{displayName} has no public getter",
            IPropertySymbol p when shapeProperty.SetMethod is not null && p.SetMethod is not { DeclaredAccessibility: Accessibility.Public } =>
                $"{displayName} has no public setter",
            IFieldSymbol { IsReadOnly: true } when shapeProperty.SetMethod is not null =>
                $"{displayName} is a readonly field",
            IPropertySymbol or IFieldSymbol =>
                $"{displayName} is not compatible with type '{shapeProperty.Type.ToDisplayString()}'",
            _ => $"missing {displayName} of type '{shapeProperty.Type.ToDisplayString()}'",
        };
    }

    // Parameters are inputs, so the shape's parameter type has to convert to the candidate's; by-reference parameters stay exact.
    private static bool ParametersMatch(IReadOnlyList<IParameterSymbol> candidate, IReadOnlyList<IParameterSymbol> shape, Compilation compilation)
    {
        if (candidate.Count != shape.Count) return false;
        for (var i = 0; i < candidate.Count; i++)
        {
            if (candidate[i].RefKind != shape[i].RefKind) return false;
            var matches = candidate[i].RefKind == RefKind.None
                ? IsAssignable(shape[i].Type, candidate[i].Type, compilation)
                : SymbolEqualityComparer.Default.Equals(candidate[i].Type, shape[i].Type);
            if (!matches) return false;
        }
        return true;
    }

    private static bool IsAssignable(ITypeSymbol source, ITypeSymbol destination, Compilation compilation)
    {
        if (SymbolEqualityComparer.Default.Equals(source, destination)) return true;
        var conversion = compilation.ClassifyCommonConversion(source, destination);
        return conversion.IsImplicit && !conversion.IsUserDefined;
    }

    private static bool IsPublicInstance(ISymbol member) => !member.IsStatic && member.DeclaredAccessibility == Accessibility.Public;

    public static IEnumerable<ISymbol> GetAllMembers(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            foreach (var m in current.GetMembers())
                yield return m;
    }
}
