using System;
using System.Text;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

/// <summary>
/// Emits an adapter for a duck-typed generic constraint, e.g. <c>where T : IAddable&lt;T&gt;</c>.
/// The adapter is its own type argument (<c>Adapter : IAddable&lt;Adapter&gt;</c>), so the interface's
/// <c>static abstract</c> members and operators can forward to the wrapped type's statics.
/// </summary>
internal static class SelfShapeAdapterEmitter
{
    public static string Emit(INamedTypeSymbol shape, INamedTypeSymbol concreteType, string adapterName, Compilation compilation)
    {
        var concrete = FullName(concreteType);
        // Only a self-referencing shape (IAddable<Money>) mentions the concrete type; for any other shape a
        // name merely starting with it (N.Person vs N.PersonShape) must be left alone.
        var isSelfReferential = Mentions(shape, concreteType);
        string Self(string text) => isSelfReferential ? ReplaceType(text, concrete, adapterName) : text;

        var sb = new StringBuilder();
        sb.AppendLine($"    internal readonly struct {adapterName} : {Self(FullName(shape))}, global::IfItQuacks.IDuckAdapter");
        sb.AppendLine("    {");
        sb.AppendLine($"        private readonly {concrete} _value;");
        sb.AppendLine($"        public {adapterName}({concrete} value) => _value = value;");
        sb.AppendLine("        object? global::IfItQuacks.IDuckAdapter.Value => _value;");
        sb.AppendLine($"        public static explicit operator {concrete}({adapterName} adapter) => adapter._value;");

        foreach (var member in StaticShapeMatcher.GetStaticMembers(shape))
        {
            if (StaticShapeMatcher.FindCounterpart(member, concreteType, compilation) is null)
                continue;

            switch (member)
            {
                case IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } op:
                    EmitOperator(sb, op, concreteType, adapterName, Self);
                    break;
                case IMethodSymbol method:
                    EmitStaticMethod(sb, method, concreteType, adapterName, concrete, Self);
                    break;
                case IPropertySymbol property:
                    EmitStaticProperty(sb, property, concreteType, adapterName, concrete, Self);
                    break;
            }
        }

        AdapterEmitter.EmitInstanceMembers(sb, shape, concreteType, "_value", compilation,
            member => Self(AdapterEmitter.Owner(member)));
        AdapterEmitter.EmitIdentityMembers(sb, concreteType, adapterName);

        sb.AppendLine("    }");
        return sb.ToString();
    }

    private static void EmitOperator(StringBuilder sb, IMethodSymbol op, INamedTypeSymbol concreteType, string adapterName,
        Func<string, string> self)
    {
        var token = StaticShapeMatcher.OperatorToken(op)!;
        var parameters = string.Join(", ", op.Parameters.Select(p => $"{self(FullName(p.Type))} {Utilities.Identifier(p.Name)}"));
        var arguments = op.Parameters.Select(p => Unwrap(p, concreteType)).ToList();
        var returnType = self(FullName(op.ReturnType));
        var wraps = IsSelf(op.ReturnType, concreteType);

        // Increment and decrement have to go through a local, because the wrapped value is readonly.
        if (token is "++" or "--")
        {
            var inner = Utilities.Identifier(op.Parameters[0].Name);
            sb.AppendLine($"        public static {returnType} operator {token}({parameters})");
            sb.AppendLine("        {");
            sb.AppendLine($"            var __inner = {inner}._value;");
            sb.AppendLine($"            __inner{token};");
            sb.AppendLine($"            return {Wrap("__inner", wraps, adapterName)};");
            sb.AppendLine("        }");
            return;
        }

        var expression = arguments.Count == 1 ? $"{token}{arguments[0]}" : $"{arguments[0]} {token} {arguments[1]}";
        sb.AppendLine($"        public static {returnType} operator {token}({parameters}) => {Wrap(expression, wraps, adapterName)};");
    }

    private static void EmitStaticMethod(StringBuilder sb, IMethodSymbol method, INamedTypeSymbol concreteType, string adapterName,
        string concrete, Func<string, string> self)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{self(FullName(p.Type))} {Utilities.Identifier(p.Name)}"));
        var arguments = string.Join(", ", method.Parameters.Select(p => Unwrap(p, concreteType)));
        var call = $"{concrete}.{method.Name}({arguments})";
        sb.AppendLine($"        public static {self(FullName(method.ReturnType))} {method.Name}({parameters}) => " +
                      $"{Wrap(call, IsSelf(method.ReturnType, concreteType), adapterName)};");
    }

    private static void EmitStaticProperty(StringBuilder sb, IPropertySymbol property, INamedTypeSymbol concreteType, string adapterName,
        string concrete, Func<string, string> self)
    {
        sb.Append($"        public static {self(FullName(property.Type))} {property.Name} {{ ");
        if (property.GetMethod is not null)
            sb.Append($"get => {Wrap($"{concrete}.{property.Name}", IsSelf(property.Type, concreteType), adapterName)}; ");
        if (property.SetMethod is not null)
            sb.Append($"set => {concrete}.{property.Name} = {(IsSelf(property.Type, concreteType) ? "value._value" : "value")}; ");
        sb.AppendLine("}");
    }

    private static string Unwrap(IParameterSymbol parameter, INamedTypeSymbol concreteType) =>
        IsSelf(parameter.Type, concreteType)
            ? $"{Utilities.Identifier(parameter.Name)}._value"
            : Utilities.Identifier(parameter.Name);

    private static string Wrap(string expression, bool wraps, string adapterName) =>
        wraps ? $"new {adapterName}({expression})" : expression;

    private static bool IsSelf(ITypeSymbol type, INamedTypeSymbol concreteType) =>
        SymbolEqualityComparer.Default.Equals(type, concreteType);

    private static string FullName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static bool Mentions(ITypeSymbol type, INamedTypeSymbol self) =>
        SymbolEqualityComparer.Default.Equals(type, self) ||
        (type is INamedTypeSymbol named && named.TypeArguments.Any(t => Mentions(t, self))) ||
        (type is IArrayTypeSymbol array && Mentions(array.ElementType, self));

    /// <summary>Replaces <paramref name="concrete"/> only where it is a whole type name, not a prefix of a longer one.</summary>
    private static string ReplaceType(string text, string concrete, string adapterName)
    {
        var sb = new StringBuilder(text.Length);
        var start = 0;

        while (true)
        {
            var index = text.IndexOf(concrete, start, StringComparison.Ordinal);
            if (index < 0)
            {
                sb.Append(text, start, text.Length - start);
                return sb.ToString();
            }

            var after = index + concrete.Length;
            var continues = after < text.Length && (char.IsLetterOrDigit(text[after]) || text[after] is '_' or '.');
            sb.Append(text, start, index - start);
            sb.Append(continues ? concrete : adapterName);
            start = after;
        }
    }
}
