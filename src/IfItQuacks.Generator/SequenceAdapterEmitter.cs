using System.Text;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

/// <summary>
/// Adapts a sequence element by element, so an <c>IEnumerable&lt;Person&gt;</c> can be passed where an
/// <c>IEnumerable&lt;INamed&gt;</c> is expected. Only interfaces using their element type in output position qualify:
/// a writable collection would need the adaptation to run backwards.
/// </summary>
internal static class SequenceAdapterEmitter
{
    private const string Enumerable = "System.Collections.Generic.IEnumerable`1";
    private const string ReadOnlyCollection = "System.Collections.Generic.IReadOnlyCollection`1";
    private const string ReadOnlyList = "System.Collections.Generic.IReadOnlyList`1";

    internal sealed record SequenceMatch(INamedTypeSymbol ElementShape, INamedTypeSymbol SourceElement, string ShapeDefinition);

    public static SequenceMatch? TryMatch(INamedTypeSymbol shape, ITypeSymbol source, Compilation compilation)
    {
        var definition = MetadataName(shape.OriginalDefinition);
        if (definition is not (Enumerable or ReadOnlyCollection or ReadOnlyList))
            return null;

        if (shape.TypeArguments[0] is not INamedTypeSymbol { TypeKind: TypeKind.Interface } elementShape)
            return null;

        return AllInterfaces(source)
            .Where(i => MetadataName(i.OriginalDefinition) == definition)
            .Select(i => i.TypeArguments[0])
            .OfType<INamedTypeSymbol>()
            .Where(element => element.TypeKind != TypeKind.Error &&
                              ShapeMatcher.FindMismatch(elementShape, element, compilation) is null &&
                              !ShapeMatcher.IsAssignable(element, elementShape, compilation))
            .Select(element => new SequenceMatch(elementShape, element, definition))
            .FirstOrDefault();
    }

    public static string GetAdapterName(INamedTypeSymbol shape, ITypeSymbol source) =>
        $"SequenceAdapter_{Sanitize(shape.ToDisplayString())}_{Sanitize(source.ToDisplayString())}";

    public static string Emit(INamedTypeSymbol shape, ITypeSymbol source, SequenceMatch match, string elementAdapter, string adapterName)
    {
        var shapeName = $"global::{shape.ToDisplayString()}";
        var elementName = $"global::{match.ElementShape.ToDisplayString()}";
        var sourceElement = match.SourceElement.ToDisplayString();
        var wrap = $"{elementName})(new global::IfItQuacks.Generated.{elementAdapter}";

        var sb = new StringBuilder();
        sb.AppendLine($"    internal readonly struct {adapterName} : {shapeName}, global::IfItQuacks.IDuckAdapter");
        sb.AppendLine("    {");
        sb.AppendLine($"        private readonly {source.ToDisplayString()} _value;");
        sb.AppendLine($"        public {adapterName}({source.ToDisplayString()} value) => _value = value;");
        sb.AppendLine("        object? global::IfItQuacks.IDuckAdapter.Value => _value;");

        sb.AppendLine($"        global::System.Collections.Generic.IEnumerator<{elementName}> global::System.Collections.Generic.IEnumerable<{elementName}>.GetEnumerator()");
        sb.AppendLine("        {");
        sb.AppendLine($"            foreach (var item in (global::System.Collections.Generic.IEnumerable<{sourceElement}>)_value)");
        sb.AppendLine($"                yield return ({wrap}(item));");
        sb.AppendLine("        }");
        sb.AppendLine($"        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() =>");
        sb.AppendLine($"            ((global::System.Collections.Generic.IEnumerable<{elementName}>)this).GetEnumerator();");

        if (match.ShapeDefinition is ReadOnlyCollection or ReadOnlyList)
        {
            sb.AppendLine($"        int global::System.Collections.Generic.IReadOnlyCollection<{elementName}>.Count =>");
            sb.AppendLine($"            ((global::System.Collections.Generic.IReadOnlyCollection<{sourceElement}>)_value).Count;");
        }

        if (match.ShapeDefinition == ReadOnlyList)
        {
            sb.AppendLine($"        {elementName} global::System.Collections.Generic.IReadOnlyList<{elementName}>.this[int index] =>");
            sb.AppendLine($"            ({wrap}(((global::System.Collections.Generic.IReadOnlyList<{sourceElement}>)_value)[index]));");
        }

        AdapterEmitter.EmitIdentityMembers(sb, source);
        sb.AppendLine("    }");
        return sb.ToString();
    }

    private static IEnumerable<INamedTypeSymbol> AllInterfaces(ITypeSymbol source) => source switch
    {
        INamedTypeSymbol { TypeKind: TypeKind.Interface } named => named.AllInterfaces.Prepend(named),
        INamedTypeSymbol named => named.AllInterfaces,
        IArrayTypeSymbol array => array.AllInterfaces,
        _ => [],
    };

    private static string MetadataName(INamedTypeSymbol type) =>
        type.ContainingNamespace.IsGlobalNamespace ? type.MetadataName : $"{type.ContainingNamespace.ToDisplayString()}.{type.MetadataName}";

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }
}
