using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

internal static class ShapeMatcher
{
    public static string? FindMismatch(INamedTypeSymbol shape, INamedTypeSymbol concreteType, Compilation compilation)
    {
        if (concreteType.DelegateInvokeMethod is { } invoke)
            return FindDelegateMismatch(shape, invoke, compilation);

        var mapped = MappedShapeEmitter.TryGetInfo(shape);
        foreach (var member in GetShapeMembers(shape).Where(IsRequired))
        {
            var mismatch = FindMemberMismatch(concreteType, member, compilation, mapped);
            if (mismatch is not null) return mismatch;
        }
        return null;
    }

    public static ISymbol? FindUnsupportedMember(INamedTypeSymbol shape, bool allowGenericMethods = false) =>
        shape.GetMembers()
            .Concat(shape.AllInterfaces.SelectMany(i => i.GetMembers()))
            .FirstOrDefault(m => IsUnsupported(m) && !(allowGenericMethods && m is IMethodSymbol { IsGenericMethod: true, IsStatic: false }));

    /// <summary>The interface members <paramref name="concreteType"/> does not provide, which a stub implements by throwing.</summary>
    public static IEnumerable<ISymbol> FindUnimplementedMembers(INamedTypeSymbol shape, INamedTypeSymbol concreteType, Compilation compilation) =>
        GetShapeMembers(shape).Where(IsRequired)
            .Where(m => FindCounterpart(m, concreteType, compilation, MappedShapeEmitter.TryGetInfo(shape)) is null);

    // A stub fills in what is missing; a member that is there but doesn't fit is a mistake, not an omission.
    public static string? FindStubMismatch(INamedTypeSymbol shape, INamedTypeSymbol concreteType, Compilation compilation) =>
        FindUnimplementedMembers(shape, concreteType, compilation)
            .Where(m => GetAllMembers(concreteType).Any(c => c.Name == m.Name && IsPublicInstance(c)))
            .Select(m => FindMemberMismatch(concreteType, m, compilation, MappedShapeEmitter.TryGetInfo(shape)))
            .FirstOrDefault(mismatch => mismatch is not null);

    public static IEnumerable<ISymbol> GetShapeMembers(INamedTypeSymbol shape) =>
        // A [DuckShape<>] interface is still empty here, because a generator can't see another's output.
        MappedShapeEmitter.TryGetInfo(shape) is { } mapped
            ? mapped.Members
            : shape.GetMembers()
                .Concat(shape.AllInterfaces.SelectMany(i => i.GetMembers()))
                .Where(IsRelevant);

    // Members with a default implementation are optional: the adapter forwards them only if the concrete type provides a match.
    // A member derived from a [DuckShape<>] source is declared abstract in the generated interface,
    // even though the symbol it came from is an ordinary class member.
    public static bool IsRequired(ISymbol member) =>
        member.IsAbstract || member.ContainingType.TypeKind != TypeKind.Interface;

    public static ISymbol? FindCounterpart(ISymbol member, INamedTypeSymbol concreteType, Compilation compilation,
        MappedShapeEmitter.Info? mapped = null)
    {
        // A delegate has no member named like the interface method; its Invoke stands in for it.
        if (concreteType.DelegateInvokeMethod is { } invoke)
            return member is IMethodSymbol method && IsDelegateMatch(method, invoke, compilation) ? invoke : null;

        return FindNamedCounterpart(member, concreteType, compilation, mapped);
    }

    private static ISymbol? FindNamedCounterpart(ISymbol member, INamedTypeSymbol concreteType, Compilation compilation,
        MappedShapeEmitter.Info? mapped = null) => member switch
    {
        IMethodSymbol method => FindMethod(concreteType, method, compilation) ?? FindDelegateMember(concreteType, method, compilation),
        IPropertySymbol property => GetAllMembers(concreteType).FirstOrDefault(m => IsPropertyMatch(m, property, compilation, mapped)),
        IEventSymbol @event => GetAllMembers(concreteType).OfType<IEventSymbol>()
            .FirstOrDefault(e => e.Name == @event.Name && IsPublicInstance(e) && SymbolEqualityComparer.Default.Equals(e.Type, @event.Type)),
        _ => null,
    };

