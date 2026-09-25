using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

internal static class AdapterEmitter
{

    public static string GetAdapterName(INamedTypeSymbol shape, INamedTypeSymbol concreteType, string prefix = "ShapeAdapter") =>
        concreteType.IsAnonymousType
            ? $"{prefix}_{GeneratedCode.ToIdentifier(shape.ToDisplayString())}_Anonymous_{GeneratedCode.ToIdentifier(AnonymousWitness(concreteType))}"
            : $"{prefix}_{GeneratedCode.ToIdentifier(shape.ToDisplayString())}_{GeneratedCode.ToIdentifier(concreteType.ToDisplayString())}";

    public static string GetStubAdapterName(INamedTypeSymbol shape, INamedTypeSymbol? concreteType) =>
        concreteType is null ? $"StubAdapter_{GeneratedCode.ToIdentifier(shape.ToDisplayString())}" : GetAdapterName(shape, concreteType, "StubAdapter");

    public static string? FindUnnameableProperty(INamedTypeSymbol anonymousType) =>
        anonymousType.GetMembers().OfType<IPropertySymbol>()
            .Select(p => p.Type switch
            {
                INamedTypeSymbol { IsAnonymousType: true } nested => FindUnnameableProperty(nested) is { } inner ? $"{p.Name}.{inner}" : null,
                _ when p.Type.ContainsType(t => t.IsAnonymousType) => p.Name,
                _ => null,
            })
            .FirstOrDefault(name => name is not null);

    public static string Emit(INamedTypeSymbol shape, INamedTypeSymbol concreteType, string adapterName, Compilation compilation, bool takesObject = false)
    {
        var code = new StringBuilder();
        var valueTypeName = concreteType.IsAnonymousType ? "object" : concreteType.ToDisplayString();
        // Anonymous types can't be named, but an identical anonymous object expression in the same compilation has the same type.
        var receiver = concreteType.IsAnonymousType
            ? GeneratedCode.CastByExample("_value", AnonymousWitness(concreteType))
            : "_value";

        code.AppendLine($"    internal readonly struct {adapterName} : global::{shape.ToDisplayString()}, global::IfItQuacks.IDuckAdapter");
        code.AppendLine("    {");
        code.AppendLine($"        private readonly {valueTypeName} _value;");
        code.AppendLine(takesObject
            ? $"        public {adapterName}(object value) => _value = ({valueTypeName})value;"
            : $"        public {adapterName}({valueTypeName} value) => _value = value;");
        code.AppendLine("        object? global::IfItQuacks.IDuckAdapter.Value => _value;");

        EmitInstanceMembers(code, shape, concreteType, receiver, compilation, ExplicitInterfaceName);

        EmitIdentityMembers(code, concreteType, adapterName);

        if (concreteType.IsAnonymousType)
            code.AppendLine("        " + GeneratedCode.CastByExampleDeclaration);

        code.AppendLine("    }");
        return code.ToString();
    }

    /// <summary>Emits a stub: every member <paramref name="concreteType"/> provides forwards to it, the rest throws.</summary>
    public static string EmitStub(INamedTypeSymbol shape, INamedTypeSymbol? concreteType, string adapterName, Compilation compilation)
    {
        var code = new StringBuilder();
        code.AppendLine($"    internal readonly struct {adapterName} : global::{shape.ToDisplayString()}, global::IfItQuacks.IDuckAdapter");
        code.AppendLine("    {");

        if (concreteType is null)
        {
            code.AppendLine("        object? global::IfItQuacks.IDuckAdapter.Value => null;");
            EmitStubMembers(code, ShapeMatcher.GetShapeMembers(shape).Where(ShapeMatcher.IsRequired));
        }
        else
        {
            var valueTypeName = concreteType.IsAnonymousType ? "object" : concreteType.ToDisplayString();
            var receiver = concreteType.IsAnonymousType
                ? GeneratedCode.CastByExample("_value", AnonymousWitness(concreteType))
                : "_value";

            code.AppendLine($"        private readonly {valueTypeName} _value;");
            code.AppendLine($"        public {adapterName}({valueTypeName} value) => _value = value;");
            code.AppendLine("        object? global::IfItQuacks.IDuckAdapter.Value => _value;");

            EmitInstanceMembers(code, shape, concreteType, receiver, compilation, ExplicitInterfaceName);
            EmitStubMembers(code, ShapeMatcher.FindUnimplementedMembers(shape, concreteType, compilation));
            EmitIdentityMembers(code, concreteType, adapterName);

            if (concreteType.IsAnonymousType)
                code.AppendLine("        " + GeneratedCode.CastByExampleDeclaration);
        }

        code.AppendLine("    }");
        return code.ToString();
    }

