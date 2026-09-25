using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

/// <summary>
/// Fills a partial interface marked with <c>[DuckShape&lt;TSource&gt;]</c> with members derived from the
/// source type - the C# equivalent of TypeScript's <c>Pick</c>, <c>Omit</c>, <c>Partial</c> and <c>Readonly</c>.
/// Several attributes intersect, because every member of every source is derived into the same interface.
/// </summary>
internal static class MappedShapeEmitter
{
    internal sealed record Options(ITypeSymbol Source, ImmutableArray<string> Pick, ImmutableArray<string> Omit,
        bool Optional, bool Readonly, bool IncludeMethods);

    /// <summary>
    /// The members a <c>[DuckShape&lt;&gt;]</c> interface ends up with: the ones it declares itself, verbatim,
    /// plus the ones derived from the sources, which are the sources' own symbols and carry the mapping's
    /// transformations. A member's containing type tells the two apart.
    /// </summary>
    internal sealed record Info(INamedTypeSymbol Shape, ImmutableArray<ISymbol> Members, bool Optional, bool Readonly)
    {
        public bool IsDerived(ISymbol member) => !SymbolEqualityComparer.Default.Equals(member.ContainingType, Shape);
    }

    /// <summary>
    /// A generator cannot see another generator's output, so the interface symbol is still empty while call
    /// sites are analysed. Structural matching and the adapters therefore derive the same members from the
    /// attribute instead of reading them off the interface.
    /// </summary>
    public static Info? TryGetInfo(INamedTypeSymbol shape)
    {
        if (shape.TypeKind != TypeKind.Interface)
            return null;

        var attributes = shape.GetAttributes()
            .Where(a => KnownSymbols.IsDuckShapeAttribute(a.AttributeClass))
            .ToImmutableArray();
        if (attributes.IsEmpty)
            return null;

        var members = ImmutableArray.CreateBuilder<ISymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var optional = false;
        var isReadonly = false;

        // A member the interface declares itself wins over the derived one: its signature is the authoritative one.
        var declared = shape.GetMembers()
            .Concat(shape.AllInterfaces.SelectMany(i => i.GetMembers()))
            .Where(ShapeMatcher.IsRelevant)
            .Where(m => seen.Add(Signature(m)));
        members.AddRange(declared);

        foreach (var attribute in attributes)
        {
            if (ReadOptions(attribute) is not { } option || (!option.Pick.IsEmpty && !option.Omit.IsEmpty) ||
                option.Source is not INamedTypeSymbol { IsAnonymousType: false } source)
                continue;

            optional |= option.Optional;
            isReadonly |= option.Readonly;

            foreach (var member in ShapeMatcher.GetAllMembers(source).Where(m => IsDerivable(m, option.IncludeMethods)))
            {
                if (!option.Pick.IsEmpty && !option.Pick.Contains(member.Name, StringComparer.Ordinal))
                    continue;
                if (option.Omit.Contains(member.Name, StringComparer.Ordinal))
                    continue;
                if (seen.Add(Signature(member)))
                    members.Add(member);
            }
        }

        return members.Count == 0 ? null : new Info(shape, members.ToImmutable(), optional, isReadonly);
    }

    public static Options? ReadOptions(AttributeData attribute)
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

