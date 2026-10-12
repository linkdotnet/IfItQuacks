using System.Text;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

/// <summary>
/// Adapts a sequence element by element, so an <c>IEnumerable&lt;Person&gt;</c> can be passed where an
/// <c>IEnumerable&lt;INamed&gt;</c> is expected. An element may itself be a sequence, which is adapted the same way.
/// A writable collection takes back what it handed out: a value written to it is unwrapped to the original element.
/// </summary>
internal static class SequenceAdapterEmitter
{
    private const string Enumerable = "System.Collections.Generic.IEnumerable`1";
    private const string ReadOnlyCollection = "System.Collections.Generic.IReadOnlyCollection`1";
    private const string ReadOnlyList = "System.Collections.Generic.IReadOnlyList`1";
    private const string Collection = "System.Collections.Generic.ICollection`1";
    private const string List = "System.Collections.Generic.IList`1";
    private const string ReadOnlyDictionary = "System.Collections.Generic.IReadOnlyDictionary`2";
    private const string AsyncEnumerable = "System.Collections.Generic.IAsyncEnumerable`1";
    private const string Generic = "global::System.Collections.Generic.";

    internal sealed record SequenceMatch(INamedTypeSymbol ElementShape, ITypeSymbol SourceElement, string ShapeDefinition);

