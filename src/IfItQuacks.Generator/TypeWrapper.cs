using System.Text;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

internal static class TypeWrapper
{
    public static string WrapInContainingScope(INamedTypeSymbol type, string memberSource)
    {
        var typeChain = Utilities.EnclosingTypes(type).Reverse().ToList();

        var sb = new StringBuilder();
        var ns = type.ContainingNamespace;
        var hasNamespace = ns is { IsGlobalNamespace: false };
        if (hasNamespace)
        {
            sb.AppendLine($"namespace {ns.ToDisplayString()}");
            sb.AppendLine("{");
        }

        var indent = hasNamespace ? "    " : "";
        foreach (var t in typeChain)
        {
            var kind = (t.IsRecord, t.TypeKind) switch
            {
                (true, TypeKind.Struct) => "record struct",
                (true, _) => "record",
                (false, TypeKind.Struct) => "struct",
                (false, TypeKind.Interface) => "interface",
                _ => "class",
            };
            var modifiers = (t.IsStatic ? "static " : "") +
                            (t.IsReadOnly ? "readonly " : "") +
                            (t.IsRefLikeType ? "ref " : "");
            sb.AppendLine($"{indent}{Utilities.AccessibilityKeyword(t.DeclaredAccessibility)} {modifiers}partial {kind} {t.Name}{TypeParams(t)}");
            sb.AppendLine($"{indent}{{");
            indent = new string(' ', indent.Length + 4);
        }

        foreach (var line in memberSource.Split('\n'))
            sb.AppendLine(indent + line.TrimEnd('\r'));

        for (var i = 0; i < typeChain.Count; i++)
        {
            indent = indent.Substring(0, indent.Length - 4);
            sb.AppendLine(indent + "}");
        }

        if (hasNamespace)
            sb.AppendLine("}");

        return sb.ToString();
    }

    public static (string Prefix, string Suffix) WrapTemplate(INamedTypeSymbol type)
    {
        const string placeholder = "__MEMBERS__";
        var wrapped = WrapInContainingScope(type, placeholder);
        var index = wrapped.IndexOf(placeholder, StringComparison.Ordinal);
        return (wrapped.Substring(0, index), wrapped.Substring(index + placeholder.Length));
    }

    private static string TypeParams(INamedTypeSymbol t) =>
        t.TypeParameters.Length == 0 ? "" : $"<{string.Join(", ", t.TypeParameters.Select(p => p.Name))}>";
}
