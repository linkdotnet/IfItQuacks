using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

/// <summary>The valid <c>[DuckTyped]</c> methods of a compilation, so call sites neither re-validate them nor bind calls to other methods.</summary>
internal sealed class DuckTypedMethodIndex : IEquatable<DuckTypedMethodIndex>
{
    private readonly EquatableArray<DuckMethodRef> _methods;
    private readonly HashSet<string> _names;
    private readonly HashSet<(string ContainingType, string Name)> _keys;

    public DuckTypedMethodIndex(ImmutableArray<DuckMethodRef> methods)
    {
        var ordered = methods.Distinct()
            .OrderBy(m => m.ContainingType, StringComparer.Ordinal)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToImmutableArray();
        _methods = new EquatableArray<DuckMethodRef>(ordered);
        _names = new HashSet<string>(ordered.Select(m => m.Name), StringComparer.Ordinal);
        _keys = [.. ordered.Select(m => (m.ContainingType, m.Name))];
    }

    public IEnumerable<DuckMethodRef> Extensions => _methods.Where(m => m.IsExtension);

    public bool ContainsName(string name) => _names.Contains(name);

    public bool Contains(IMethodSymbol method) => _keys.Contains((DuckMethodRef.MetadataName(method.ContainingType), method.Name));

    public bool Equals(DuckTypedMethodIndex? other) => other is not null && _methods.Equals(other._methods);

    public override bool Equals(object? obj) => Equals(obj as DuckTypedMethodIndex);

    public override int GetHashCode() => _methods.GetHashCode();
}