    private static void EmitStubMembers(StringBuilder code, IEnumerable<ISymbol> members)
    {
        foreach (var member in members)
        {
            var thrown = $"throw new global::IfItQuacks.DuckStubException(\"{MemberName(member)}\")";
            switch (member)
            {
                case IMethodSymbol method:
                    code.AppendLine($"        {SourceSyntax.RefReturnPrefix(method.RefKind)}{method.ReturnType.ToDisplayString()} {ExplicitInterfaceName(method)}.{method.Name}{TypeParameters(method)}({FormatParameters(method.Parameters)}) => {thrown};");
                    break;
                case IPropertySymbol { IsIndexer: true } indexer:
                    code.Append($"        {SourceSyntax.RefReturnPrefix(indexer.RefKind)}{indexer.Type.ToDisplayString()} {ExplicitInterfaceName(indexer)}.this[{FormatParameters(indexer.Parameters)}] {{ ");
                    if (indexer.GetMethod is not null) code.Append($"get => {thrown}; ");
                    if (indexer.SetMethod is { } indexerSetter) code.Append($"{SetterKeyword(indexerSetter)} => {thrown}; ");
                    code.AppendLine("}");
                    break;
                case IPropertySymbol property:
                    code.Append($"        {SourceSyntax.RefReturnPrefix(property.RefKind)}{property.Type.ToDisplayString()} {ExplicitInterfaceName(property)}.{property.Name} {{ ");
                    if (property.GetMethod is not null) code.Append($"get => {thrown}; ");
                    if (property.SetMethod is { } setter) code.Append($"{SetterKeyword(setter)} => {thrown}; ");
                    code.AppendLine("}");
                    break;
                case IEventSymbol @event:
                    code.AppendLine($"        event {@event.Type.ToDisplayString()} {ExplicitInterfaceName(@event)}.{@event.Name} {{ add => {thrown}; remove => {thrown}; }}");
                    break;
            }
        }
    }

    private static string MemberName(ISymbol member) =>
        member.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    private static string TypeParameters(IMethodSymbol method) =>
        method.IsGenericMethod ? $"<{string.Join(", ", method.TypeParameters.Select(tp => tp.Name))}>" : "";

    /// <summary>Emits the interface's instance members as explicit implementations forwarding to <paramref name="receiver"/>.</summary>
    public static void EmitInstanceMembers(StringBuilder code, INamedTypeSymbol shape, INamedTypeSymbol concreteType, string receiver,
        Compilation compilation, Func<ISymbol, string> explicitInterfaceName)
    {
        // A [DuckShape<>] member is a symbol of the source type, so the interface it is implemented for and
        // the signature it carries both come from the mapping, not from the symbol itself.
        var mapped = MappedShape.TryGet(shape);
        if (mapped is not null)
        {
            var shapeName = shape.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            explicitInterfaceName = _ => shapeName;
        }

        foreach (var member in ShapeMatcher.GetShapeMembers(shape))
        {
            var counterpart = ShapeMatcher.FindCounterpart(member, concreteType, compilation, mapped);
            if (counterpart is null)
                continue;

            EmitMember(code, member, counterpart, Qualify(receiver, concreteType, counterpart), explicitInterfaceName, mapped);
        }
    }

    private static void EmitMember(StringBuilder code, ISymbol member, ISymbol counterpart, string receiver, Func<ISymbol, string> explicitInterfaceName,
        MappedShape? mapped = null)
    {
        switch (member)
        {
            case IMethodSymbol method when counterpart is IMethodSymbol target:
                EmitMethod(code, method, target, receiver, explicitInterfaceName, mapped);
                break;
            case IMethodSymbol method:
                EmitDelegateMethod(code, method, counterpart, receiver, explicitInterfaceName);
                break;
            case IPropertySymbol { IsIndexer: true } indexer:
                EmitIndexer(code, indexer, receiver, explicitInterfaceName, mapped);
                break;
            case IPropertySymbol property:
                EmitProperty(code, property, receiver, explicitInterfaceName, mapped);
                break;
            case IEventSymbol @event:
                EmitEvent(code, @event, receiver, explicitInterfaceName);
                break;
        }
    }

    private static string MemberType(ISymbol member, ITypeSymbol type, MappedShape? mapped) =>
        mapped is null ? type.ToDisplayString() : mapped.MemberTypeName(member, type);

