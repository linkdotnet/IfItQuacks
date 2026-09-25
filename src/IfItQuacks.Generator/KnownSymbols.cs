using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

internal static class KnownSymbols
{
    private const string Namespace = "IfItQuacks";

    public static bool IsDuckTypedAttribute(INamedTypeSymbol? type) =>
        type is { Name: "DuckTypedAttribute", Arity: 0, ContainingType: null } && IsIfItQuacksNamespace(type.ContainingNamespace);

    public static bool IsDuckShapeAttribute(INamedTypeSymbol? type) =>
        type is { MetadataName: "DuckShapeAttribute`1" } && IsIfItQuacksNamespace(type.ContainingNamespace);

    public static bool IsDuckType(INamedTypeSymbol type) =>
        type is { Name: "Duck", Arity: 0, ContainingType: null } && IsIfItQuacksNamespace(type.ContainingNamespace);

    private static bool IsIfItQuacksNamespace(INamespaceSymbol? ns) =>
        ns is { Name: Namespace, ContainingNamespace.IsGlobalNamespace: true };
}
