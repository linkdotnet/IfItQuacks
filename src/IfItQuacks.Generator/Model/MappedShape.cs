using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

/// <summary>
/// The members a <c>[DuckShape&lt;&gt;]</c> interface ends up with: the ones it declares itself, verbatim,
/// plus the ones derived from the sources, which are the sources' own symbols and carry the mapping's
/// transformations. A member's containing type tells the two apart.
/// </summary>
internal sealed record MappedShape(INamedTypeSymbol Shape, ImmutableArray<ISymbol> Members, bool Optional, bool Readonly)
{
    /// <summary>
    /// A generator cannot see another generator's output, so the interface symbol is still empty while call
    /// sites are analysed. Structural matching and the adapters therefore derive the same members from the
    /// attribute instead of reading them off the interface.
    /// </summary>
    public static MappedShape? TryGet(INamedTypeSymbol shape)
    {
        if (shape.TypeKind != TypeKind.Interface)
            return null;

        var attributes = shape.GetAttributes().Where(a => KnownSymbols.IsDuckShapeAttribute(a.AttributeClass)).ToImmutableArray();
        if (attributes.IsEmpty)
            return null;

        var members = ImmutableArray.CreateBuilder<ISymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var optional = false;
        var isReadonly = false;

        // A member the interface declares itself wins over the derived one: its signature is the authoritative one.
        members.AddRange(shape.GetMembers()
            .Concat(shape.AllInterfaces.SelectMany(i => i.GetMembers()))
            .Where(ShapeMatcher.IsRelevant)
            .Where(m => seen.Add(Signature(m))));

        foreach (var attribute in attributes)
        {
            if (MappedShapeOptions.Read(attribute) is not { CombinesPickAndOmit: false } option ||
                option.Source is not INamedTypeSymbol { IsAnonymousType: false } source)
                continue;

            optional |= option.Optional;
            isReadonly |= option.Readonly;
            members.AddRange(ShapeMatcher.GetAllMembers(source).Where(m => IsDerivable(m, option.IncludeMethods) && option.Selects(m) && seen.Add(Signature(m))));
        }

        return members.Count == 0 ? null : new MappedShape(shape, members.ToImmutable(), optional, isReadonly);
    }

    public bool IsDerived(ISymbol member) => !SymbolEqualityComparer.Default.Equals(member.ContainingType, Shape);

    /// <summary>The type a member carries in the generated interface, which the adapter has to repeat exactly.</summary>
    public string MemberTypeName(ISymbol member, ITypeSymbol type) =>
        IsDerived(member) && Optional && IsOptionalPosition(member, type)
            ? Nullable(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            : type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    public bool KeepsSetter(ISymbol member) => !(IsDerived(member) && Readonly);

    public static ITypeSymbol OptionalType(ISymbol member, ITypeSymbol type, MappedShape? mapped, Compilation compilation) =>
        mapped is { Optional: true } && mapped.IsDerived(member) && IsOptionalPosition(member, type) &&
        type.IsValueType && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
            ? compilation.GetSpecialType(SpecialType.System_Nullable_T).Construct(type)
            : type;

    public static bool IsDerivable(ISymbol member, bool includeMethods) =>
        member is { IsStatic: false, DeclaredAccessibility: Accessibility.Public } &&
        member switch
        {
            IPropertySymbol { IsIndexer: true } => includeMethods,
            IPropertySymbol => true,
            IMethodSymbol { MethodKind: MethodKind.Ordinary, IsGenericMethod: false } => includeMethods,
            IEventSymbol => includeMethods,
            _ => false,
        };

    public static string Signature(ISymbol member) => member switch
    {
        IMethodSymbol method => $"M:{method.Name}({string.Join(",", method.Parameters.Select(p => p.Type.ToDisplayString()))})",
        IPropertySymbol { IsIndexer: true } indexer => $"I:({string.Join(",", indexer.Parameters.Select(p => p.Type.ToDisplayString()))})",
        _ => $"P:{member.Name}",
    };

    public static string Nullable(string typeName) => typeName.EndsWith("?", StringComparison.Ordinal) ? typeName : typeName + "?";

    private static bool IsOptionalPosition(ISymbol member, ITypeSymbol type) =>
        member is IPropertySymbol property && SymbolEqualityComparer.Default.Equals(property.Type, type);
}
