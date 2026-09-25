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
    public static string? Emit(INamedTypeSymbol target, ImmutableArray<MappedShapeOptions> options, ImmutableArray<Diagnostic>.Builder diagnostics,
        Location location)
    {
        var code = new StringBuilder();
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        // A member the user wrote by hand wins; deriving it again would declare it twice.
        foreach (var declared in target.GetMembers())
            emitted.Add(MappedShape.Signature(declared));

        var count = 0;
        foreach (var option in options)
        {
            if (option.CombinesPickAndOmit)
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

            var candidates = ShapeMatcher.GetAllMembers(source).Where(m => MappedShape.IsDerivable(m, option.IncludeMethods)).ToImmutableArray();

            if (FindUnknownName(option.Pick.Concat(option.Omit), candidates) is { } unknown)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMappedShape, location, target.Name,
                    source.ToDisplayString(), $"'{unknown}' is not a public instance member of the source"));
                continue;
            }

            foreach (var member in candidates.Where(m => option.Selects(m) && emitted.Add(MappedShape.Signature(m))))
            {
                if (LessAccessibleThan(member, target) is { } inaccessible)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMappedShape, location, target.Name,
                        source.ToDisplayString(), $"member '{member.Name}' has type '{inaccessible}', which is less accessible than the interface"));
                    continue;
                }

                EmitMember(code, member, option);
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

        return code.ToString();
    }

    private static void EmitMember(StringBuilder code, ISymbol member, MappedShapeOptions option)
    {
        switch (member)
        {
            case IPropertySymbol { IsIndexer: true } indexer:
                code.AppendLine($"{TypeName(indexer.Type, option)} this[{Parameters(indexer.Parameters)}] {{ {Accessors(indexer, option)}}}");
                break;
            case IPropertySymbol property:
                code.AppendLine($"{TypeName(property.Type, option)} {property.Name} {{ {Accessors(property, option)}}}");
                break;
            case IMethodSymbol method:
                code.AppendLine($"{TypeName(method.ReturnType, option, optionalPosition: false)} {method.Name}({Parameters(method.Parameters)});");
                break;
            case IEventSymbol @event:
                code.AppendLine($"event {FullyQualifiedName(@event.Type)} {@event.Name};");
                break;
        }
    }

    private static string Accessors(IPropertySymbol property, MappedShapeOptions option)
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
        string.Join(", ", parameters.Select(p => SourceSyntax.Parameter(p, FullyQualifiedName(p.Type)) + SourceSyntax.DefaultValue(p)));

    // 'Optional' is TypeScript's Partial<T>: a nullable member type still matches the source, because a
    // get-only property only has to be assignable and int -> int? is a built-in conversion.
    private static string TypeName(ITypeSymbol type, MappedShapeOptions option, bool optionalPosition = true)
    {
        var name = FullyQualifiedName(type);
        return option.Optional && optionalPosition && type.SpecialType != SpecialType.System_Void
            ? MappedShape.Nullable(name)
            : name;
    }

    private static string FullyQualifiedName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

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
}
