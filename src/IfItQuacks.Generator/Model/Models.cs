using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

internal sealed record GeneratedFile(string Name, string Source);

// Prefix and suffix wrap the members in their containing namespace and types, so overloads from different call sites share one file.
internal sealed record OverloadMember(string FileName, string Prefix, string Suffix, string Member);

internal sealed record MappedShapeOutput(GeneratedFile? File, EquatableArray<Diagnostic> Diagnostics);

internal sealed record DuckTypedMethodOutput(DuckMethodRef? ValidMethod, GeneratedFile? Fallback, EquatableArray<Diagnostic> Diagnostics);

// An extension method call on a receiver that doesn't implement the interface has no symbol, so it is looked up by name instead.
internal sealed record DuckMethodRef(string Name, string ContainingType, bool IsExtension)
{
    public static DuckMethodRef From(IMethodSymbol method) => new(method.Name, MetadataName(method.ContainingType), method.IsExtensionMethod);

    public static string MetadataName(INamedTypeSymbol type)
    {
        var nested = string.Join("+", type.EnclosingTypes().Reverse().Select(t => t.MetadataName));
        return type.ContainingNamespace.IsGlobalNamespace ? nested : type.ContainingNamespace.ToDisplayString() + "." + nested;
    }
}

internal sealed record CallSiteOutput(
    string? Interceptor,
    EquatableArray<GeneratedFile> Adapters,
    OverloadMember? Overload,
    EquatableArray<Diagnostic> Diagnostics,
    EquatableArray<OverloadMember> NestedAdapters = default)
{
    public IEnumerable<OverloadMember> OverloadMembers => Overload is null ? NestedAdapters : NestedAdapters.Prepend(Overload);

    public static CallSiteOutput ForInterceptor(string interceptor, GeneratedFile generated) =>
        new(interceptor, new EquatableArray<GeneratedFile>([generated]), null, default);

    public static CallSiteOutput ForDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        new(null, default, null, new EquatableArray<Diagnostic>([.. diagnostics]));
}

// A sequence argument needs the wrapper and the adapter for its elements.
// An adapter for a type only its containing type can name is nested in that type instead of going into the Files.
internal sealed record AdapterSet(string Name, ImmutableArray<GeneratedFile> Files, string Reference, OverloadMember? Nested = null);
