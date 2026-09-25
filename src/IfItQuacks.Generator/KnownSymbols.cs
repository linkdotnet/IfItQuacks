using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace IfItQuacks.Generator;

/// <summary>Names and identities of the IfItQuacks and framework symbols the generator reacts to.</summary>
internal static class KnownSymbols
{
    public const string DuckTypedAttributeMetadataName = "IfItQuacks.DuckTypedAttribute";
    public const string DuckShapeAttributeMetadataName = "IfItQuacks.DuckShapeAttribute`1";
    public const string OverloadResolutionPriorityAttributeMetadataName = "System.Runtime.CompilerServices.OverloadResolutionPriorityAttribute";
    public const string DuckAsMethod = "As";
    public const string DuckStubMethod = "Stub";
    public const string DuckMergeMethod = "Merge";
    public const string DuckToMethod = "To";

    private const string Namespace = "IfItQuacks";

    public static bool IsDuckTyped(IMethodSymbol method) => method.GetAttributes().Any(a => IsDuckTypedAttribute(a.AttributeClass));

    public static bool IsDuckShapeAttribute(INamedTypeSymbol? type) =>
        type is { MetadataName: "DuckShapeAttribute`1" } && IsIfItQuacksNamespace(type.ContainingNamespace);

    public static bool IsDuckConversion(IMethodSymbol method, Compilation compilation) =>
        method.Name is DuckAsMethod or DuckStubMethod or DuckMergeMethod or DuckToMethod &&
        method.TypeArguments.Length == 1 &&
        method.ContainingType is { Name: "Duck", Arity: 0, ContainingType: null } duck && IsIfItQuacksNamespace(duck.ContainingNamespace) &&
        SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly);

    public static bool SupportsOverloadPriority(Compilation compilation) =>
        compilation is CSharpCompilation { LanguageVersion: >= LanguageVersion.CSharp13 };

    public static bool LacksOverloadPriorityAttribute(Compilation compilation) =>
        compilation.GetTypeByMetadataName(OverloadResolutionPriorityAttributeMetadataName) is null;

    private static bool IsDuckTypedAttribute(INamedTypeSymbol? type) =>
        type is { Name: "DuckTypedAttribute", Arity: 0, ContainingType: null } && IsIfItQuacksNamespace(type.ContainingNamespace);

    private static bool IsIfItQuacksNamespace(INamespaceSymbol? ns) =>
        ns is { Name: Namespace, ContainingNamespace.IsGlobalNamespace: true };
}
