using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IfItQuacks.Generator;

// These hold symbols and syntax of one compilation, so they stay inside a transform and never become pipeline outputs.

internal sealed record DuckArgument(IParameterSymbol Parameter, ExpressionSyntax Expression, ITypeSymbol ConcreteType);

internal sealed record ResolvedArgument(ITypeSymbol ConcreteType, string? AdapterReference);

internal sealed record TypeParameterBinding(INamedTypeSymbol ConcreteType, string? AdapterReference);

/// <summary>Collects the adapters one call site needs until they become its <see cref="CallSiteOutput"/>.</summary>
internal sealed class GeneratedAdapters
{
    private readonly ImmutableArray<GeneratedFile>.Builder _files = ImmutableArray.CreateBuilder<GeneratedFile>();
    private readonly ImmutableArray<OverloadMember>.Builder _nested = ImmutableArray.CreateBuilder<OverloadMember>();

    public bool IsEmpty => _files.Count == 0;

    public string Add(AdapterSet adapter)
    {
        _files.AddRange(adapter.Files);
        if (adapter.Nested is not null)
            _nested.Add(adapter.Nested);
        return adapter.Reference;
    }

    public void Add(GeneratedFile file) => _files.Add(file);

    public CallSiteOutput WithInterceptor(string interceptor) =>
        new(interceptor, new EquatableArray<GeneratedFile>(_files.ToImmutable()), null, default, new EquatableArray<OverloadMember>(_nested.ToImmutable()));

    public CallSiteOutput WithOverload(OverloadMember overload) =>
        new(null, new EquatableArray<GeneratedFile>(_files.ToImmutable()), overload, default, new EquatableArray<OverloadMember>(_nested.ToImmutable()));
}