    public static SequenceMatch? TryMatch(INamedTypeSymbol shape, ITypeSymbol source, Compilation compilation)
    {
        var definition = MetadataName(shape.OriginalDefinition);
        if (definition is not (Enumerable or ReadOnlyCollection or ReadOnlyList or Collection or List or ReadOnlyDictionary or AsyncEnumerable))
            return null;

        // A dictionary adapts its values only: its keys are looked up, so they have to stay the same type.
        var elementIndex = shape.TypeArguments.Length - 1;
        if (shape.TypeArguments[elementIndex] is not INamedTypeSymbol { TypeKind: TypeKind.Interface } elementShape)
            return null;

        return SelfAndAllInterfaces(source)
            .Where(i => MetadataName(i.OriginalDefinition) == definition &&
                        (elementIndex == 0 || SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], shape.TypeArguments[0])))
            .Select(i => i.TypeArguments[elementIndex])
            .Where(element => IsAdaptable(elementShape, element, compilation))
            .Select(element => new SequenceMatch(elementShape, element, definition))
            .FirstOrDefault();
    }

    /// <summary>
    /// Whether an element already converts to the element interface. An invariant collection like <c>IList&lt;T&gt;</c>
    /// still needs a wrapper then, but no element adapter.
    /// </summary>
    public static bool IsAssignableElement(SequenceMatch match, Compilation compilation) =>
        ShapeMatcher.IsAssignable(match.SourceElement, match.ElementShape, compilation);

    /// <summary>Whether an element is adapted by an adapter for a named type, rather than by another sequence adapter.</summary>
    public static bool IsNamedElement(SequenceMatch match, Compilation compilation) =>
        !IsAssignableElement(match, compilation) &&
        match.SourceElement is INamedTypeSymbol named && ShapeMatcher.FindMismatch(match.ElementShape, named, compilation) is null;

    public static string GetAdapterName(INamedTypeSymbol shape, ITypeSymbol source) =>
        $"SequenceAdapter_{GeneratedCode.ToIdentifier(shape.ToDisplayString())}_{GeneratedCode.ToIdentifier(source.ToDisplayString())}";

    public static string Emit(INamedTypeSymbol shape, ITypeSymbol source, SequenceMatch match, string? elementAdapter, string adapterName)
    {
        var element = $"global::{match.ElementShape.ToDisplayString()}";
        var sourceElement = match.SourceElement.ToDisplayString();
        string Wrap(string value) => elementAdapter is null
            ? $"(({element})({value}))"
            : $"(({element})(new global::{GeneratedCode.Namespace}.{elementAdapter}({value})))";

        var code = new StringBuilder();
        code.AppendLine($"    internal readonly struct {adapterName} : global::{shape.ToDisplayString()}, global::IfItQuacks.IDuckAdapter");
        code.AppendLine("    {");
        code.AppendLine($"        private readonly {source.ToDisplayString()} _value;");
        code.AppendLine($"        public {adapterName}({source.ToDisplayString()} value) => _value = value;");
        code.AppendLine("        object? global::IfItQuacks.IDuckAdapter.Value => _value;");

        switch (match.ShapeDefinition)
        {
            case AsyncEnumerable:
                EmitAsyncEnumerable(code, element, sourceElement, Wrap);
                break;
            case ReadOnlyDictionary:
                EmitReadOnlyDictionary(code, shape.TypeArguments[0].ToDisplayString(), element, sourceElement, Wrap);
                break;
            default:
                EmitEnumerable(code, element, sourceElement, Wrap);
                if (match.ShapeDefinition is ReadOnlyCollection or ReadOnlyList)
                    code.AppendLine($"        int {Generic}IReadOnlyCollection<{element}>.Count => (({Generic}IReadOnlyCollection<{sourceElement}>)_value).Count;");
                if (match.ShapeDefinition == ReadOnlyList)
                    code.AppendLine($"        {element} {Generic}IReadOnlyList<{element}>.this[int index] => {Wrap($"(({Generic}IReadOnlyList<{sourceElement}>)_value)[index]")};");
                if (match.ShapeDefinition is Collection or List)
                    EmitCollection(code, element, match.SourceElement, Wrap);
                if (match.ShapeDefinition == List)
                    EmitList(code, element, sourceElement, Wrap);
                break;
        }

        AdapterEmitter.EmitIdentityMembers(code, source, adapterName);
        code.AppendLine("    }");
        return code.ToString();
    }

    private static void EmitEnumerable(StringBuilder code, string element, string sourceElement, Func<string, string> wrap)
    {
        code.AppendLine($"        {Generic}IEnumerator<{element}> {Generic}IEnumerable<{element}>.GetEnumerator()");
        code.AppendLine("        {");
        code.AppendLine($"            foreach (var item in ({Generic}IEnumerable<{sourceElement}>)_value)");
        code.AppendLine($"                yield return {wrap("item")};");
        code.AppendLine("        }");
        code.AppendLine("        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() =>");
        code.AppendLine($"            (({Generic}IEnumerable<{element}>)this).GetEnumerator();");
    }

    private static void EmitAsyncEnumerable(StringBuilder code, string element, string sourceElement, Func<string, string> wrap)
    {
        code.AppendLine($"        async {Generic}IAsyncEnumerator<{element}> {Generic}IAsyncEnumerable<{element}>.GetAsyncEnumerator(global::System.Threading.CancellationToken cancellationToken)");
        code.AppendLine("        {");
        code.AppendLine("            await foreach (var item in global::System.Threading.Tasks.TaskAsyncEnumerableExtensions.WithCancellation(");
        code.AppendLine($"                ({Generic}IAsyncEnumerable<{sourceElement}>)_value, cancellationToken).ConfigureAwait(false))");
        code.AppendLine($"                yield return {wrap("item")};");
        code.AppendLine("        }");
    }

    private static void EmitReadOnlyDictionary(StringBuilder code, string key, string element, string sourceElement, Func<string, string> wrap)
    {
        var shape = $"{Generic}IReadOnlyDictionary<{key}, {element}>";
        var source = $"(({Generic}IReadOnlyDictionary<{key}, {sourceElement}>)_value)";
        var pair = $"{Generic}KeyValuePair<{key}, {element}>";

        code.AppendLine($"        {element} {shape}.this[{key} key] => {wrap($"{source}[key]")};");
        code.AppendLine($"        {Generic}IEnumerable<{key}> {shape}.Keys => {source}.Keys;");
        code.AppendLine($"        {Generic}IEnumerable<{element}> {shape}.Values");
        code.AppendLine("        {");
        code.AppendLine("            get");
        code.AppendLine("            {");
        code.AppendLine($"                foreach (var item in {source}.Values)");
        code.AppendLine($"                    yield return {wrap("item")};");
        code.AppendLine("            }");
        code.AppendLine("        }");
        code.AppendLine($"        bool {shape}.ContainsKey({key} key) => {source}.ContainsKey(key);");
        // The interface's [MaybeNullWhen(false)] isn't available on every target framework, so the warning about leaving it out is silenced instead.
        code.AppendLine("#nullable disable warnings");
        code.AppendLine($"        bool {shape}.TryGetValue({key} key, out {element} value)");
        code.AppendLine("        {");
        code.AppendLine($"            var found = {source}.TryGetValue(key, out var item);");
        code.AppendLine($"            value = found ? {wrap("item")} : default;");
        code.AppendLine("            return found;");
        code.AppendLine("        }");
        code.AppendLine("#nullable restore warnings");
        code.AppendLine($"        int {Generic}IReadOnlyCollection<{pair}>.Count => {source}.Count;");
        code.AppendLine($"        {Generic}IEnumerator<{pair}> {Generic}IEnumerable<{pair}>.GetEnumerator()");
        code.AppendLine("        {");
        code.AppendLine($"            foreach (var pair in {source})");
        code.AppendLine($"                yield return new {pair}(pair.Key, {wrap("pair.Value")});");
        code.AppendLine("        }");
        code.AppendLine("        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() =>");
        code.AppendLine($"            (({Generic}IEnumerable<{pair}>)this).GetEnumerator();");
    }

    // A value written to the collection has to be the original element type, which an adapter handed out by it wraps.
    private static void EmitCollection(StringBuilder code, string element, ITypeSymbol sourceElementType, Func<string, string> wrap)
    {
        var sourceElement = sourceElementType.ToDisplayString();
        var pattern = sourceElementType.WithoutNullability().ToDisplayString();
        var shape = $"{Generic}ICollection<{element}>";
        var source = $"(({Generic}ICollection<{sourceElement}>)_value)";

        code.AppendLine($"        int {shape}.Count => {source}.Count;");
        code.AppendLine($"        bool {shape}.IsReadOnly => {source}.IsReadOnly;");
        code.AppendLine($"        void {shape}.Add({element} item) => {source}.Add(__Unwrap(item));");
        code.AppendLine($"        void {shape}.Clear() => {source}.Clear();");
        code.AppendLine($"        bool {shape}.Contains({element} item) => __TryUnwrap(item, out var value) && {source}.Contains(value);");
        code.AppendLine($"        bool {shape}.Remove({element} item) => __TryUnwrap(item, out var value) && {source}.Remove(value);");
        code.AppendLine($"        void {shape}.CopyTo({element}[] array, int arrayIndex)");
        code.AppendLine("        {");
        code.AppendLine($"            foreach (var item in {source})");
        code.AppendLine($"                array[arrayIndex++] = {wrap("item")};");
        code.AppendLine("        }");
        code.AppendLine($"        private static {sourceElement} __Unwrap({element} item) => __TryUnwrap(item, out var value)");
        code.AppendLine("            ? value");
        code.AppendLine($"            : throw new global::System.ArgumentException(\"Only '{pattern}' values or adapters of them can be stored in this collection.\", nameof(item));");
        code.AppendLine($"        private static bool __TryUnwrap({element} item, out {sourceElement} value)");
        code.AppendLine("        {");
        code.AppendLine($"            if (item is global::IfItQuacks.IDuckAdapter {{ Value: {pattern} adapted }})");
        code.AppendLine("            {");
        code.AppendLine("                value = adapted;");
        code.AppendLine("                return true;");
        code.AppendLine("            }");
        code.AppendLine($"            if ((object?)item is {pattern} same)");
        code.AppendLine("            {");
        code.AppendLine("                value = same;");
        code.AppendLine("                return true;");
        code.AppendLine("            }");
        code.AppendLine("            value = default!;");
        code.AppendLine("            return false;");
        code.AppendLine("        }");
    }

    private static void EmitList(StringBuilder code, string element, string sourceElement, Func<string, string> wrap)
    {
        var shape = $"{Generic}IList<{element}>";
        var source = $"(({Generic}IList<{sourceElement}>)_value)";

        code.AppendLine($"        {element} {shape}.this[int index] {{ get => {wrap($"{source}[index]")}; set => {source}[index] = __Unwrap(value); }}");
        code.AppendLine($"        int {shape}.IndexOf({element} item) => __TryUnwrap(item, out var value) ? {source}.IndexOf(value) : -1;");
        code.AppendLine($"        void {shape}.Insert(int index, {element} item) => {source}.Insert(index, __Unwrap(item));");
        code.AppendLine($"        void {shape}.RemoveAt(int index) => {source}.RemoveAt(index);");
    }

    // Only reached when the sequence itself doesn't convert, so an assignable element means an invariant collection like IList<T>.
    private static bool IsAdaptable(INamedTypeSymbol elementShape, ITypeSymbol element, Compilation compilation) =>
        element.TypeKind != TypeKind.Error &&
        (ShapeMatcher.IsAssignable(element, elementShape, compilation) ||
         (element is INamedTypeSymbol named && ShapeMatcher.FindMismatch(elementShape, named, compilation) is null) ||
         TryMatch(elementShape, element, compilation) is not null);

    private static IEnumerable<INamedTypeSymbol> SelfAndAllInterfaces(ITypeSymbol source) => source switch
    {
        INamedTypeSymbol { TypeKind: TypeKind.Interface } named => named.AllInterfaces.Prepend(named),
        INamedTypeSymbol named => named.AllInterfaces,
        IArrayTypeSymbol array => array.AllInterfaces,
        _ => [],
    };

    private static string MetadataName(INamedTypeSymbol type) =>
        type.ContainingNamespace.IsGlobalNamespace ? type.MetadataName : $"{type.ContainingNamespace.ToDisplayString()}.{type.MetadataName}";
}
