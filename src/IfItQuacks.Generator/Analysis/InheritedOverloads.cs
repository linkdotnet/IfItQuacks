using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace IfItQuacks.Generator;

/// <summary>
/// Overloads a non-generic <c>[DuckTyped]</c> method inherits. Overload resolution drops a base type's methods once a method of the
/// derived type applies, whatever its priority, so the generated fallback would take their calls. Redeclaring each of them next to
/// the fallback keeps them in the running, where the fallback's lower priority lets them win as declared overloads do.
/// </summary>
internal static class InheritedOverloads
{
    internal sealed record Result(ImmutableArray<IMethodSymbol> Forwarded, IMethodSymbol? Unsupported);

    public static Result Find(IMethodSymbol method, Compilation compilation)
    {
        var containingType = method.ContainingType;
        var declared = UserDeclaredMethods(containingType, method.Name).ToDictionary(Signature, m => m);
        var seen = new HashSet<string>(declared.Keys, StringComparer.Ordinal);
        var forwarded = ImmutableArray.CreateBuilder<IMethodSymbol>();
        IMethodSymbol? unsupported = null;

        for (var type = containingType.BaseType; type is not null && !method.IsGenericMethod; type = type.BaseType)
        {
            foreach (var inherited in UserDeclaredMethods(type, method.Name))
            {
                var signature = Signature(inherited);
                var hidden = !seen.Add(signature);
                if (inherited.MethodKind != MethodKind.Ordinary || !compilation.IsSymbolAccessibleWithin(inherited, containingType) || method.Overrides(inherited))
                    continue;

                // An override declared next to the [DuckTyped] method still counts as the base type's method, so it can't be redeclared.
                if (hidden && !(declared.TryGetValue(signature, out var own) && own.IsOverride))
                    continue;

                if (hidden || method.IsOverride || containingType.IsValueType || !CanForward(inherited))
                    unsupported ??= inherited;
                else
                    forwarded.Add(inherited);
            }
        }

        return new Result(forwarded.ToImmutable(), unsupported);
    }

    public static string EmitForwarder(IMethodSymbol inherited, INamedTypeSymbol containingType, Compilation compilation)
    {
        string Attributes(IEnumerable<AttributeData> attributes, string target = "") =>
            string.Concat(attributes.Where(a => IsCopied(a, containingType, compilation)).Select(a => $"[{target}{Render(a)}] "));

        var parameters = string.Join(", ", inherited.Parameters.Select(p =>
            Attributes(p.GetAttributes()) + SourceSyntax.Parameter(p, p.Type.ToDisplayString()) + SourceSyntax.DefaultValue(p)));
        var arguments = string.Join(", ", inherited.Parameters.Select(SourceSyntax.Argument));
        return Attributes(inherited.GetAttributes()) + Attributes(inherited.GetReturnTypeAttributes(), "return: ") +
               $"{SourceSyntax.AccessibilityKeyword(inherited.DeclaredAccessibility)} {(inherited.IsStatic ? "static " : "")}new {inherited.ReturnType.ToDisplayString()} " +
               $"{inherited.Name}({parameters}) => {Target(inherited)}.{inherited.Name}({arguments});";
    }

    // Callers keep what the attributes give them, e.g. [CallerMemberName] values, [Obsolete] warnings and nullability hints.
    // Compiler and interop attributes describe the declaration itself (params, defaults, P/Invoke), which the redeclaration writes out on its own.
    private static bool IsCopied(AttributeData attribute, INamedTypeSymbol containingType, Compilation compilation) =>
        attribute.AttributeClass is { } attributeClass && compilation.IsSymbolAccessibleWithin(attributeClass, containingType) &&
        attributeClass.ToDisplayString() is var name && name != "System.ParamArrayAttribute" &&
        !name.StartsWith("System.Runtime.InteropServices.", StringComparison.Ordinal) &&
        (!name.StartsWith("System.Runtime.CompilerServices.", StringComparison.Ordinal) || attributeClass.Name.StartsWith("Caller", StringComparison.Ordinal));

    private static string Render(AttributeData attribute) =>
        $"global::{attribute.AttributeClass!.ToDisplayString()}(" +
        string.Join(", ", attribute.ConstructorArguments.Select(a => a.ToCSharpString())
            .Concat(attribute.NamedArguments.Select(a => $"{a.Key} = {a.Value.ToCSharpString()}"))) + ")";

    private static string Target(IMethodSymbol inherited) =>
        inherited.IsStatic ? inherited.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : "base";

    // Redeclaring a virtual method would hide it, so a type further down could no longer override it.
    private static bool CanForward(IMethodSymbol inherited) =>
        inherited is { IsGenericMethod: false, IsVararg: false, RefKind: RefKind.None, IsVirtual: false, IsAbstract: false, IsOverride: false };

    // Parameters differing only in ref, out or in can't overload each other, so only by-reference versus by-value counts.
    private static string Signature(IMethodSymbol method) =>
        $"{method.Arity}({string.Join(",", method.Parameters.Select(p => (p.RefKind == RefKind.None ? "" : "&") + p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))})";

    // The analyzer validates against a compilation that already contains the generated code, which isn't the user's.
    public static IEnumerable<IMethodSymbol> UserDeclaredMethods(INamedTypeSymbol type, string name) =>
        type.GetMembers(name).OfType<IMethodSymbol>().Where(m => !m.DeclaringSyntaxReferences.Any(r => GeneratedCode.IsGeneratedFile(r.SyntaxTree.FilePath)));
}
