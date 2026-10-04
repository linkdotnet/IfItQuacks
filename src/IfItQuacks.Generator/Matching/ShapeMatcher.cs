using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

internal static class ShapeMatcher
{
    public static string? FindMismatch(INamedTypeSymbol shape, INamedTypeSymbol concreteType, Compilation compilation)
    {
        if (concreteType.DelegateInvokeMethod is { } invoke)
            return FindDelegateMismatch(shape, invoke, compilation);

        var mapped = MappedShape.TryGet(shape);
        var mismatches = GetShapeMembers(shape).Where(IsRequired)
            .Select(member => FindMemberMismatch(concreteType, member, compilation, mapped))
            .OfType<string>()
            .ToList();
        return mismatches.Count == 0 ? null : string.Join("; ", mismatches);
    }

    public static ISymbol? FindUnsupportedMember(INamedTypeSymbol shape, bool allowGenericMethods = false) =>
        shape.GetMembers()
            .Concat(shape.AllInterfaces.SelectMany(i => i.GetMembers()))
            .FirstOrDefault(m => IsUnsupported(m) && !(allowGenericMethods && m is IMethodSymbol { IsGenericMethod: true, IsStatic: false }));

    /// <summary>The interface members <paramref name="concreteType"/> does not provide, which a stub implements by throwing.</summary>
    public static IEnumerable<ISymbol> FindUnimplementedMembers(INamedTypeSymbol shape, INamedTypeSymbol concreteType, Compilation compilation)
    {
        var mapped = MappedShape.TryGet(shape);
        return GetShapeMembers(shape).Where(IsRequired).Where(m => FindCounterpart(m, concreteType, compilation, mapped) is null);
    }

    // A stub fills in what is missing; a member that is there but doesn't fit is a mistake, not an omission.
    public static string? FindStubMismatch(INamedTypeSymbol shape, INamedTypeSymbol concreteType, Compilation compilation)
    {
        var mapped = MappedShape.TryGet(shape);
        return FindUnimplementedMembers(shape, concreteType, compilation)
            .Where(m => GetAllMembers(concreteType).Any(c => c.Name == m.Name && IsPublicInstance(c)))
            .Select(m => FindMemberMismatch(concreteType, m, compilation, mapped))
            .FirstOrDefault(mismatch => mismatch is not null);
    }

