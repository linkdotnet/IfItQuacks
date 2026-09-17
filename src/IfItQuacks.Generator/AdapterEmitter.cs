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

        EmitInstanceMembers(sb, shape, concreteType, receiver, compilation, Owner);

        EmitIdentityMembers(sb, concreteType);

        if (concreteType.IsAnonymousType)
            sb.AppendLine($"        private static T {CastByExample}<T>(object value, global::System.Func<T> example) => (T)value;");

        sb.AppendLine("    }");
        return sb.ToString();
    }

    /// <summary>Emits the interface's instance members as explicit implementations forwarding to <paramref name="receiver"/>.</summary>
    public static void EmitInstanceMembers(StringBuilder sb, INamedTypeSymbol shape, INamedTypeSymbol concreteType, string receiver,
        Compilation compilation, Func<ISymbol, string> owner)
    {
        foreach (var member in ShapeMatcher.GetShapeMembers(shape))
        {
            var counterpart = ShapeMatcher.FindCounterpart(member, concreteType, compilation);
            if (counterpart is null)
                continue;

            var memberReceiver = Qualify(receiver, concreteType, counterpart);
            switch (member)
            {
                case IMethodSymbol method:
                    EmitMethod(sb, method, (IMethodSymbol)counterpart, memberReceiver, owner);
                    break;
                case IPropertySymbol { IsIndexer: true } indexer:
                    EmitIndexer(sb, indexer, memberReceiver, owner);
                    break;
                case IPropertySymbol property:
                    EmitProperty(sb, property, memberReceiver, owner);
                    break;
                case IEventSymbol @event:
                    EmitEvent(sb, @event, memberReceiver, owner);
                    break;
            }
        }
    }

    // Members are implemented explicitly, so interfaces inheriting same-named members (IEnumerable<T>.GetEnumerator) or declaring object members don't clash.
    private static void EmitMethod(StringBuilder sb, IMethodSymbol method, IMethodSymbol counterpart, string receiver, Func<ISymbol, string> owner)
    {
        var parameters = FormatParameters(method.Parameters);
        // Casting to the matched parameter types keeps overload resolution on the member the shape was matched against.
        var args = string.Join(", ", method.Parameters.Select((p, i) =>
            SymbolEqualityComparer.Default.Equals(p.Type, counterpart.Parameters[i].Type)
                ? Utilities.Argument(p)
                : $"({counterpart.Parameters[i].Type.ToDisplayString()}){Utilities.Identifier(p.Name)}"));
        sb.AppendLine($"        {Utilities.RefReturnPrefix(method.RefKind)}{method.ReturnType.ToDisplayString()} {owner(method)}.{method.Name}({parameters}) => {RefExpressionPrefix(method.RefKind)}{receiver}.{counterpart.Name}({args});");
    }

    private static void EmitProperty(StringBuilder sb, IPropertySymbol property, string receiver, Func<ISymbol, string> owner)
    {
        sb.Append($"        {Utilities.RefReturnPrefix(property.RefKind)}{property.Type.ToDisplayString()} {owner(property)}.{property.Name} {{ ");
        if (property.GetMethod is not null) sb.Append($"get => {RefExpressionPrefix(property.RefKind)}{receiver}.{property.Name}; ");
        if (property.SetMethod is { } setter) sb.Append($"{SetterKeyword(setter)} => {receiver}.{property.Name} = value; ");
        sb.AppendLine("}");
    }

    private static void EmitIndexer(StringBuilder sb, IPropertySymbol indexer, string receiver, Func<ISymbol, string> owner)
    {
        var args = string.Join(", ", indexer.Parameters.Select(Utilities.Argument));
        sb.Append($"        {Utilities.RefReturnPrefix(indexer.RefKind)}{indexer.Type.ToDisplayString()} {owner(indexer)}.this[{FormatParameters(indexer.Parameters)}] {{ ");
        if (indexer.GetMethod is not null) sb.Append($"get => {RefExpressionPrefix(indexer.RefKind)}{receiver}[{args}]; ");
        if (indexer.SetMethod is { } setter) sb.Append($"{SetterKeyword(setter)} => {receiver}[{args}] = value; ");
        sb.AppendLine("}");
    }

    private static void EmitEvent(StringBuilder sb, IEventSymbol @event, string receiver, Func<ISymbol, string> owner) =>
        sb.AppendLine($"        event {@event.Type.ToDisplayString()} {owner(@event)}.{@event.Name} {{ add => {receiver}.{@event.Name} += value; remove => {receiver}.{@event.Name} -= value; }}");

    // Adapters are boxed as the shape, so without forwarding two views of the same instance would neither be equal nor hash alike.
    public static void EmitIdentityMembers(StringBuilder sb, INamedTypeSymbol concreteType)
    {
        var isReference = concreteType.IsReferenceType;
        sb.AppendLine("        public override bool Equals(object? obj) => global::System.Object.Equals(_value, global::IfItQuacks.Duck.Unwrap(obj));");
        sb.AppendLine($"        public override int GetHashCode() => {(isReference ? "_value?.GetHashCode() ?? 0" : "_value.GetHashCode()")};");
        sb.AppendLine($"        public override string ToString() => {(isReference ? "_value?.ToString()" : "_value.ToString()")} ?? string.Empty;");
    }

    // A member of the concrete type can hide the inherited one the shape was matched against, so the receiver is cast to its declaring type.
    private static string Qualify(string receiver, INamedTypeSymbol concreteType, ISymbol counterpart)
    {
        var declaringType = counterpart.ContainingType;
        if (SymbolEqualityComparer.Default.Equals(declaringType, concreteType))
            return receiver;

        var hides = false;
        for (var t = concreteType; t is not null && !SymbolEqualityComparer.Default.Equals(t, declaringType); t = t.BaseType)
            hides |= t.GetMembers(counterpart.Name).Any(m => !SymbolEqualityComparer.Default.Equals(m, counterpart));

        return hides ? $"(({declaringType.ToDisplayString()}){receiver})" : receiver;
    }

    // An interface accessor declared init-only has to be implemented as init-only as well.
    private static string SetterKeyword(IMethodSymbol setter) => setter.IsInitOnly ? "init" : "set";

    private static string RefExpressionPrefix(RefKind kind) => kind == RefKind.None ? "" : "ref ";

    public static string Owner(ISymbol member) => member.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

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
