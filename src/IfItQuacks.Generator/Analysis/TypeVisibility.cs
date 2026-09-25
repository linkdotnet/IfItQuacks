using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

/// <summary>Which types generated code can name or expose, and where an adapter for a hidden type can live instead.</summary>
internal static class TypeVisibility
{
    public const string InaccessibleReason = "it is not accessible from generated code";

    // Generated code outside a type can't name its private or protected nested types, unless the adapter is nested in that type too.
    public static bool IsNameable(ITypeSymbol type) => type switch
    {
        INamedTypeSymbol named => named.EnclosingTypes().All(t => t.IsAnonymousType ||
                                      t.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal) &&
                                  named.TypeArguments.All(IsNameable),
        IArrayTypeSymbol array => IsNameable(array.ElementType),
        _ => true,
    };

    public static INamedTypeSymbol? GetAdapterHost(ITypeSymbol type) =>
        type is INamedTypeSymbol { ContainingType: { } host } named && !IsNameable(named) && IsNameable(host) &&
        named.TypeArguments.All(IsNameable) && !host.IsInGenericType() && !host.IsInFileLocalType() &&
        host.EnclosingTypes().All(t => t.DeclaringSyntaxReferences.Length > 0 && t.IsDeclaredPartial())
            ? host
            : null;

    public static bool IsPubliclyVisible(ITypeSymbol type) => type switch
    {
        INamedTypeSymbol named => named.EnclosingTypes().All(t => t.DeclaredAccessibility == Accessibility.Public) &&
                                  named.TypeArguments.All(IsPubliclyVisible),
        IArrayTypeSymbol array => IsPubliclyVisible(array.ElementType),
        _ => true,
    };
}
