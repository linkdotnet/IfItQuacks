using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

/// <summary>The arguments of one <c>[DuckShape&lt;TSource&gt;]</c> attribute.</summary>
internal sealed record MappedShapeOptions(ITypeSymbol Source, ImmutableArray<string> Pick, ImmutableArray<string> Omit,
    bool Optional, bool Readonly, bool IncludeMethods)
{
    public bool CombinesPickAndOmit => !Pick.IsEmpty && !Omit.IsEmpty;

    public static MappedShapeOptions? Read(AttributeData attribute)
    {
        // netstandard2.0 has no System.Index, so no list pattern here.
        if (attribute.AttributeClass is not { TypeArguments.Length: 1 } attributeClass ||
            attributeClass.TypeArguments[0] is not INamedTypeSymbol source)
            return null;

        ImmutableArray<string> Names(string name) =>
            attribute.NamedArguments.FirstOrDefault(a => a.Key == name).Value is { Kind: TypedConstantKind.Array } array
                ? [.. array.Values.Select(v => v.Value as string).Where(v => v is not null).Select(v => v!)]
                : [];

        bool Flag(string name) => attribute.NamedArguments.FirstOrDefault(a => a.Key == name).Value.Value is true;

        return new MappedShapeOptions(source, Names("Pick"), Names("Omit"), Flag("Optional"), Flag("Readonly"), Flag("IncludeMethods"));
    }

    public bool Selects(ISymbol member) =>
        (Pick.IsEmpty || Pick.Contains(member.Name, StringComparer.Ordinal)) && !Omit.Contains(member.Name, StringComparer.Ordinal);
}
