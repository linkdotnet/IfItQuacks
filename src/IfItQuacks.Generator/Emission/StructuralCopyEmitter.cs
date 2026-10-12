using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

/// <summary>
/// Builds a value of the target type from the members of a source that has the same shape. Unlike an adapter this
/// copies: it reads every member once and forwards to nothing afterwards. Members match by name and assignable type;
/// a member of another user-defined type is built the same way, and a collection is copied element by element.
/// </summary>
internal static class StructuralCopyEmitter
{
    public static string GetFactoryName(INamedTypeSymbol target, INamedTypeSymbol source) =>
        source.IsAnonymousType
            ? $"CopyFactory_{GeneratedCode.ToIdentifier(target.ToDisplayString())}_Anonymous_{GeneratedCode.ToIdentifier(AnonymousWitness(source))}"
            : $"CopyFactory_{GeneratedCode.ToIdentifier(target.ToDisplayString())}_{GeneratedCode.ToIdentifier(source.ToDisplayString())}";

    /// <summary>The reason <paramref name="target"/> cannot be built from <paramref name="source"/>, or <c>null</c>.</summary>
    public static string? FindMismatch(INamedTypeSymbol target, INamedTypeSymbol source, Compilation compilation) =>
        FindMismatch(target, source, new Matching(compilation));

    /// <summary>The factory for <paramref name="target"/> first, then one for every nested type it builds.</summary>
    public static ImmutableArray<GeneratedFile> Emit(INamedTypeSymbol target, INamedTypeSymbol source, Compilation compilation)
    {
        var files = ImmutableArray.CreateBuilder<GeneratedFile>();
        var pending = new Queue<(INamedTypeSymbol Target, INamedTypeSymbol Source)>([(target, source)]);
        var emitted = new HashSet<string>(StringComparer.Ordinal) { GetFactoryName(target, source) };
        var matching = new Matching(compilation);

        while (pending.Count > 0)
        {
            var (nextTarget, nextSource) = pending.Dequeue();
            var factoryName = GetFactoryName(nextTarget, nextSource);
            files.Add(new GeneratedFile(factoryName, EmitFactory(nextTarget, nextSource, factoryName, matching, nested =>
            {
                if (emitted.Add(GetFactoryName(nested.Target, nested.Source)))
                    pending.Enqueue(nested);
            })));
        }

        return files.ToImmutable();
    }

    private static string? FindMismatch(INamedTypeSymbol target, INamedTypeSymbol source, Matching matching)
    {
        if (target.TypeKind == TypeKind.Interface)
            return "the target is an interface - use 'Duck.As' to view a value as one";

        if (target is { IsAbstract: true } or { TypeKind: TypeKind.Delegate } or { IsRefLikeType: true })
            return "the target has to be a concrete class, struct or record";

        if (!IsReachable(target))
            return "the target type is not accessible from generated code";

        // A type that (indirectly) contains itself is assumed to fit while it is being checked, so a recursive type ends.
        if (!matching.Enter(target, source))
            return null;

        try
        {
            if (FindLargestFillableConstructor(target, source, matching) is not { } constructor)
            {
                var largest = AccessibleConstructors(target).FirstOrDefault();
                var unfilled = largest?.Parameters.FirstOrDefault(p => p.RefKind != RefKind.None || FindSourceMember(source, p.Name, p.Type, matching) is null);
                return unfilled is null
                    ? "no accessible constructor can be filled from the source"
                    : $"no accessible constructor can be filled from the source: {DescribeUnfilled(source, unfilled.Name, unfilled.Type, matching)}";
            }

            var set = constructor.Parameters.Select(p => p.Name).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
            var unmatched = InitializableMembers(target)
                .Where(m => !set.Contains(m.Name))
                .FirstOrDefault(m => FindSourceMember(source, m.Name, MemberType(m), matching) is null);

            return unmatched is null ? null : DescribeUnfilled(source, unmatched.Name, MemberType(unmatched), matching);
        }
        finally
        {
            matching.Leave(target, source);
        }
    }

