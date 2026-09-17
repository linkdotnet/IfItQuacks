using System.Text;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

internal static class AdapterEmitter
{
    private const string CastByExample = "__CastByExample";

    public static string GetAdapterName(INamedTypeSymbol shape, INamedTypeSymbol concreteType) =>
        concreteType.IsAnonymousType
            ? $"ShapeAdapter_{Sanitize(shape.ToDisplayString())}_Anonymous_{Sanitize(AnonymousWitness(concreteType))}"
            : $"ShapeAdapter_{Sanitize(shape.ToDisplayString())}_{Sanitize(concreteType.ToDisplayString())}";

    public static string? FindUnnameableProperty(INamedTypeSymbol anonymousType) =>
        anonymousType.GetMembers().OfType<IPropertySymbol>()
            .Select(p => p.Type switch
            {
                INamedTypeSymbol { IsAnonymousType: true } nested => FindUnnameableProperty(nested) is { } inner ? $"{p.Name}.{inner}" : null,
                _ when ContainsAnonymousType(p.Type) => p.Name,
                _ => null,
            })
            .FirstOrDefault(name => name is not null);

    public static string Emit(INamedTypeSymbol shape, INamedTypeSymbol concreteType, string adapterName, Compilation compilation)
    {
        var sb = new StringBuilder();
        var valueTypeName = concreteType.IsAnonymousType ? "object" : concreteType.ToDisplayString();
        // Anonymous types can't be named, but an identical anonymous object expression in the same compilation has the same type.
        var receiver = concreteType.IsAnonymousType
            ? $"{CastByExample}(_value, static () => {AnonymousWitness(concreteType)})"
            : "_value";

        sb.AppendLine($"    internal readonly struct {adapterName} : global::{shape.ToDisplayString()}, global::IfItQuacks.IDuckAdapter");
        sb.AppendLine("    {");
        sb.AppendLine($"        private readonly {valueTypeName} _value;");
        sb.AppendLine($"        public {adapterName}({valueTypeName} value) => _value = value;");
        sb.AppendLine("        object? global::IfItQuacks.IDuckAdapter.Value => _value;");

        foreach (var member in ShapeMatcher.GetShapeMembers(shape))
        {
            var counterpart = ShapeMatcher.FindCounterpart(member, concreteType, compilation);
            if (counterpart is null)
                continue;

            switch (member)
            {
                case IMethodSymbol method:
                    EmitMethod(sb, method, (IMethodSymbol)counterpart, receiver);
                    break;
                case IPropertySymbol { IsIndexer: true } indexer:
                    EmitIndexer(sb, indexer, receiver);
                    break;
                case IPropertySymbol property:
                    EmitProperty(sb, property, receiver);
                    break;
                case IEventSymbol @event:
                    EmitEvent(sb, @event, receiver);
                    break;
            }
        }

        EmitIdentityMembers(sb, shape, concreteType);

        if (concreteType.IsAnonymousType)
            sb.AppendLine($"        private static T {CastByExample}<T>(object value, global::System.Func<T> example) => (T)value;");

        sb.AppendLine("    }");
        return sb.ToString();
    }

    private static void EmitMethod(StringBuilder sb, IMethodSymbol method, IMethodSymbol counterpart, string receiver)
    {
        var parameters = FormatParameters(method.Parameters);
        // Casting to the matched parameter types keeps overload resolution on the member the shape was matched against.
        var args = string.Join(", ", method.Parameters.Select((p, i) =>
            SymbolEqualityComparer.Default.Equals(p.Type, counterpart.Parameters[i].Type)
                ? Utilities.Argument(p)
                : $"({counterpart.Parameters[i].Type.ToDisplayString()}){Utilities.Identifier(p.Name)}"));
        sb.AppendLine($"        public {method.ReturnType.ToDisplayString()} {method.Name}({parameters}) => {receiver}.{method.Name}({args});");
    }

    private static void EmitProperty(StringBuilder sb, IPropertySymbol property, string receiver)
    {
        sb.Append($"        public {property.Type.ToDisplayString()} {property.Name} {{ ");
        if (property.GetMethod is not null) sb.Append($"get => {receiver}.{property.Name}; ");
        if (property.SetMethod is not null) sb.Append($"set => {receiver}.{property.Name} = value; ");
        sb.AppendLine("}");
    }

    private static void EmitIndexer(StringBuilder sb, IPropertySymbol indexer, string receiver)
    {
        var args = string.Join(", ", indexer.Parameters.Select(Utilities.Argument));
        sb.Append($"        public {indexer.Type.ToDisplayString()} this[{FormatParameters(indexer.Parameters)}] {{ ");
        if (indexer.GetMethod is not null) sb.Append($"get => {receiver}[{args}]; ");
        if (indexer.SetMethod is not null) sb.Append($"set => {receiver}[{args}] = value; ");
        sb.AppendLine("}");
    }

    private static void EmitEvent(StringBuilder sb, IEventSymbol @event, string receiver) =>
        sb.AppendLine($"        public event {@event.Type.ToDisplayString()} {@event.Name} {{ add => {receiver}.{@event.Name} += value; remove => {receiver}.{@event.Name} -= value; }}");

    // Adapters are boxed as the shape, so without forwarding two views of the same instance would neither be equal nor hash alike.
    private static void EmitIdentityMembers(StringBuilder sb, INamedTypeSymbol shape, INamedTypeSymbol concreteType)
    {
        var declared = ShapeMatcher.GetShapeMembers(shape).OfType<IMethodSymbol>().ToList();
        bool Declares(string name, int parameterCount) => declared.Any(m => m.Name == name && m.Parameters.Length == parameterCount);

        var isReference = concreteType.IsReferenceType;
        if (!Declares(nameof(Equals), 1))
            sb.AppendLine("        public override bool Equals(object? obj) => global::System.Object.Equals(_value, global::IfItQuacks.Duck.Unwrap(obj));");
        if (!Declares(nameof(GetHashCode), 0))
            sb.AppendLine($"        public override int GetHashCode() => {(isReference ? "_value?.GetHashCode() ?? 0" : "_value.GetHashCode()")};");
        if (!Declares(nameof(ToString), 0))
            sb.AppendLine($"        public override string ToString() => {(isReference ? "_value?.ToString()" : "_value.ToString()")} ?? string.Empty;");
    }

    private static string AnonymousWitness(INamedTypeSymbol anonymousType) =>
        "new { " + string.Join(", ", anonymousType.GetMembers().OfType<IPropertySymbol>().Select(p =>
            $"{Utilities.Identifier(p.Name)} = {(p.Type is INamedTypeSymbol { IsAnonymousType: true } nested ? AnonymousWitness(nested) : $"default({p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})!")}")) + " }";

    private static bool ContainsAnonymousType(ITypeSymbol type) => type switch
    {
        INamedTypeSymbol { IsAnonymousType: true } => true,
        INamedTypeSymbol named => named.TypeArguments.Any(ContainsAnonymousType),
        IArrayTypeSymbol array => ContainsAnonymousType(array.ElementType),
        _ => false,
    };

    private static string FormatParameters(IEnumerable<IParameterSymbol> parameters) =>
        string.Join(", ", parameters.Select(p => Utilities.Parameter(p, p.Type.ToDisplayString())));

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }
}
