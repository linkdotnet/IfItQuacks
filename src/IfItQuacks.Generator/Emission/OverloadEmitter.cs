using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

/// <summary>
/// Emits concrete overloads for calls an interceptor can't serve: generic <c>[DuckTyped]</c> methods, duck-typed constraints
/// and method groups. Overloads of one method share a file, so call sites needing the same overload generate it once.
/// </summary>
internal static class OverloadEmitter
{
    // Interceptors must keep the intercepted signature, so a generic fallback can't return the per-call inferred type; concrete overloads can.
    public static OverloadMember ForDuckTypedCall(IMethodSymbol method, IMethodSymbol constructed, IReadOnlyDictionary<int, ResolvedArgument> adaptedArguments)
    {
        var parameters = string.Join(", ", constructed.Parameters.Select(p =>
            SourceSyntax.DeclareParameter(method, p, adaptedArguments.TryGetValue(p.Ordinal, out var adapted) ? adapted.ConcreteType.ToDisplayString() : p.Type.ToDisplayString()) +
            SourceSyntax.DefaultValue(p)));

        var arguments = string.Join(", ", constructed.Parameters.Select(p =>
            adaptedArguments.TryGetValue(p.Ordinal, out var adapted) && adapted.AdapterReference is not null
                ? $"new {adapted.AdapterReference}({SourceSyntax.Identifier(p.Name)})"
                : SourceSyntax.Argument(p)));

        var typeArguments = constructed.TypeArguments.IsEmpty
            ? ""
            : $"<{string.Join(", ", constructed.TypeArguments.Select(t => t.ToDisplayString()))}>";
        var accessibility = AccessibilityFor(method, adaptedArguments.Values.Select(a => a.ConcreteType));
        return Member(method,
            $"{GeneratedCode.EditorBrowsableNever} {accessibility} {(method.IsStatic ? "static " : "")}{constructed.ReturnType.ToDisplayString()} {method.Name}({parameters}) => " +
            $"{method.Name}{typeArguments}({arguments});");
    }

    public static OverloadMember? ForConstraintCall(IMethodSymbol method,
        IReadOnlyDictionary<ITypeParameterSymbol, TypeParameterBinding> typeArguments, IReadOnlyDictionary<int, ResolvedArgument> adaptedArguments)
    {
        string? BoundTypeName(ITypeSymbol type) =>
            type is ITypeParameterSymbol typeParameter && typeArguments.TryGetValue(typeParameter, out var binding) ? binding.ConcreteType.ToDisplayString() : null;

        if (method.Parameters.Any(p => BoundTypeName(p.Type) is null && p.Type.ContainsAnyTypeParameter()) ||
            (BoundTypeName(method.ReturnType) is null && method.ReturnType.ContainsAnyTypeParameter()))
            return null;

        var parameters = string.Join(", ", method.Parameters.Select(p => adaptedArguments.TryGetValue(p.Ordinal, out var adapted)
            ? SourceSyntax.DeclareParameter(method, p, adapted.ConcreteType.ToDisplayString())
            : SourceSyntax.DeclareParameter(method, p, BoundTypeName(p.Type) ?? p.Type.ToDisplayString()) + SourceSyntax.DefaultValue(p)));

        var arguments = string.Join(", ", method.Parameters.Select(p =>
        {
            // The cast keeps overload resolution on the user's method instead of the generated overload.
            if (adaptedArguments.TryGetValue(p.Ordinal, out var adapted))
                return $"({p.Type.ToDisplayString()})(new {adapted.AdapterReference}({SourceSyntax.Identifier(p.Name)}))";

            return p.Type is ITypeParameterSymbol typeParameter && typeArguments.TryGetValue(typeParameter, out var binding) && binding.AdapterReference is { } adapter
                ? $"new {adapter}({SourceSyntax.Identifier(p.Name)})"
                : SourceSyntax.Argument(p);
        }));

        var call = $"{method.Name}<{string.Join(", ", method.TypeParameters.Select(tp => typeArguments[tp].AdapterReference ?? typeArguments[tp].ConcreteType.ToDisplayString()))}>({arguments})";
        var accessibility = AccessibilityFor(method,
            typeArguments.Values.Select<TypeParameterBinding, ITypeSymbol>(b => b.ConcreteType).Concat(adaptedArguments.Values.Select(a => a.ConcreteType)));
        return Member(method,
            $"{GeneratedCode.EditorBrowsableNever} {accessibility} {(method.IsStatic ? "static " : "")}{BoundTypeName(method.ReturnType) ?? method.ReturnType.ToDisplayString()} " +
            $"{method.Name}({parameters}) => {ConvertAdaptedResult(method, typeArguments, call)};");
    }

    // A method returning the constrained type parameter returns an adapter, which converts back to the argument's type.
    private static string ConvertAdaptedResult(IMethodSymbol method, IReadOnlyDictionary<ITypeParameterSymbol, TypeParameterBinding> typeArguments, string call) =>
        !method.ReturnsVoid && method.ReturnType is ITypeParameterSymbol returned &&
        typeArguments.TryGetValue(returned, out var binding) && binding.AdapterReference is not null
            ? $"({binding.ConcreteType.ToDisplayString()})({call})"
            : call;

    // A public overload can't expose a non-public argument type, so it falls back to internal.
    private static string AccessibilityFor(IMethodSymbol method, IEnumerable<ITypeSymbol> concreteTypes) =>
        method.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal &&
        !concreteTypes.All(TypeVisibility.IsPubliclyVisible)
            ? "internal"
            : SourceSyntax.AccessibilityKeyword(method.DeclaredAccessibility);

    private static OverloadMember Member(IMethodSymbol method, string member)
    {
        var (prefix, suffix) = TypeWrapper.WrapTemplate(method.ContainingType);
        return new OverloadMember(GeneratedCode.HintName("Overloads", method.ContainingType, method.Name), GeneratedCode.Header + prefix, suffix, member);
    }
}
