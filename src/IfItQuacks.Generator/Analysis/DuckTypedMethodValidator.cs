using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

/// <summary>Decides whether a <c>[DuckTyped]</c> method can get a fallback and interceptors, and reports why not.</summary>
internal static class DuckTypedMethodValidator
{
    public static bool IsValid(IMethodSymbol method, Compilation compilation) =>
        Validate(method, compilation, Location.None, diagnostics: null);

    public static bool Validate(IMethodSymbol method, Compilation compilation, Location location, ImmutableArray<Diagnostic>.Builder? diagnostics)
    {
        if (!method.ContainingType.IsDeclaredPartial())
            diagnostics?.Add(Diagnostic.Create(Diagnostics.ContainingTypeNotPartial, location, method.Name, method.ContainingType.Name));

        var violation = FindViolation(method, compilation, location);
        if (violation is not null)
            diagnostics?.Add(violation);

        return violation is null;
    }

    private static Diagnostic? FindViolation(IMethodSymbol method, Compilation compilation, Location location)
    {
        if (FindUnsupportedContainingType(method.ContainingType) is { } containingTypeReason)
            return Diagnostics.CreateUnsupportedSignature(location, method, containingTypeReason);

        var constraintTypeParameters = DuckTypedSignature.GetDuckConstraintTypeParameters(method);
        if (!constraintTypeParameters.IsEmpty)
        {
            return method.TypeParameters.Length == constraintTypeParameters.Length
                ? null
                : Diagnostics.CreateUnsupportedSignature(location, method, "not all of its type parameters have a single interface constraint used by a parameter");
        }

        var duckParameters = DuckTypedSignature.GetDuckParameters(method);
        if (duckParameters.IsEmpty)
            return Diagnostic.Create(Diagnostics.ParameterNotShape, location, method.Name);

        var reason = FindUnusedTypeParameter(method, duckParameters)
                     ?? FindOtherDuckTypedOverload(method)
                     ?? FindOverloadNeedingPriority(method, compilation)
                     ?? FindFallbackCollision(method, duckParameters)
                     ?? FindProtectedOverride(method, compilation)
                     ?? FindHiddenInheritedOverload(method, compilation);
        return reason is null ? null : Diagnostics.CreateUnsupportedSignature(location, method, reason);
    }

    private static string? FindUnsupportedContainingType(INamedTypeSymbol type) => type switch
    {
        _ when type.IsInGenericType() => "its containing type is generic",
        { TypeKind: TypeKind.Interface } => "its containing type is an interface",
        // A file-local type can't be extended by a partial declaration in the generated file.
        _ when type.IsInFileLocalType() => "its containing type is file-local",
        _ => null,
    };

    private static string? FindUnusedTypeParameter(IMethodSymbol method, ImmutableArray<IParameterSymbol> duckParameters) =>
        method.TypeParameters.FirstOrDefault(tp => !duckParameters.Any(p => p.Type.Mentions(tp))) is { } unused
            ? $"its type parameter '{unused.Name}' is not used by an interface parameter"
            : null;

    private static string? FindOtherDuckTypedOverload(IMethodSymbol method) =>
        method.ContainingType.GetMembers(method.Name).OfType<IMethodSymbol>().Count(KnownSymbols.IsDuckTyped) > 1
            ? "it is overloaded (only one [DuckTyped] method per name is supported)"
            : null;

    // Without a lower priority the generic fallback would take the calls meant for the other overload and throw at runtime.
    private static string? FindOverloadNeedingPriority(IMethodSymbol method, Compilation compilation) =>
        !method.IsGenericMethod && !KnownSymbols.SupportsOverloadPriority(compilation) &&
        InheritedOverloads.UserDeclaredMethods(method.ContainingType, method.Name).FirstOrDefault(m => m.MethodKind == MethodKind.Ordinary && !KnownSymbols.IsDuckTyped(m)) is { } overload
            ? $"it is overloaded by '{Display(overload)}', which requires C# 13"
            : null;

    private static string? FindFallbackCollision(IMethodSymbol method, ImmutableArray<IParameterSymbol> duckParameters)
    {
        if (method.IsGenericMethod)
            return null;

        var variants = FallbackSignature.GetGenericParameterSubsets(duckParameters).ToImmutableArray();
        return InheritedOverloads.UserDeclaredMethods(method.ContainingType, method.Name).FirstOrDefault(m =>
                   m is { MethodKind: MethodKind.Ordinary, IsGenericMethod: true } && !KnownSymbols.IsDuckTyped(m) &&
                   variants.Any(genericParameters => FallbackSignature.Matches(m, method, genericParameters))) is { } conflict
            ? $"it is overloaded by '{Display(conflict)}', which its generated fallback would collide with"
            : null;
    }

    // Inside the derived type the generated fallback hides the overridden method, so it is called through the base type, which protected access forbids.
    private static string? FindProtectedOverride(IMethodSymbol method, Compilation compilation)
    {
        var overridden = method.OverriddenRoot();
        return method.IsOverride && !compilation.IsSymbolAccessibleWithin(overridden, method.ContainingType, overridden.ContainingType)
            ? $"it overrides the protected '{Display(overridden)}', which its generated fallback can't call without calling itself"
            : null;
    }

    // The fallback's lower priority only lets an inherited overload win once it is redeclared next to it, which needs C# 13 like any overload.
    private static string? FindHiddenInheritedOverload(IMethodSymbol method, Compilation compilation)
    {
        var inherited = InheritedOverloads.Find(method, compilation);
        if (inherited.Unsupported is { } unsupported)
            return $"it is overloaded by the inherited '{Display(unsupported)}', which its generated fallback would hide";

        return !inherited.Forwarded.IsEmpty && !KnownSymbols.SupportsOverloadPriority(compilation)
            ? $"it is overloaded by the inherited '{Display(inherited.Forwarded[0])}', which requires C# 13"
            : null;
    }

    private static string Display(ISymbol symbol) => symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
}
