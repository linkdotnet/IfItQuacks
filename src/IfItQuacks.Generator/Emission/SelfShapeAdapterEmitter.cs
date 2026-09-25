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
        var concrete = FullyQualifiedName(concreteType);
        // Only a self-referencing shape (IAddable<Money>) mentions the concrete type; for any other shape a
        // name merely starting with it (N.Person vs N.PersonShape) must be left alone.
        var isSelfReferential = shape.Mentions(concreteType);
        string Self(string text) => isSelfReferential ? ReplaceType(text, concrete, adapterName) : text;

        var code = new StringBuilder();
        code.AppendLine($"    internal readonly struct {adapterName} : {Self(FullyQualifiedName(shape))}, global::IfItQuacks.IDuckAdapter");
        code.AppendLine("    {");
        code.AppendLine($"        private readonly {concrete} _value;");
        code.AppendLine($"        public {adapterName}({concrete} value) => _value = value;");
        code.AppendLine("        object? global::IfItQuacks.IDuckAdapter.Value => _value;");
        code.AppendLine($"        public static explicit operator {concrete}({adapterName} adapter) => adapter._value;");

        foreach (var member in StaticShapeMatcher.GetStaticMembers(shape))
        {
            if (StaticShapeMatcher.FindCounterpart(member, concreteType, compilation) is null)
                continue;

            switch (member)
            {
                case IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } op:
                    EmitOperator(code, op, concreteType, adapterName, Self);
                    break;
                case IMethodSymbol method:
                    EmitStaticMethod(code, method, concreteType, adapterName, concrete, Self);
                    break;
                case IPropertySymbol property:
                    EmitStaticProperty(code, property, concreteType, adapterName, concrete, Self);
                    break;
            }
        }

        AdapterEmitter.EmitInstanceMembers(code, shape, concreteType, "_value", compilation,
            member => Self(AdapterEmitter.ExplicitInterfaceName(member)));
        AdapterEmitter.EmitIdentityMembers(code, concreteType, adapterName);

        code.AppendLine("    }");
        return code.ToString();
    }

    private static void EmitOperator(StringBuilder code, IMethodSymbol op, INamedTypeSymbol concreteType, string adapterName,
        Func<string, string> self)
    {
        var token = StaticShapeMatcher.OperatorToken(op)!;
        var parameters = string.Join(", ", op.Parameters.Select(p => $"{self(FullyQualifiedName(p.Type))} {SourceSyntax.Identifier(p.Name)}"));
        var arguments = op.Parameters.Select(p => UnwrapAdapter(p, concreteType)).ToList();
        var returnType = self(FullyQualifiedName(op.ReturnType));
        var wraps = IsAdaptedType(op.ReturnType, concreteType);

        // Increment and decrement have to go through a local, because the wrapped value is readonly.
        if (token is "++" or "--")
        {
            var inner = SourceSyntax.Identifier(op.Parameters[0].Name);
            code.AppendLine($"        public static {returnType} operator {token}({parameters})");
            code.AppendLine("        {");
            code.AppendLine($"            var __inner = {inner}._value;");
            code.AppendLine($"            __inner{token};");
            code.AppendLine($"            return {WrapInAdapter("__inner", wraps, adapterName)};");
            code.AppendLine("        }");
            return;
        }

        var expression = arguments.Count == 1 ? $"{token}{arguments[0]}" : $"{arguments[0]} {token} {arguments[1]}";
        code.AppendLine($"        public static {returnType} operator {token}({parameters}) => {WrapInAdapter(expression, wraps, adapterName)};");
    }

    private static void EmitStaticMethod(StringBuilder code, IMethodSymbol method, INamedTypeSymbol concreteType, string adapterName,
        string concrete, Func<string, string> self)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{self(FullyQualifiedName(p.Type))} {SourceSyntax.Identifier(p.Name)}"));
        var arguments = string.Join(", ", method.Parameters.Select(p => UnwrapAdapter(p, concreteType)));
        var call = $"{concrete}.{method.Name}({arguments})";
        code.AppendLine($"        public static {self(FullyQualifiedName(method.ReturnType))} {method.Name}({parameters}) => " +
                      $"{WrapInAdapter(call, IsAdaptedType(method.ReturnType, concreteType), adapterName)};");
    }

    private static void EmitStaticProperty(StringBuilder code, IPropertySymbol property, INamedTypeSymbol concreteType, string adapterName,
        string concrete, Func<string, string> self)
    {
        code.Append($"        public static {self(FullyQualifiedName(property.Type))} {property.Name} {{ ");
        if (property.GetMethod is not null)
            code.Append($"get => {WrapInAdapter($"{concrete}.{property.Name}", IsAdaptedType(property.Type, concreteType), adapterName)}; ");
        if (property.SetMethod is not null)
            code.Append($"set => {concrete}.{property.Name} = {(IsAdaptedType(property.Type, concreteType) ? "value._value" : "value")}; ");
        code.AppendLine("}");
    }

    private static string UnwrapAdapter(IParameterSymbol parameter, INamedTypeSymbol concreteType) =>
        IsAdaptedType(parameter.Type, concreteType)
            ? $"{SourceSyntax.Identifier(parameter.Name)}._value"
            : SourceSyntax.Identifier(parameter.Name);

    private static string WrapInAdapter(string expression, bool wraps, string adapterName) =>
        wraps ? $"new {adapterName}({expression})" : expression;

    private static bool IsAdaptedType(ITypeSymbol type, INamedTypeSymbol concreteType) =>
        SymbolEqualityComparer.Default.Equals(type, concreteType);

    private static string FullyQualifiedName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>Replaces <paramref name="concrete"/> only where it is a whole type name, not a prefix of a longer one.</summary>
    private static string ReplaceType(string text, string concrete, string adapterName)
    {
        var code = new StringBuilder(text.Length);
        var start = 0;

        while (true)
        {
            var index = text.IndexOf(concrete, start, StringComparison.Ordinal);
            if (index < 0)
            {
                code.Append(text, start, text.Length - start);
                return code.ToString();
            }

            var after = index + concrete.Length;
            var continues = after < text.Length && (char.IsLetterOrDigit(text[after]) || text[after] is '_' or '.');
            code.Append(text, start, index - start);
            code.Append(continues ? concrete : adapterName);
            start = after;
        }
    }
}
