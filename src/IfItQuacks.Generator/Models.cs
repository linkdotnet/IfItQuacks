using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

internal sealed record GeneratedFile(string Name, string Source);

// Prefix and suffix wrap the members in their containing namespace and types, so overloads from different call sites share one file.
internal sealed record OverloadMember(string FileName, string Prefix, string Suffix, string Member);

internal sealed record DuckTypedMethodOutput(string Name, GeneratedFile? Fallback, EquatableArray<Diagnostic> Diagnostics);

internal sealed record CallSiteOutput(
    string? Interceptor,
    EquatableArray<GeneratedFile> Adapters,
    OverloadMember? Overload,
    EquatableArray<Diagnostic> Diagnostics);