    public static string GetMergeAdapterName(INamedTypeSymbol shape, IEnumerable<INamedTypeSymbol> sources) =>
        $"MergeAdapter_{GeneratedCode.ToIdentifier(shape.ToDisplayString())}_" +
        string.Join("_", sources.Select(s => GeneratedCode.ToIdentifier(s.IsAnonymousType ? AnonymousWitness(s) : s.ToDisplayString())));

    /// <summary>Emits an adapter taking each interface member from the first source that provides it.</summary>
    public static string EmitMerge(INamedTypeSymbol shape, ImmutableArray<INamedTypeSymbol> sources, string adapterName, Compilation compilation)
    {
        var code = new StringBuilder();
        code.AppendLine($"    internal readonly struct {adapterName} : global::{shape.ToDisplayString()}, global::IfItQuacks.IDuckAdapter");
        code.AppendLine("    {");

        for (var i = 0; i < sources.Length; i++)
            code.AppendLine($"        private readonly {(sources[i].IsAnonymousType ? "object" : sources[i].ToDisplayString())} _value{i};");

        var parameters = string.Join(", ", sources.Select((s, i) => $"{(s.IsAnonymousType ? "object" : s.ToDisplayString())} value{i}"));
        var assignments = string.Join(" ", sources.Select((_, i) => $"_value{i} = value{i};"));
        code.AppendLine($"        public {adapterName}({parameters}) {{ {assignments} }}");
        code.AppendLine("        object? global::IfItQuacks.IDuckAdapter.Value => _value0;");

        foreach (var member in ShapeMatcher.GetShapeMembers(shape))
        {
            var source = FindSource(sources, member, compilation);
            if (source is not { } found)
                continue;

            var receiver = sources[found.Index].IsAnonymousType
                ? GeneratedCode.CastByExample($"_value{found.Index}", AnonymousWitness(sources[found.Index]))
                : $"_value{found.Index}";
            receiver = found.Counterpart.ContainingType.TypeKind == TypeKind.Interface
                ? $"((global::{found.Counterpart.ContainingType.ToDisplayString()}){receiver})"
                : Qualify(receiver, sources[found.Index], found.Counterpart);
            EmitMember(code, member, found.Counterpart, receiver, ExplicitInterfaceName);
        }

        EmitMergeIdentityMembers(code, sources, adapterName);

        if (sources.Any(s => s.IsAnonymousType))
            code.AppendLine("        " + GeneratedCode.CastByExampleDeclaration);

        code.AppendLine("    }");
        return code.ToString();
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
    private static void EmitMethod(StringBuilder code, IMethodSymbol method, IMethodSymbol counterpart, string receiver, Func<ISymbol, string> explicitInterfaceName,
        MappedShape? mapped = null)
    {
        var parameters = FormatParameters(method.Parameters);
        // Casting to the matched parameter types keeps overload resolution on the member the shape was matched against.
        var args = string.Join(", ", method.Parameters.Select((p, i) =>
            SymbolEqualityComparer.Default.Equals(p.Type, counterpart.Parameters[i].Type)
                ? SourceSyntax.Argument(p)
                : $"({counterpart.Parameters[i].Type.ToDisplayString()}){SourceSyntax.Identifier(p.Name)}"));
        code.AppendLine($"        {SourceSyntax.RefReturnPrefix(method.RefKind)}{MemberType(method, method.ReturnType, mapped)} {explicitInterfaceName(method)}.{method.Name}({parameters}) => {RefExpressionPrefix(method.RefKind)}{receiver}.{counterpart.Name}({args});");
    }

    // A member holding a delegate implements the interface method through its Invoke.
    private static void EmitDelegateMethod(StringBuilder code, IMethodSymbol method, ISymbol counterpart, string receiver, Func<ISymbol, string> explicitInterfaceName)
    {
        var invoke = ShapeMatcher.DelegateTypeOf(counterpart)!.DelegateInvokeMethod!;
        var args = string.Join(", ", method.Parameters.Select((p, i) =>
            SymbolEqualityComparer.Default.Equals(p.Type, invoke.Parameters[i].Type)
                ? SourceSyntax.Argument(p)
                : $"({invoke.Parameters[i].Type.ToDisplayString()}){SourceSyntax.Identifier(p.Name)}"));
        code.AppendLine($"        {SourceSyntax.RefReturnPrefix(method.RefKind)}{method.ReturnType.ToDisplayString()} {explicitInterfaceName(method)}.{method.Name}({FormatParameters(method.Parameters)}) => " +
                      $"{RefExpressionPrefix(method.RefKind)}{receiver}.{counterpart.Name}({args});");
    }

    private static void EmitProperty(StringBuilder code, IPropertySymbol property, string receiver, Func<ISymbol, string> explicitInterfaceName,
        MappedShape? mapped = null)
    {
        code.Append($"        {SourceSyntax.RefReturnPrefix(property.RefKind)}{MemberType(property, property.Type, mapped)} {explicitInterfaceName(property)}.{property.Name} {{ ");
        if (property.GetMethod is not null) code.Append($"get => {RefExpressionPrefix(property.RefKind)}{receiver}.{property.Name}; ");
        if (property.SetMethod is { } setter && (mapped is null || (mapped.KeepsSetter(property) && !setter.IsInitOnly)))
            code.Append($"{SetterKeyword(setter)} => {receiver}.{property.Name} = value; ");
        code.AppendLine("}");
    }

    private static void EmitIndexer(StringBuilder code, IPropertySymbol indexer, string receiver, Func<ISymbol, string> explicitInterfaceName,
        MappedShape? mapped = null)
    {
        var args = string.Join(", ", indexer.Parameters.Select(SourceSyntax.Argument));
        code.Append($"        {SourceSyntax.RefReturnPrefix(indexer.RefKind)}{MemberType(indexer, indexer.Type, mapped)} {explicitInterfaceName(indexer)}.this[{FormatParameters(indexer.Parameters)}] {{ ");
        if (indexer.GetMethod is not null) code.Append($"get => {RefExpressionPrefix(indexer.RefKind)}{receiver}[{args}]; ");
        if (indexer.SetMethod is { } setter && (mapped is null || (mapped.KeepsSetter(indexer) && !setter.IsInitOnly)))
            code.Append($"{SetterKeyword(setter)} => {receiver}[{args}] = value; ");
        code.AppendLine("}");
    }

    private static void EmitEvent(StringBuilder code, IEventSymbol @event, string receiver, Func<ISymbol, string> explicitInterfaceName) =>
        code.AppendLine($"        event {@event.Type.ToDisplayString()} {explicitInterfaceName(@event)}.{@event.Name} {{ add => {receiver}.{@event.Name} += value; remove => {receiver}.{@event.Name} -= value; }}");

    // Adapters are boxed as the shape, so without forwarding two views of the same instance would neither be equal nor hash alike.
    // Equality is limited to the same adapter type: the original's Equals can't know about adapters, so equating the two would be one-sided.
    public static void EmitIdentityMembers(StringBuilder code, ITypeSymbol concreteType, string adapterName)
    {
        var isReference = concreteType.IsReferenceType;
        code.AppendLine($"        public override bool Equals(object? obj) => obj is {adapterName} other && global::System.Object.Equals(_value, other._value);");
        code.AppendLine($"        public override int GetHashCode() => {(isReference ? "_value?.GetHashCode() ?? 0" : "_value.GetHashCode()")};");
        code.AppendLine($"        public override string ToString() => {(isReference ? "_value?.ToString()" : "_value.ToString()")} ?? string.Empty;");
    }

    // Two merges differing only in a later value would otherwise compare equal.
    private static void EmitMergeIdentityMembers(StringBuilder code, ImmutableArray<INamedTypeSymbol> sources, string adapterName)
    {
        var equalities = string.Join(" && ", sources.Select((_, i) => $"global::System.Object.Equals(_value{i}, other._value{i})"));
        var hashes = sources.Select((s, i) => s.IsReferenceType || s.IsAnonymousType ? $"(_value{i}?.GetHashCode() ?? 0)" : $"_value{i}.GetHashCode()").ToList();
        var hash = hashes.Skip(1).Aggregate(hashes[0], (acc, h) => $"({acc}) * -1521134295 + {h}");
        var isReference = sources[0].IsReferenceType;
        code.AppendLine($"        public override bool Equals(object? obj) => obj is {adapterName} other && {equalities};");
        code.AppendLine($"        public override int GetHashCode() => unchecked({hash});");
        code.AppendLine($"        public override string ToString() => {(isReference ? "_value0?.ToString()" : "_value0.ToString()")} ?? string.Empty;");
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

    public static string ExplicitInterfaceName(ISymbol member) => member.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string AnonymousWitness(INamedTypeSymbol anonymousType) =>
        "new { " + string.Join(", ", anonymousType.GetMembers().OfType<IPropertySymbol>().Select(p =>
            $"{SourceSyntax.Identifier(p.Name)} = {(p.Type is INamedTypeSymbol { IsAnonymousType: true } nested ? AnonymousWitness(nested) : $"default({p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})!")}")) + " }";

    private static string FormatParameters(IEnumerable<IParameterSymbol> parameters) =>
        string.Join(", ", parameters.Select(p => SourceSyntax.Parameter(p, p.Type.ToDisplayString())));

}