    public static IEnumerable<ISymbol> GetShapeMembers(INamedTypeSymbol shape) =>
        // A [DuckShape<>] interface is still empty here, because a generator can't see another's output.
        MappedShape.TryGet(shape) is { } mapped
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
        MappedShape? mapped = null)
    {
        // A delegate has no member named like the interface method; its Invoke stands in for it.
        if (concreteType.DelegateInvokeMethod is { } invoke)
            return member is IMethodSymbol method && IsDelegateMatch(method, invoke, compilation) ? invoke : null;

        return FindNamedCounterpart(member, concreteType, compilation, mapped);
    }

    private static ISymbol? FindNamedCounterpart(ISymbol member, INamedTypeSymbol concreteType, Compilation compilation,
        MappedShape? mapped = null) => member switch
    {
        IMethodSymbol method => FindMethod(concreteType, method, compilation) ?? FindDelegateMember(concreteType, method, compilation),
        IPropertySymbol or IFieldSymbol => GetAllMembers(concreteType).FirstOrDefault(m => IsPropertyMatch(m, member, compilation, mapped)),
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
        MappedShape? mapped = null)
    {
        if (FindCounterpart(member, concreteType, compilation, mapped) is not null)
            return null;

        return member switch
        {
            IMethodSymbol method => DescribeMethodMismatch(concreteType, method),
            IPropertySymbol or IFieldSymbol => DescribePropertyMismatch(concreteType, member),
            IEventSymbol @event => $"missing event '{@event.Name}' of type '{@event.Type.ToDisplayString()}'{NearMiss(concreteType, @event.Name)}",
            _ => null,
        };
    }

    private static string DescribeMethodMismatch(INamedTypeSymbol concreteType, IMethodSymbol shapeMethod)
    {
        var display = shapeMethod.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return GetAllMembers(concreteType).OfType<IMethodSymbol>().FirstOrDefault(m => m.Name == shapeMethod.Name && IsPublicInstance(m)) is { } candidate
            ? $"method '{candidate.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' doesn't match '{display}'"
            : $"missing method '{display}'{NearMiss(concreteType, shapeMethod.Name)}";
    }

    // Explains the likeliest reason a member wasn't found: it exists, just not as a public instance member, or with other casing.
    private static string NearMiss(INamedTypeSymbol concreteType, string name)
    {
        var members = GetAllMembers(concreteType).Where(m => !m.IsImplicitlyDeclared).ToList();
        if (members.FirstOrDefault(m => m.Name == name) is { } sameName)
            return sameName.IsStatic ? $" ('{name}' is static)" : $" ('{name}' is not public)";

        return members.FirstOrDefault(m => IsPublicInstance(m) && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) is { } otherCase
            ? $" - did you mean '{otherCase.Name}'?"
            : "";
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
    private static bool NeedsSetter(ISymbol shapeMember, MappedShape? mapped) =>
        HasSetter(shapeMember) && (mapped is null || mapped.KeepsSetter(shapeMember));

    // A field derived by [DuckShape<>] is declared as a property in the generated interface, so it is matched like one.
    private static bool HasSetter(ISymbol shapeMember) => shapeMember is IPropertySymbol { SetMethod: not null } or IFieldSymbol { IsReadOnly: false };

    private static bool HasGetter(ISymbol shapeMember) => shapeMember is not IPropertySymbol { GetMethod: null };

    private static ITypeSymbol ValueType(ISymbol shapeMember) => shapeMember is IFieldSymbol field ? field.Type : ((IPropertySymbol)shapeMember).Type;

    private static bool IsPropertyMatch(ISymbol candidate, ISymbol shapeMember, Compilation compilation,
        MappedShape? mapped = null)
    {
        var shapeProperty = shapeMember as IPropertySymbol;
        return candidate switch
        {
            IPropertySymbol property =>
                property.Name == shapeMember.Name &&
                IsPublicInstance(property) &&
                (shapeProperty is null or { RefKind: RefKind.None } || IsRefMatch(property.RefKind, property.Type, shapeProperty.RefKind, shapeProperty.Type)) &&
                (!HasGetter(shapeMember) || property.GetMethod is { DeclaredAccessibility: Accessibility.Public }) &&
                (!NeedsSetter(shapeMember, mapped) || IsUsableSetter(property.SetMethod)) &&
                IsValueMatch(property.Type, shapeMember, compilation, mapped) &&
                ParametersMatch(property.Parameters, shapeProperty?.Parameters ?? [], compilation),
            IFieldSymbol field =>
                shapeProperty is null or { IsIndexer: false, RefKind: RefKind.None } &&
                field.Name == shapeMember.Name &&
                IsPublicInstance(field) &&
                (!NeedsSetter(shapeMember, mapped) || !field.IsReadOnly) &&
                IsValueMatch(field.Type, shapeMember, compilation, mapped),
            _ => false,
        };
    }

    // Reading is covariant and writing contravariant, so a property with both accessors needs a type convertible in both directions.
    private static bool IsValueMatch(ITypeSymbol candidateType, ISymbol shapeMember, Compilation compilation,
        MappedShape? mapped = null)
    {
        // Under 'Optional' the derived member is declared nullable, so that is what a counterpart has to fit.
        var shapeType = MappedShape.OptionalType(shapeMember, ValueType(shapeMember), mapped, compilation);
        return (!HasGetter(shapeMember) || IsAssignable(candidateType, shapeType, compilation)) &&
               (!NeedsSetter(shapeMember, mapped) || IsAssignable(shapeType, candidateType, compilation));
    }

    private static string DescribePropertyMismatch(INamedTypeSymbol concreteType, ISymbol shapeMember)
    {
        var isIndexer = shapeMember is IPropertySymbol { IsIndexer: true };
        var parameterCount = (shapeMember as IPropertySymbol)?.Parameters.Length ?? 0;
        var typeName = ValueType(shapeMember).ToDisplayString();
        var displayName = isIndexer
            ? $"indexer '{shapeMember.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'"
            : $"property '{shapeMember.Name}'";

        var candidate = GetAllMembers(concreteType).FirstOrDefault(m => m switch
        {
            IPropertySymbol p => p.Name == shapeMember.Name && IsPublicInstance(p) && p.Parameters.Length == parameterCount,
            IFieldSymbol f => !isIndexer && f.Name == shapeMember.Name && IsPublicInstance(f),
            _ => false,
        });

        return candidate switch
        {
            IPropertySymbol p when HasGetter(shapeMember) && p.GetMethod is not { DeclaredAccessibility: Accessibility.Public } =>
                $"{displayName} has no public getter",
            IPropertySymbol { SetMethod.IsInitOnly: true } when HasSetter(shapeMember) =>
                $"{displayName} has an init-only setter",
            IPropertySymbol p when HasSetter(shapeMember) && !IsUsableSetter(p.SetMethod) =>
                $"{displayName} has no public setter",
            IFieldSymbol { IsReadOnly: true } when HasSetter(shapeMember) =>
                $"{displayName} is a readonly field",
            IPropertySymbol or IFieldSymbol =>
                $"{displayName} is not compatible with type '{typeName}'",
            _ => $"missing {displayName} of type '{typeName}'" + (isIndexer ? "" : NearMiss(concreteType, shapeMember.Name)),
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