    // A delegate can stand in for an interface with exactly one required method, like a functional interface.
    private static string? FindDelegateMismatch(INamedTypeSymbol shape, IMethodSymbol invoke, Compilation compilation)
    {
        var required = GetShapeMembers(shape).Where(IsRequired).ToList();
        if (required.Count != 1 || required[0] is not IMethodSymbol method)
            return "a delegate can only satisfy an interface with a single method";

        return IsDelegateMatch(method, invoke, compilation)
            ? null
            : $"the delegate's signature doesn't match '{method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'";
    }

    private static bool IsDelegateMatch(IMethodSymbol shapeMethod, IMethodSymbol invoke, Compilation compilation)
    {
        var returnMatches = shapeMethod.RefKind != RefKind.None
            ? IsRefMatch(invoke.RefKind, invoke.ReturnType, shapeMethod.RefKind, shapeMethod.ReturnType)
            : shapeMethod.ReturnsVoid ||
              (!invoke.ReturnsVoid && invoke.RefKind == RefKind.None && IsAssignable(invoke.ReturnType, shapeMethod.ReturnType, compilation));

        return returnMatches && ParametersMatch(invoke.Parameters, shapeMethod.Parameters, compilation);
    }

    public static bool IsRelevant(ISymbol member)
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
        IMethodSymbol { MethodKind: MethodKind.Ordinary, IsStatic: false } method => method.IsGenericMethod,
        _ => false,
    };

    private static string? FindMemberMismatch(INamedTypeSymbol concreteType, ISymbol member, Compilation compilation,
        MappedShapeEmitter.Info? mapped = null)
    {
        if (FindCounterpart(member, concreteType, compilation, mapped) is not null)
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

            if (shapeMethod.RefKind != RefKind.None)
            {
                if (IsRefMatch(candidate.RefKind, candidate.ReturnType, shapeMethod.RefKind, shapeMethod.ReturnType) &&
                    ParametersMatch(candidate.Parameters, shapeMethod.Parameters, compilation))
                    return candidate;
                continue;
            }

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

    /// <summary>The delegate type a member stands in for when it satisfies an interface method, or <c>null</c>.</summary>
    public static INamedTypeSymbol? DelegateTypeOf(ISymbol member) => member switch
    {
        IPropertySymbol { IsIndexer: false, GetMethod.DeclaredAccessibility: Accessibility.Public } property =>
            property.Type as INamedTypeSymbol,
        IFieldSymbol field => field.Type as INamedTypeSymbol,
        _ => null,
    } is { DelegateInvokeMethod: not null } delegateType ? delegateType : null;

    // A member holding a delegate stands in for the interface method of the same name, which is what makes 'new { Load = (int id) => ... }' a duck.
    private static ISymbol? FindDelegateMember(INamedTypeSymbol concreteType, IMethodSymbol shapeMethod, Compilation compilation) =>
        GetAllMembers(concreteType)
            .FirstOrDefault(m => m.Name == shapeMethod.Name && IsPublicInstance(m) &&
                                 DelegateTypeOf(m) is { DelegateInvokeMethod: { } invoke } &&
                                 IsDelegateMatch(shapeMethod, invoke, compilation));

    /// <summary>Under <c>Readonly</c> the derived member has no setter, so no counterpart needs one either.</summary>
    private static bool NeedsSetter(IPropertySymbol shapeProperty, MappedShapeEmitter.Info? mapped) =>
        shapeProperty.SetMethod is not null && (mapped is null || MappedShapeEmitter.KeepsSetter(shapeProperty, mapped));

    private static bool IsPropertyMatch(ISymbol candidate, IPropertySymbol shapeProperty, Compilation compilation,
        MappedShapeEmitter.Info? mapped = null) => candidate switch
    {
        IPropertySymbol property =>
            property.Name == shapeProperty.Name &&
            IsPublicInstance(property) &&
            (shapeProperty.RefKind == RefKind.None || IsRefMatch(property.RefKind, property.Type, shapeProperty.RefKind, shapeProperty.Type)) &&
            (shapeProperty.GetMethod is null || property.GetMethod is { DeclaredAccessibility: Accessibility.Public }) &&
            (!NeedsSetter(shapeProperty, mapped) || IsUsableSetter(property.SetMethod)) &&
            IsValueMatch(property.Type, shapeProperty, compilation, mapped) &&
            ParametersMatch(property.Parameters, shapeProperty.Parameters, compilation),
        IFieldSymbol field =>
            shapeProperty is { IsIndexer: false, RefKind: RefKind.None } &&
            field.Name == shapeProperty.Name &&
            IsPublicInstance(field) &&
            (!NeedsSetter(shapeProperty, mapped) || !field.IsReadOnly) &&
            IsValueMatch(field.Type, shapeProperty, compilation, mapped),
        _ => false,
    };

    // Reading is covariant and writing contravariant, so a property with both accessors needs a type convertible in both directions.
    private static bool IsValueMatch(ITypeSymbol candidateType, IPropertySymbol shapeProperty, Compilation compilation,
        MappedShapeEmitter.Info? mapped = null)
    {
        // Under 'Optional' the derived member is declared nullable, so that is what a counterpart has to fit.
        var shapeType = MappedShapeEmitter.OptionalType(shapeProperty, shapeProperty.Type, mapped, compilation);
        return (shapeProperty.GetMethod is null || IsAssignable(candidateType, shapeType, compilation)) &&
               (!NeedsSetter(shapeProperty, mapped) || IsAssignable(shapeType, candidateType, compilation));
    }

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
            IPropertySymbol { SetMethod.IsInitOnly: true } when shapeProperty.SetMethod is not null =>
                $"{displayName} has an init-only setter",
            IPropertySymbol p when shapeProperty.SetMethod is not null && !IsUsableSetter(p.SetMethod) =>
                $"{displayName} has no public setter",
            IFieldSymbol { IsReadOnly: true } when shapeProperty.SetMethod is not null =>
                $"{displayName} is a readonly field",
            IPropertySymbol or IFieldSymbol =>
                $"{displayName} is not compatible with type '{shapeProperty.Type.ToDisplayString()}'",
            _ => $"missing {displayName} of type '{shapeProperty.Type.ToDisplayString()}'",
        };
    }

    // An init-only setter can only be called from an object initializer or a constructor, so the adapter can't forward to it.
    private static bool IsUsableSetter(IMethodSymbol? setter) =>
        setter is { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false };

    // A by-reference result aliases storage, so its type has to match exactly; a writable ref also satisfies a ref readonly member.
    private static bool IsRefMatch(RefKind candidateKind, ITypeSymbol candidateType, RefKind shapeKind, ITypeSymbol shapeType) =>
        IsRefKindCompatible(candidateKind, shapeKind) && SymbolEqualityComparer.Default.Equals(candidateType, shapeType);

    public static bool IsRefKindCompatible(RefKind candidateKind, RefKind shapeKind) =>
        candidateKind == shapeKind || (shapeKind == RefKind.RefReadOnly && candidateKind == RefKind.Ref);

    // Parameters are inputs, so the shape's parameter type has to convert to the candidate's; by-reference parameters stay exact.
    public static bool ParametersMatch(IReadOnlyList<IParameterSymbol> candidate, IReadOnlyList<IParameterSymbol> shape, Compilation compilation)
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

    public static bool IsAssignable(ITypeSymbol source, ITypeSymbol destination, Compilation compilation)
    {
        if (SymbolEqualityComparer.Default.Equals(source, destination)) return true;
        var conversion = compilation.ClassifyCommonConversion(source, destination);
        return conversion.IsImplicit && !conversion.IsUserDefined;
    }

    public static bool IsPublicInstance(ISymbol member) => !member.IsStatic && member.DeclaredAccessibility == Accessibility.Public;

    // An interface-typed value also exposes the members of its base interfaces, which are not part of its base type chain.
    public static IEnumerable<ISymbol> GetAllMembers(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            foreach (var m in current.GetMembers())
                yield return m;

        if (type.TypeKind == TypeKind.Interface)
            foreach (var m in type.AllInterfaces.SelectMany(i => i.GetMembers()))
                yield return m;
    }
}
