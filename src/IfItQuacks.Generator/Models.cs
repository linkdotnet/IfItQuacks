using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

internal sealed record GeneratedFile(string Name, string Source);

// Prefix and suffix wrap the members in their containing namespace and types, so overloads from different call sites share one file.
internal sealed record OverloadMember(string FileName, string Prefix, string Suffix, string Member);

internal sealed record DuckTypedMethodOutput(string Name, DuckMethodRef Reference, GeneratedFile? Fallback, EquatableArray<Diagnostic> Diagnostics);

// An extension method call on a receiver that doesn't implement the interface has no symbol, so it is looked up by name instead.
internal sealed record DuckMethodRef(string Name, string ContainingType, bool IsExtension);

internal sealed record CallSiteOutput(
    string? Interceptor,
    EquatableArray<GeneratedFile> Adapters,
    OverloadMember? Overload,
    EquatableArray<Diagnostic> Diagnostics);

// A sequence argument needs the wrapper and the adapter for its elements.
internal sealed record AdapterSet(string Name, System.Collections.Immutable.ImmutableArray<GeneratedFile> Files);
