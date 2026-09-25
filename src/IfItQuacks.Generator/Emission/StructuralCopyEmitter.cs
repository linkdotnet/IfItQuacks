using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

/// <summary>
/// Builds a value of the target type from the members of a source that has the same shape. Unlike an adapter this
/// copies: it reads every member once and forwards to nothing afterwards. Matching stays as strict as everywhere
/// else - name for name, assignable types, no renaming and no nested projection.
/// </summary>
internal static class StructuralCopyEmitter
{

    public static string GetFactoryName(INamedTypeSymbol target, INamedTypeSymbol source) =>
        source.IsAnonymousType
            ? $"CopyFactory_{GeneratedCode.ToIdentifier(target.ToDisplayString())}_Anonymous_{GeneratedCode.ToIdentifier(AnonymousWitness(source))}"
            : $"CopyFactory_{GeneratedCode.ToIdentifier(target.ToDisplayString())}_{GeneratedCode.ToIdentifier(source.ToDisplayString())}";

    /// <summary>The reason <paramref name="target"/> cannot be built from <paramref name="source"/>, or <c>null</c>.</summary>
    public static string? FindMismatch(INamedTypeSymbol target, INamedTypeSymbol source, Compilation compilation)
    {
        if (target.TypeKind == TypeKind.Interface)
            return "the target is an interface - use 'Duck.As' to view a value as one";

        if (target is { IsAbstract: true } or { TypeKind: TypeKind.Delegate } or { IsRefLikeType: true })
            return "the target has to be a concrete class, struct or record";

        if (!IsReachable(target))
            return "the target type is not accessible from generated code";

        if (FindLargestFillableConstructor(target, source, compilation) is not { } constructor)
            return "no accessible constructor can be filled from the source";

        var set = constructor.Parameters.Select(p => p.Name).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var unmatched = InitializableMembers(target)
            .Where(m => !set.Contains(m.Name))
            .FirstOrDefault(m => FindSourceMember(source, m.Name, MemberType(m), compilation) is null);

        return unmatched is null
            ? null
            : $"no member of the source fills '{unmatched.Name}' of type '{MemberType(unmatched).ToDisplayString()}'";
    }

    public static string Emit(INamedTypeSymbol target, INamedTypeSymbol source, string factoryName, Compilation compilation)
    {
        var constructor = FindLargestFillableConstructor(target, source, compilation)!;
        var receiver = source.IsAnonymousType ? "source" : "value";
        var set = constructor.Parameters.Select(p => p.Name).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        var arguments = string.Join(", ", constructor.Parameters.Select(p =>
            $"{receiver}.{FindSourceMember(source, p.Name, p.Type, compilation)!.Name}"));

        var assignments = InitializableMembers(target)
            .Where(m => !set.Contains(m.Name))
            .Select(m => $"{m.Name} = {receiver}.{FindSourceMember(source, m.Name, MemberType(m), compilation)!.Name}")
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

    private static IMethodSymbol? FindLargestFillableConstructor(INamedTypeSymbol target, INamedTypeSymbol source, Compilation compilation) =>
        target.InstanceConstructors
            .Where(c => c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal && !c.IsVararg)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault(c => c.Parameters.All(p =>
                p.RefKind == RefKind.None && FindSourceMember(source, p.Name, p.Type, compilation) is not null));

    private static IEnumerable<ISymbol> InitializableMembers(INamedTypeSymbol target) =>
        ShapeMatcher.GetAllMembers(target).Where(m => m switch
        {
            IPropertySymbol { IsIndexer: false, IsStatic: false, DeclaredAccessibility: Accessibility.Public } property =>
                property.SetMethod is { DeclaredAccessibility: Accessibility.Public },
            IFieldSymbol { IsStatic: false, IsReadOnly: false, IsConst: false, DeclaredAccessibility: Accessibility.Public } field =>
                !field.IsImplicitlyDeclared,
            _ => false,
        });

    private static ISymbol? FindSourceMember(INamedTypeSymbol source, string name, ITypeSymbol targetType, Compilation compilation) =>
        ShapeMatcher.GetAllMembers(source).FirstOrDefault(m =>
            string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) &&
            ShapeMatcher.IsPublicInstance(m) &&
            m switch
            {
                IPropertySymbol { IsIndexer: false, GetMethod.DeclaredAccessibility: Accessibility.Public } property =>
                    ShapeMatcher.IsAssignable(property.Type, targetType, compilation),
                IFieldSymbol field => ShapeMatcher.IsAssignable(field.Type, targetType, compilation),
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
