using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

internal static class AdapterEmitter
{
    private const string CastByExample = "__CastByExample";

    public static string GetAdapterName(INamedTypeSymbol shape, INamedTypeSymbol concreteType, string prefix = "ShapeAdapter") =>
        concreteType.IsAnonymousType
            ? $"{prefix}_{Sanitize(shape.ToDisplayString())}_Anonymous_{Sanitize(AnonymousWitness(concreteType))}"
            : $"{prefix}_{Sanitize(shape.ToDisplayString())}_{Sanitize(concreteType.ToDisplayString())}";

    public static string GetStubAdapterName(INamedTypeSymbol shape, INamedTypeSymbol? concreteType) =>
        concreteType is null ? $"StubAdapter_{Sanitize(shape.ToDisplayString())}" : GetAdapterName(shape, concreteType, "StubAdapter");

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

    /// <summary>Emits a stub: every member <paramref name="concreteType"/> provides forwards to it, the rest throws.</summary>
    public static string EmitStub(INamedTypeSymbol shape, INamedTypeSymbol? concreteType, string adapterName, Compilation compilation)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"    internal readonly struct {adapterName} : global::{shape.ToDisplayString()}, global::IfItQuacks.IDuckAdapter");
        sb.AppendLine("    {");

        if (concreteType is null)
        {
            sb.AppendLine("        object? global::IfItQuacks.IDuckAdapter.Value => null;");
            EmitStubMembers(sb, ShapeMatcher.GetShapeMembers(shape).Where(ShapeMatcher.IsRequired));
        }
        else
        {
            var valueTypeName = concreteType.IsAnonymousType ? "object" : concreteType.ToDisplayString();
            var receiver = concreteType.IsAnonymousType
                ? $"{CastByExample}(_value, static () => {AnonymousWitness(concreteType)})"
                : "_value";

            sb.AppendLine($"        private readonly {valueTypeName} _value;");
            sb.AppendLine($"        public {adapterName}({valueTypeName} value) => _value = value;");
            sb.AppendLine("        object? global::IfItQuacks.IDuckAdapter.Value => _value;");

            EmitInstanceMembers(sb, shape, concreteType, receiver, compilation, Owner);
            EmitStubMembers(sb, ShapeMatcher.FindUnimplementedMembers(shape, concreteType, compilation));
            EmitIdentityMembers(sb, concreteType);

            if (concreteType.IsAnonymousType)
                sb.AppendLine($"        private static T {CastByExample}<T>(object value, global::System.Func<T> example) => (T)value;");
        }

        sb.AppendLine("    }");
        return sb.ToString();
    }

    private static void EmitStubMembers(StringBuilder sb, IEnumerable<ISymbol> members)
    {
        foreach (var member in members)
        {
            var thrown = $"throw new global::IfItQuacks.DuckStubException(\"{MemberName(member)}\")";
            switch (member)
            {
                case IMethodSymbol method:
                    sb.AppendLine($"        {Utilities.RefReturnPrefix(method.RefKind)}{method.ReturnType.ToDisplayString()} {Owner(method)}.{method.Name}{TypeParameters(method)}({FormatParameters(method.Parameters)}) => {thrown};");
                    break;
                case IPropertySymbol { IsIndexer: true } indexer:
                    sb.Append($"        {Utilities.RefReturnPrefix(indexer.RefKind)}{indexer.Type.ToDisplayString()} {Owner(indexer)}.this[{FormatParameters(indexer.Parameters)}] {{ ");
                    if (indexer.GetMethod is not null) sb.Append($"get => {thrown}; ");
                    if (indexer.SetMethod is { } indexerSetter) sb.Append($"{SetterKeyword(indexerSetter)} => {thrown}; ");
                    sb.AppendLine("}");
                    break;
                case IPropertySymbol property:
                    sb.Append($"        {Utilities.RefReturnPrefix(property.RefKind)}{property.Type.ToDisplayString()} {Owner(property)}.{property.Name} {{ ");
                    if (property.GetMethod is not null) sb.Append($"get => {thrown}; ");
                    if (property.SetMethod is { } setter) sb.Append($"{SetterKeyword(setter)} => {thrown}; ");
                    sb.AppendLine("}");
                    break;
                case IEventSymbol @event:
                    sb.AppendLine($"        event {@event.Type.ToDisplayString()} {Owner(@event)}.{@event.Name} {{ add => {thrown}; remove => {thrown}; }}");
                    break;
            }
        }
    }

    private static string MemberName(ISymbol member) =>
        member.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    private static string TypeParameters(IMethodSymbol method) =>
        method.IsGenericMethod ? $"<{string.Join(", ", method.TypeParameters.Select(tp => tp.Name))}>" : "";

    /// <summary>Emits the interface's instance members as explicit implementations forwarding to <paramref name="receiver"/>.</summary>
    public static void EmitInstanceMembers(StringBuilder sb, INamedTypeSymbol shape, INamedTypeSymbol concreteType, string receiver,
        Compilation compilation, Func<ISymbol, string> owner)
    {
        // A [DuckShape<>] member is a symbol of the source type, so the interface it is implemented for and
        // the signature it carries both come from the mapping, not from the symbol itself.
        var mapped = MappedShapeEmitter.TryGetInfo(shape);
        if (mapped is not null)
        {
            var shapeName = shape.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            owner = _ => shapeName;
        }

        foreach (var member in ShapeMatcher.GetShapeMembers(shape))
        {
            var counterpart = ShapeMatcher.FindCounterpart(member, concreteType, compilation, mapped);
            if (counterpart is null)
                continue;

            EmitMember(sb, member, counterpart, Qualify(receiver, concreteType, counterpart), owner, mapped);
        }
    }

    private static void EmitMember(StringBuilder sb, ISymbol member, ISymbol counterpart, string receiver, Func<ISymbol, string> owner,
        MappedShapeEmitter.Info? mapped = null)
    {
        switch (member)
        {
            case IMethodSymbol method when counterpart is IMethodSymbol target:
                EmitMethod(sb, method, target, receiver, owner, mapped);
                break;
            case IMethodSymbol method:
                EmitDelegateMethod(sb, method, counterpart, receiver, owner);
                break;
            case IPropertySymbol { IsIndexer: true } indexer:
                EmitIndexer(sb, indexer, receiver, owner, mapped);
                break;
            case IPropertySymbol property:
                EmitProperty(sb, property, receiver, owner, mapped);
                break;
            case IEventSymbol @event:
                EmitEvent(sb, @event, receiver, owner);
                break;
        }
    }

    private static string MemberType(ISymbol member, ITypeSymbol type, MappedShapeEmitter.Info? mapped) =>
        mapped is null ? type.ToDisplayString() : MappedShapeEmitter.MemberTypeName(member, type, mapped);

    public static string GetMergeAdapterName(INamedTypeSymbol shape, IEnumerable<INamedTypeSymbol> sources) =>
        $"MergeAdapter_{Sanitize(shape.ToDisplayString())}_" +
        string.Join("_", sources.Select(s => Sanitize(s.IsAnonymousType ? AnonymousWitness(s) : s.ToDisplayString())));

    /// <summary>Emits an adapter taking each interface member from the first source that provides it.</summary>
    public static string EmitMerge(INamedTypeSymbol shape, ImmutableArray<INamedTypeSymbol> sources, string adapterName, Compilation compilation)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"    internal readonly struct {adapterName} : global::{shape.ToDisplayString()}, global::IfItQuacks.IDuckAdapter");
        sb.AppendLine("    {");

        for (var i = 0; i < sources.Length; i++)
            sb.AppendLine($"        private readonly {(sources[i].IsAnonymousType ? "object" : sources[i].ToDisplayString())} _value{i};");

        var parameters = string.Join(", ", sources.Select((s, i) => $"{(s.IsAnonymousType ? "object" : s.ToDisplayString())} value{i}"));
        var assignments = string.Join(" ", sources.Select((_, i) => $"_value{i} = value{i};"));
        sb.AppendLine($"        public {adapterName}({parameters}) {{ {assignments} }}");
        sb.AppendLine("        object? global::IfItQuacks.IDuckAdapter.Value => _value0;");

        foreach (var member in ShapeMatcher.GetShapeMembers(shape))
        {
            var source = FindSource(sources, member, compilation);
            if (source is not { } found)
                continue;

            var receiver = sources[found.Index].IsAnonymousType
                ? $"{CastByExample}(_value{found.Index}, static () => {AnonymousWitness(sources[found.Index])})"
                : $"_value{found.Index}";
            receiver = found.Counterpart.ContainingType.TypeKind == TypeKind.Interface
                ? $"((global::{found.Counterpart.ContainingType.ToDisplayString()}){receiver})"
                : Qualify(receiver, sources[found.Index], found.Counterpart);
            EmitMember(sb, member, found.Counterpart, receiver, Owner);
        }

        EmitMergeIdentityMembers(sb, sources, adapterName);

        if (sources.Any(s => s.IsAnonymousType))
            sb.AppendLine($"        private static T {CastByExample}<T>(object value, global::System.Func<T> example) => (T)value;");

        sb.AppendLine("    }");
        return sb.ToString();
    }

    /// <summary>
    /// The first source implementing <paramref name="member"/>'s interface, else the first providing it structurally, or <c>null</c> if none does.
    /// </summary>
    public static (int Index, ISymbol Counterpart)? FindSource(ImmutableArray<INamedTypeSymbol> sources, ISymbol member, Compilation compilation)
    {
        for (var i = 0; i < sources.Length; i++)
            if (Implements(sources[i], member.ContainingType))
                return (i, member);
        for (var i = 0; i < sources.Length; i++)
            if (ShapeMatcher.FindCounterpart(member, sources[i], compilation) is { } counterpart)
                return (i, counterpart);
        return null;
    }

    private static bool Implements(INamedTypeSymbol source, INamedTypeSymbol @interface) =>
        SymbolEqualityComparer.Default.Equals(source, @interface) ||
        source.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, @interface));

    // Members are implemented explicitly, so interfaces inheriting same-named members (IEnumerable<T>.GetEnumerator) or declaring object members don't clash.
    private static void EmitMethod(StringBuilder sb, IMethodSymbol method, IMethodSymbol counterpart, string receiver, Func<ISymbol, string> owner,
        MappedShapeEmitter.Info? mapped = null)
    {
        var parameters = FormatParameters(method.Parameters);
        // Casting to the matched parameter types keeps overload resolution on the member the shape was matched against.
        var args = string.Join(", ", method.Parameters.Select((p, i) =>
            SymbolEqualityComparer.Default.Equals(p.Type, counterpart.Parameters[i].Type)
                ? Utilities.Argument(p)
                : $"({counterpart.Parameters[i].Type.ToDisplayString()}){Utilities.Identifier(p.Name)}"));
        sb.AppendLine($"        {Utilities.RefReturnPrefix(method.RefKind)}{MemberType(method, method.ReturnType, mapped)} {owner(method)}.{method.Name}({parameters}) => {RefExpressionPrefix(method.RefKind)}{receiver}.{counterpart.Name}({args});");
    }

    // A member holding a delegate implements the interface method through its Invoke.
    private static void EmitDelegateMethod(StringBuilder sb, IMethodSymbol method, ISymbol counterpart, string receiver, Func<ISymbol, string> owner)
    {
        var invoke = ShapeMatcher.DelegateTypeOf(counterpart)!.DelegateInvokeMethod!;
        var args = string.Join(", ", method.Parameters.Select((p, i) =>
            SymbolEqualityComparer.Default.Equals(p.Type, invoke.Parameters[i].Type)
                ? Utilities.Argument(p)
                : $"({invoke.Parameters[i].Type.ToDisplayString()}){Utilities.Identifier(p.Name)}"));
        sb.AppendLine($"        {Utilities.RefReturnPrefix(method.RefKind)}{method.ReturnType.ToDisplayString()} {owner(method)}.{method.Name}({FormatParameters(method.Parameters)}) => " +
                      $"{RefExpressionPrefix(method.RefKind)}{receiver}.{counterpart.Name}({args});");
    }

    private static void EmitProperty(StringBuilder sb, IPropertySymbol property, string receiver, Func<ISymbol, string> owner,
        MappedShapeEmitter.Info? mapped = null)
    {
        sb.Append($"        {Utilities.RefReturnPrefix(property.RefKind)}{MemberType(property, property.Type, mapped)} {owner(property)}.{property.Name} {{ ");
        if (property.GetMethod is not null) sb.Append($"get => {RefExpressionPrefix(property.RefKind)}{receiver}.{property.Name}; ");
        if (property.SetMethod is { } setter && (mapped is null || (MappedShapeEmitter.KeepsSetter(property, mapped) && !setter.IsInitOnly)))
            sb.Append($"{SetterKeyword(setter)} => {receiver}.{property.Name} = value; ");
        sb.AppendLine("}");
    }

    private static void EmitIndexer(StringBuilder sb, IPropertySymbol indexer, string receiver, Func<ISymbol, string> owner,
        MappedShapeEmitter.Info? mapped = null)
    {
        var args = string.Join(", ", indexer.Parameters.Select(Utilities.Argument));
        sb.Append($"        {Utilities.RefReturnPrefix(indexer.RefKind)}{MemberType(indexer, indexer.Type, mapped)} {owner(indexer)}.this[{FormatParameters(indexer.Parameters)}] {{ ");
        if (indexer.GetMethod is not null) sb.Append($"get => {RefExpressionPrefix(indexer.RefKind)}{receiver}[{args}]; ");
        if (indexer.SetMethod is { } setter && (mapped is null || (MappedShapeEmitter.KeepsSetter(indexer, mapped) && !setter.IsInitOnly)))
            sb.Append($"{SetterKeyword(setter)} => {receiver}[{args}] = value; ");
        sb.AppendLine("}");
    }

    private static void EmitEvent(StringBuilder sb, IEventSymbol @event, string receiver, Func<ISymbol, string> owner) =>
        sb.AppendLine($"        event {@event.Type.ToDisplayString()} {owner(@event)}.{@event.Name} {{ add => {receiver}.{@event.Name} += value; remove => {receiver}.{@event.Name} -= value; }}");

    // Adapters are boxed as the shape, so without forwarding two views of the same instance would neither be equal nor hash alike.
    public static void EmitIdentityMembers(StringBuilder sb, ITypeSymbol concreteType, string field = "_value")
    {
        var isReference = concreteType.IsReferenceType;
        sb.AppendLine($"        public override bool Equals(object? obj) => global::System.Object.Equals({field}, global::IfItQuacks.Duck.Unwrap(obj));");
        sb.AppendLine($"        public override int GetHashCode() => {(isReference ? $"{field}?.GetHashCode() ?? 0" : $"{field}.GetHashCode()")};");
        sb.AppendLine($"        public override string ToString() => {(isReference ? $"{field}?.ToString()" : $"{field}.ToString()")} ?? string.Empty;");
    }

    // Two merges differing only in a later value would otherwise compare equal.
    private static void EmitMergeIdentityMembers(StringBuilder sb, ImmutableArray<INamedTypeSymbol> sources, string adapterName)
    {
        var equalities = string.Join(" && ", sources.Select((_, i) => $"global::System.Object.Equals(_value{i}, other._value{i})"));
        var hashes = sources.Select((s, i) => s.IsReferenceType || s.IsAnonymousType ? $"(_value{i}?.GetHashCode() ?? 0)" : $"_value{i}.GetHashCode()").ToList();
        var hash = hashes.Skip(1).Aggregate(hashes[0], (acc, h) => $"({acc}) * -1521134295 + {h}");
        var isReference = sources[0].IsReferenceType;
        sb.AppendLine($"        public override bool Equals(object? obj) => obj is {adapterName} other && {equalities};");
        sb.AppendLine($"        public override int GetHashCode() => unchecked({hash});");
        sb.AppendLine($"        public override string ToString() => {(isReference ? "_value0?.ToString()" : "_value0.ToString()")} ?? string.Empty;");
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
