using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IfItQuacks.Generator;

internal static class SymbolExtensions
{
    public static IEnumerable<INamedTypeSymbol> EnclosingTypes(this INamedTypeSymbol type)
    {
        for (var enclosing = type; enclosing is not null; enclosing = enclosing.ContainingType)
            yield return enclosing;
    }

    public static bool IsInGenericType(this INamedTypeSymbol type) => type.EnclosingTypes().Any(t => t.IsGenericType);

    public static bool IsInFileLocalType(this INamedTypeSymbol type) => type.EnclosingTypes().Any(t => t.IsFileLocal);

    public static bool IsDeclaredPartial(this INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .All(d => d.Modifiers.Any(SyntaxKind.PartialKeyword));

    public static bool Overrides(this IMethodSymbol method, IMethodSymbol other)
    {
        for (var overridden = method.OverriddenMethod; overridden is not null; overridden = overridden.OverriddenMethod)
        {
            if (SymbolEqualityComparer.Default.Equals(overridden.OriginalDefinition, other.OriginalDefinition))
                return true;
        }

        return false;
    }

    public static IMethodSymbol OverriddenRoot(this IMethodSymbol method)
    {
        while (method.OverriddenMethod is { } overridden)
            method = overridden;
        return method;
    }

    public static bool HasBuiltInImplicitConversion(this Compilation compilation, ITypeSymbol source, ITypeSymbol destination) =>
        compilation.ClassifyCommonConversion(source, destination) is { IsImplicit: true, IsUserDefined: false };

    public static bool ContainsType(this ITypeSymbol type, Func<ITypeSymbol, bool> predicate) =>
        predicate(type) || type switch
        {
            INamedTypeSymbol named => named.TypeArguments.Any(t => t.ContainsType(predicate)),
            IArrayTypeSymbol array => array.ElementType.ContainsType(predicate),
            _ => false,
        };

    public static bool Mentions(this ITypeSymbol type, ITypeSymbol other) =>
        type.ContainsType(t => SymbolEqualityComparer.Default.Equals(t, other));

    public static bool ContainsAnyTypeParameter(this ITypeSymbol type) => type.ContainsType(t => t is ITypeParameterSymbol);

    public static bool ContainsErrorType(this ITypeSymbol type) => type.ContainsType(t => t.TypeKind == TypeKind.Error);

    public static ITypeSymbol WithoutNullability(this ITypeSymbol type) => type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

    public static ITypeSymbol SubstituteTypeParameter(this ITypeSymbol type, ITypeParameterSymbol typeParameter, ITypeSymbol replacement) => type switch
    {
        ITypeParameterSymbol parameter when SymbolEqualityComparer.Default.Equals(parameter, typeParameter) => replacement,
        INamedTypeSymbol { IsGenericType: true } named =>
            named.OriginalDefinition.Construct([.. named.TypeArguments.Select(t => t.SubstituteTypeParameter(typeParameter, replacement))]),
        _ => type,
    };
}