        return new Options(source, Names("Pick"), Names("Omit"), Flag("Optional"), Flag("Readonly"), Flag("IncludeMethods"));
    }

    /// <summary>The type a member carries in the generated interface, which the adapter has to repeat exactly.</summary>
    public static string MemberTypeName(ISymbol member, ITypeSymbol type, Info info) =>
        info.IsDerived(member) && info.Optional && IsOptionalPosition(member, type)
            ? Nullable(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            : type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>Whether the member keeps its setter in the generated interface.</summary>
    public static bool KeepsSetter(ISymbol member, Info info) => !(info.IsDerived(member) && info.Readonly);

    /// <summary>Whether the member's type widens under <c>Optional</c>: properties and indexers do, nothing else.</summary>
    public static bool IsOptionalPosition(ISymbol member, ITypeSymbol type) =>
        member is IPropertySymbol && SymbolEqualityComparer.Default.Equals(((IPropertySymbol)member).Type, type);

    /// <summary>The widened type of a member under <c>Optional</c>, or the type itself.</summary>
    public static ITypeSymbol OptionalType(ISymbol member, ITypeSymbol type, Info? info, Compilation compilation) =>
        info is { Optional: true } && info.IsDerived(member) && IsOptionalPosition(member, type) &&
        type.IsValueType && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
            ? compilation.GetSpecialType(SpecialType.System_Nullable_T).Construct(type)
            : type;

    private static string Nullable(string name) => name.EndsWith("?", StringComparison.Ordinal) ? name : name + "?";

    public static string? Emit(INamedTypeSymbol target, ImmutableArray<Options> options, ImmutableArray<Diagnostic>.Builder diagnostics,
        Location location)
    {
        var sb = new StringBuilder();
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        // A member the user wrote by hand wins; deriving it again would declare it twice.
        foreach (var declared in target.GetMembers())
            emitted.Add(Signature(declared));

        var count = 0;
        foreach (var option in options)
        {
            if (!option.Pick.IsEmpty && !option.Omit.IsEmpty)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMappedShape, location, target.Name,
                    option.Source.ToDisplayString(), "'Pick' and 'Omit' cannot be combined"));
                continue;
            }

            if (option.Source is not INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct or TypeKind.Interface } source ||
                source.IsAnonymousType)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMappedShape, location, target.Name,
                    option.Source.ToDisplayString(), "the source has to be a named class, struct, record or interface"));
                continue;
            }

            var candidates = ShapeMatcher.GetAllMembers(source).Where(m => IsDerivable(m, option.IncludeMethods)).ToImmutableArray();

            if (FindUnknownName(option.Pick.Concat(option.Omit), candidates) is { } unknown)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMappedShape, location, target.Name,
                    source.ToDisplayString(), $"'{unknown}' is not a public instance member of the source"));
                continue;
            }

            foreach (var member in candidates)
            {
                if (!option.Pick.IsEmpty && !option.Pick.Contains(member.Name, StringComparer.Ordinal))
                    continue;
                if (option.Omit.Contains(member.Name, StringComparer.Ordinal))
                    continue;
                if (!emitted.Add(Signature(member)))
                    continue;

                if (LessAccessibleThan(member, target) is { } inaccessible)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMappedShape, location, target.Name,
                        source.ToDisplayString(), $"member '{member.Name}' has type '{inaccessible}', which is less accessible than the interface"));
                    continue;
                }

                EmitMember(sb, member, option);
                count++;
            }
        }

        if (diagnostics.Count > 0)
            return null;

        if (count == 0 && !target.GetMembers().Any(ShapeMatcher.IsRelevant))
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMappedShape, location, target.Name,
                options[0].Source.ToDisplayString(), "no member was derived"));
            return null;
        }

        return sb.ToString();
    }

    /// <summary>Properties and fields are derived by default; methods, indexers and events only on request.</summary>
    private static bool IsDerivable(ISymbol member, bool includeMethods) =>
        member is { IsStatic: false, DeclaredAccessibility: Accessibility.Public } &&
        member switch
        {
            IPropertySymbol { IsIndexer: true } => includeMethods,
            IPropertySymbol => true,
            IMethodSymbol { MethodKind: MethodKind.Ordinary, IsGenericMethod: false } => includeMethods,
            IEventSymbol => includeMethods,
            _ => false,
        };

    private static void EmitMember(StringBuilder sb, ISymbol member, Options option)
    {
        switch (member)
        {
            case IPropertySymbol { IsIndexer: true } indexer:
                sb.AppendLine($"{TypeName(indexer.Type, option)} this[{Parameters(indexer.Parameters)}] {{ {Accessors(indexer, option)}}}");
                break;
            case IPropertySymbol property:
                sb.AppendLine($"{TypeName(property.Type, option)} {property.Name} {{ {Accessors(property, option)}}}");
                break;
            case IMethodSymbol method:
                sb.AppendLine($"{TypeName(method.ReturnType, option, optionalPosition: false)} {method.Name}({Parameters(method.Parameters)});");
                break;
            case IEventSymbol @event:
                sb.AppendLine($"event {FullName(@event.Type)} {@event.Name};");
                break;
        }
    }

    private static string Accessors(IPropertySymbol property, Options option)
    {
        var accessors = new StringBuilder();
        if (property.GetMethod is { DeclaredAccessibility: Accessibility.Public })
            accessors.Append("get; ");
        // An init-only setter can't be satisfied through an interface, so it is derived as read-only.
        if (!option.Readonly && property.SetMethod is { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false })
            accessors.Append("set; ");
        return accessors.Length == 0 ? "get; " : accessors.ToString();
    }

    private static string Parameters(ImmutableArray<IParameterSymbol> parameters) =>
        string.Join(", ", parameters.Select(p => Utilities.Parameter(p, FullName(p.Type)) + Utilities.DefaultValue(p)));

    // 'Optional' is TypeScript's Partial<T>: a nullable member type still matches the source, because a
    // get-only property only has to be assignable and int -> int? is a built-in conversion.
    private static string TypeName(ITypeSymbol type, Options option, bool optionalPosition = true)
    {
        var name = FullName(type);
        return option.Optional && optionalPosition && type.SpecialType != SpecialType.System_Void
            ? Nullable(name)
            : name;
    }

    private static string FullName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string? FindUnknownName(IEnumerable<string> names, ImmutableArray<ISymbol> candidates) =>
        names.FirstOrDefault(name => !candidates.Any(m => string.Equals(m.Name, name, StringComparison.Ordinal)));

    /// <summary>The type of a derived member has to be at least as visible as the interface carrying it.</summary>
    private static string? LessAccessibleThan(ISymbol member, INamedTypeSymbol target)
    {
        if (target.DeclaredAccessibility != Accessibility.Public)
            return null;

        var type = member switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            IMethodSymbol method => method.ReturnType,
            IEventSymbol @event => @event.Type,
            _ => null,
        };

        return type is INamedTypeSymbol { DeclaredAccessibility: not Accessibility.Public and not Accessibility.NotApplicable }
            ? type.ToDisplayString()
            : null;
    }

    private static string Signature(ISymbol member) => member switch
    {
        IMethodSymbol method => $"M:{method.Name}({string.Join(",", method.Parameters.Select(p => p.Type.ToDisplayString()))})",
        IPropertySymbol { IsIndexer: true } indexer => $"I:({string.Join(",", indexer.Parameters.Select(p => p.Type.ToDisplayString()))})",
        _ => $"P:{member.Name}",
    };
}