    private static string DescribeUnfilled(INamedTypeSymbol source, string name, ITypeSymbol targetType, Matching matching)
    {
        var sameName = ReadableMembers(source).FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        return sameName is not null && FindConversionMismatch(MemberType(sameName), targetType, matching) is { } nested
            ? $"'{name}' of type '{targetType.ToDisplayString()}' can't be built from '{MemberType(sameName).ToDisplayString()}': {nested}"
            : $"no member of the source fills '{name}' of type '{targetType.ToDisplayString()}'";
    }

    // Only a mismatch that building or copying could have bridged gets a reason; anything else just doesn't convert.
    private static string? FindConversionMismatch(ITypeSymbol from, ITypeSymbol to, Matching matching)
    {
        if (ShapeMatcher.IsAssignable(from, to, matching.Compilation))
            return null;

        if (EnumPair.TryCreate(from, to) is { } enums)
            return enums.FindMissingMember();

        if (TargetDictionary(to) is { } targetEntry && SourceEntry(from) is { } sourceEntry)
        {
            if (!ShapeMatcher.IsAssignable(sourceEntry.Key, targetEntry.Key, matching.Compilation))
                return $"its keys of type '{sourceEntry.Key.ToDisplayString()}' are not assignable to '{targetEntry.Key.ToDisplayString()}'";
            return FindConversionMismatch(sourceEntry.Value, targetEntry.Value, matching) is { } valueMismatch ? $"its values don't fit: {valueMismatch}" : null;
        }

        if (TargetElement(to) is { } targetElement && SourceElement(from) is { } sourceElement)
            return FindConversionMismatch(sourceElement.Element, targetElement.Element, matching) is { } elementMismatch ? $"its elements don't fit: {elementMismatch}" : null;

        return IsBuildable(from, to)
            ? FindMismatch((INamedTypeSymbol)to.WithoutNullability(), (INamedTypeSymbol)from.WithoutNullability(), matching)
            : $"'{from.ToDisplayString()}' is not assignable to '{to.ToDisplayString()}'";
    }

    private static string EmitFactory(INamedTypeSymbol target, INamedTypeSymbol source, string factoryName, Matching matching,
        Action<(INamedTypeSymbol Target, INamedTypeSymbol Source)> requireFactory)
    {
        var constructor = FindLargestFillableConstructor(target, source, matching)!;
        var receiver = source.IsAnonymousType ? "source" : "value";
        var set = constructor.Parameters.Select(p => p.Name).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var conversion = new Conversion(matching.Compilation, requireFactory);

        string Read(string name, ITypeSymbol targetType)
        {
            var member = FindSourceMember(source, name, targetType, matching)!;
            return conversion.Convert($"{receiver}.{member.Name}", MemberType(member), targetType);
        }

        var arguments = string.Join(", ", constructor.Parameters.Select(p => Read(p.Name, p.Type)));
        var assignments = InitializableMembers(target)
            .Where(m => !set.Contains(m.Name))
            .Select(m => $"{m.Name} = {Read(m.Name, MemberType(m))}")
            .ToList();

        var initializer = assignments.Count == 0 ? "" : " { " + string.Join(", ", assignments) + " }";
        var creation = $"new {target.ToDisplayString()}({arguments}){initializer}";

        var code = new StringBuilder();
        code.AppendLine($"    internal static class {factoryName}");
        code.AppendLine("    {");
        if (source.IsAnonymousType)
        {
            // An anonymous type can't be named, but an identical anonymous object expression in the same compilation has the same type.
            code.AppendLine($"        public static {target.ToDisplayString()} Create(object value)");
            code.AppendLine("        {");
            code.AppendLine($"            var source = {GeneratedCode.CastByExample("value", AnonymousWitness(source))};");
            code.AppendLine($"            return {creation};");
            code.AppendLine("        }");
            code.AppendLine("        " + GeneratedCode.CastByExampleDeclaration);
        }
        else
        {
            code.AppendLine($"        public static {target.ToDisplayString()} Create({source.ToDisplayString()} value) => {creation};");
        }
        code.AppendLine("    }");
        return code.ToString();
    }

    /// <summary>Writes the expression converting a source member to the target's member type, numbering the variables it declares.</summary>
    private sealed class Conversion(Compilation compilation, Action<(INamedTypeSymbol Target, INamedTypeSymbol Source)> requireFactory)
    {
        private int _variables;

        public string Convert(string expression, ITypeSymbol from, ITypeSymbol to)
        {
            if (ShapeMatcher.IsAssignable(from, to, compilation))
                return expression;

            // A null nested object, collection or enum stays null rather than failing the whole copy.
            var value = $"__v{_variables++}";
            if (EnumPair.TryCreate(from, to) is { } enums)
            {
                return enums.FromNullable
                    ? $"({expression} is {{ }} {value} ? ({to.ToDisplayString()})({enums.Convert(value, $"__v{_variables++}")}) : null)"
                    : enums.Convert(expression, value);
            }

            if (TargetDictionary(to) is { } targetEntry && SourceEntry(from) is { } sourceEntry)
            {
                var entry = $"__v{_variables++}";
                var entryType = $"global::System.Collections.Generic.KeyValuePair<{sourceEntry.Key.ToDisplayString()}, {sourceEntry.Value.ToDisplayString()}>";
                // A dictionary copied with the same key type keeps its comparer, e.g. one ignoring case.
                var comparer = from.WithoutNullability() is INamedTypeSymbol { Name: "Dictionary" } dictionary && IsGenericCollection(dictionary) &&
                               SymbolEqualityComparer.Default.Equals(sourceEntry.Key, targetEntry.Key)
                    ? $", {value}.Comparer"
                    : "";
                return $"({expression} is {{ }} {value} ? global::System.Linq.Enumerable.ToDictionary<{entryType}, {targetEntry.Key.ToDisplayString()}, {targetEntry.Value.ToDisplayString()}>(" +
                       $"{value}, {entry} => {entry}.Key, {entry} => {Convert($"{entry}.Value", sourceEntry.Value, targetEntry.Value)}{comparer}) : default!)";
            }

            if (TargetElement(to) is { } targetElement && SourceElement(from) is { } sourceElement)
            {
                var item = $"__v{_variables++}";
                var elementType = targetElement.Element.ToDisplayString();
                var select = $"global::System.Linq.Enumerable.Select<{sourceElement.Element.ToDisplayString()}, {elementType}>({value}, " +
                             $"{item} => {Convert(item, sourceElement.Element, targetElement.Element)})";
                var copy = targetElement.Kind switch
                {
                    CollectionKind.Array => $"global::System.Linq.Enumerable.ToArray({select})",
                    CollectionKind.Set => $"new global::System.Collections.Generic.HashSet<{elementType}>({select})",
                    _ => $"global::System.Linq.Enumerable.ToList({select})",
                };
                return $"({expression} is {{ }} {value} ? {copy} : default!)";
            }

            var nested = ((INamedTypeSymbol)to.WithoutNullability(), (INamedTypeSymbol)from.WithoutNullability());
            requireFactory(nested);
            return $"({expression} is {{ }} {value} ? global::{GeneratedCode.Namespace}.{GetFactoryName(nested.Item1, nested.Item2)}.Create({value}) : default!)";
        }
    }

    /// <summary>The pairs being matched right now, which a recursive type reaches again.</summary>
    private sealed class Matching(Compilation compilation)
    {
        private readonly HashSet<(INamedTypeSymbol, INamedTypeSymbol)> _inProgress = new(PairComparer.Instance);

        public Compilation Compilation { get; } = compilation;

        public bool Enter(INamedTypeSymbol target, INamedTypeSymbol source) => _inProgress.Add((target, source));

        public void Leave(INamedTypeSymbol target, INamedTypeSymbol source) => _inProgress.Remove((target, source));
    }

    private sealed class PairComparer : IEqualityComparer<(INamedTypeSymbol, INamedTypeSymbol)>
    {
        public static readonly PairComparer Instance = new();

        public bool Equals((INamedTypeSymbol, INamedTypeSymbol) x, (INamedTypeSymbol, INamedTypeSymbol) y) =>
            SymbolEqualityComparer.Default.Equals(x.Item1, y.Item1) && SymbolEqualityComparer.Default.Equals(x.Item2, y.Item2);

        public int GetHashCode((INamedTypeSymbol, INamedTypeSymbol) obj) =>
            unchecked((SymbolEqualityComparer.Default.GetHashCode(obj.Item1) * 31) + SymbolEqualityComparer.Default.GetHashCode(obj.Item2));
    }

    // Framework types aren't built member by member: a System.Version from any type with Major and Minor would be a surprise, not a copy.
    private static bool IsBuildable(ITypeSymbol from, ITypeSymbol to) =>
        from.WithoutNullability() is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct, SpecialType: SpecialType.None, IsAnonymousType: false } source &&
        to.WithoutNullability() is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct, SpecialType: SpecialType.None } target &&
        source.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T && target.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T &&
        !IsFrameworkType(source) && !IsFrameworkType(target) && TypeVisibility.IsNameable(source);

    private static bool IsFrameworkType(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace;
        while (ns is { ContainingNamespace.IsGlobalNamespace: false })
            ns = ns.ContainingNamespace;
        return ns is { Name: "System" or "Microsoft" };
    }

    private enum CollectionKind
    {
        List,
        Array,
        Set,
    }

    private static (ITypeSymbol Element, CollectionKind Kind)? TargetElement(ITypeSymbol type) => type.WithoutNullability() switch
    {
        IArrayTypeSymbol { Rank: 1 } array => (array.ElementType, CollectionKind.Array),
        INamedTypeSymbol { TypeArguments.Length: 1 } named when IsGenericCollection(named) => named.Name switch
        {
            "List" or "IList" or "ICollection" or "IEnumerable" or "IReadOnlyList" or "IReadOnlyCollection" => (named.TypeArguments[0], CollectionKind.List),
            "HashSet" or "ISet" or "IReadOnlySet" => (named.TypeArguments[0], CollectionKind.Set),
            _ => null,
        },
        _ => null,
    };

    private static (ITypeSymbol Element, CollectionKind Kind)? SourceElement(ITypeSymbol type) => type.WithoutNullability() switch
    {
        { SpecialType: SpecialType.System_String } => null,
        IArrayTypeSymbol { Rank: 1 } array => (array.ElementType, CollectionKind.Array),
        INamedTypeSymbol named => named.AllInterfaces.Prepend(named)
            .FirstOrDefault(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)?.TypeArguments[0] is { } element
            ? (element, CollectionKind.List)
            : null,
        _ => null,
    };

    private static (ITypeSymbol Key, ITypeSymbol Value)? TargetDictionary(ITypeSymbol type) =>
        type.WithoutNullability() is INamedTypeSymbol { Name: "Dictionary" or "IDictionary" or "IReadOnlyDictionary", TypeArguments.Length: 2 } named && IsGenericCollection(named)
            ? (named.TypeArguments[0], named.TypeArguments[1])
            : null;

    private static (ITypeSymbol Key, ITypeSymbol Value)? SourceEntry(ITypeSymbol type) =>
        SourceElement(type)?.Element is INamedTypeSymbol { Name: "KeyValuePair", TypeArguments.Length: 2 } pair && IsGenericCollection(pair)
            ? (pair.TypeArguments[0], pair.TypeArguments[1])
            : null;

    private static bool IsGenericCollection(INamedTypeSymbol type) =>
        type.ContainingNamespace is { Name: "Generic", ContainingNamespace: { Name: "Collections", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } };

    /// <summary>
    /// Two enum types, converted by member name. A [Flags] enum combines its members, so it is converted by value instead,
    /// which every member name has to agree on.
    /// </summary>
    private sealed record EnumPair(INamedTypeSymbol From, INamedTypeSymbol To, bool FromNullable, bool ToNullable)
    {
        public static EnumPair? TryCreate(ITypeSymbol from, ITypeSymbol to) =>
            Underlying(from) is (INamedTypeSymbol { TypeKind: TypeKind.Enum } fromEnum, var fromNullable) &&
            Underlying(to) is (INamedTypeSymbol { TypeKind: TypeKind.Enum } toEnum, var toNullable) &&
            !SymbolEqualityComparer.Default.Equals(fromEnum, toEnum)
                ? new EnumPair(fromEnum, toEnum, fromNullable, toNullable)
                : null;

        private bool IsFlags => From.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.FlagsAttribute");

        public string? FindMissingMember()
        {
            if (FromNullable && !ToNullable)
                return $"a null '{From.ToDisplayString()}?' has no '{To.ToDisplayString()}' value";

            var targets = Members(To).GroupBy(m => m.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var missing = Members(From).FirstOrDefault(m => !targets.TryGetValue(m.Name, out var target) ||
                                                            (IsFlags && !Equals(Number(m), Number(target))));
            if (missing is null)
                return null;

            return IsFlags
                ? $"'{To.ToDisplayString()}' has no member '{missing.Name}' with the value {missing.ConstantValue} of the [Flags] enum '{From.ToDisplayString()}'"
                : $"'{To.ToDisplayString()}' has no member '{missing.Name}'";
        }

        public string Convert(string value, string unnamed)
        {
            var to = To.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (IsFlags)
                return $"(({to}){value})";

            var from = From.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            // Members sharing a value are one case; the first name decides.
            var arms = Members(From).GroupBy(Number).Select(g => g.First())
                .Select(m => $"{from}.{SourceSyntax.Identifier(m.Name)} => {to}.{SourceSyntax.Identifier(m.Name)}, ");
            return $"({value} switch {{ {string.Concat(arms)}var {unnamed} => throw new global::System.ArgumentOutOfRangeException(null, {unnamed}, " +
                   $"\"The value has no member name, so it has no counterpart in '{To.ToDisplayString()}'.\") }})";
        }

        private static IEnumerable<IFieldSymbol> Members(INamedTypeSymbol type) => type.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue);

        private static decimal Number(IFieldSymbol member) => System.Convert.ToDecimal(member.ConstantValue, System.Globalization.CultureInfo.InvariantCulture);

        private static (ITypeSymbol Type, bool Nullable) Underlying(ITypeSymbol type) =>
            type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? (nullable.TypeArguments[0], true) : (type, false);
    }

    private static IMethodSymbol? FindLargestFillableConstructor(INamedTypeSymbol target, INamedTypeSymbol source, Matching matching) =>
        AccessibleConstructors(target).FirstOrDefault(c => c.Parameters.All(p =>
            p.RefKind == RefKind.None && FindSourceMember(source, p.Name, p.Type, matching) is not null));

    private static IEnumerable<IMethodSymbol> AccessibleConstructors(INamedTypeSymbol target) =>
        target.InstanceConstructors
            .Where(c => c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal && !c.IsVararg)
            .OrderByDescending(c => c.Parameters.Length);

    private static IEnumerable<ISymbol> InitializableMembers(INamedTypeSymbol target) =>
        ShapeMatcher.GetAllMembers(target).Where(m => m switch
        {
            IPropertySymbol { IsIndexer: false, IsStatic: false, DeclaredAccessibility: Accessibility.Public } property =>
                property.SetMethod is { DeclaredAccessibility: Accessibility.Public },
            IFieldSymbol { IsStatic: false, IsReadOnly: false, IsConst: false, DeclaredAccessibility: Accessibility.Public } field =>
                !field.IsImplicitlyDeclared,
            _ => false,
        });

    private static ISymbol? FindSourceMember(INamedTypeSymbol source, string name, ITypeSymbol targetType, Matching matching) =>
        ReadableMembers(source).FirstOrDefault(m =>
            string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) && FindConversionMismatch(MemberType(m), targetType, matching) is null);

    private static IEnumerable<ISymbol> ReadableMembers(INamedTypeSymbol source) =>
        ShapeMatcher.GetAllMembers(source).Where(m => ShapeMatcher.IsPublicInstance(m) && m switch
        {
            IPropertySymbol { IsIndexer: false, GetMethod.DeclaredAccessibility: Accessibility.Public } => true,
            IFieldSymbol => true,
            _ => false,
        });

    private static ITypeSymbol MemberType(ISymbol member) =>
        member is IPropertySymbol property ? property.Type : ((IFieldSymbol)member).Type;

    private static bool IsReachable(INamedTypeSymbol type) =>
        type.EnclosingTypes().All(t => t.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal);

    private static string AnonymousWitness(INamedTypeSymbol anonymousType) =>
        "new { " + string.Join(", ", anonymousType.GetMembers().OfType<IPropertySymbol>().Select(p =>
            $"{SourceSyntax.Identifier(p.Name)} = default({p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})!")) + " }";
}
