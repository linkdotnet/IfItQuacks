using System.Text;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

internal static class AdapterEmitter
{
    public static string GetAdapterName(INamedTypeSymbol shape, INamedTypeSymbol concreteType) =>
        $"ShapeAdapter_{Sanitize(shape.ToDisplayString())}_{Sanitize(concreteType.ToDisplayString())}";

    public static string Emit(INamedTypeSymbol shape, INamedTypeSymbol concreteType, string adapterName)
    {
        var sb = new StringBuilder();
        var concreteTypeName = concreteType.ToDisplayString();

        sb.AppendLine($"    internal readonly struct {adapterName} : global::{shape.ToDisplayString()}");
        sb.AppendLine("    {");
        sb.AppendLine($"        private readonly {concreteTypeName} _value;");
        sb.AppendLine($"        public {adapterName}({concreteTypeName} value) => _value = value;");

        foreach (var member in ShapeMatcher.GetShapeMembers(shape))
        {
            if (!ShapeMatcher.IsRequired(member) && !ShapeMatcher.IsProvidedBy(member, concreteType))
                continue;

            switch (member)
            {
                case IMethodSymbol method:
                    EmitMethod(sb, method);
                    break;
                case IPropertySymbol { IsIndexer: true } indexer:
                    EmitIndexer(sb, indexer);
                    break;
                case IPropertySymbol property:
                    EmitProperty(sb, property);
                    break;
                case IEventSymbol @event:
                    EmitEvent(sb, @event);
                    break;
            }
        }

        sb.AppendLine("    }");
        return sb.ToString();
    }

    private static void EmitMethod(StringBuilder sb, IMethodSymbol method)
    {
        var parameters = FormatParameters(method.Parameters);
        var args = string.Join(", ", method.Parameters.Select(Utilities.Argument));
        sb.AppendLine($"        public {method.ReturnType.ToDisplayString()} {method.Name}({parameters}) => _value.{method.Name}({args});");
    }

    private static void EmitProperty(StringBuilder sb, IPropertySymbol property)
    {
        sb.Append($"        public {property.Type.ToDisplayString()} {property.Name} {{ ");
        if (property.GetMethod is not null) sb.Append($"get => _value.{property.Name}; ");
        if (property.SetMethod is not null) sb.Append($"set => _value.{property.Name} = value; ");
        sb.AppendLine("}");
    }

    private static void EmitIndexer(StringBuilder sb, IPropertySymbol indexer)
    {
        var args = string.Join(", ", indexer.Parameters.Select(Utilities.Argument));
        sb.Append($"        public {indexer.Type.ToDisplayString()} this[{FormatParameters(indexer.Parameters)}] {{ ");
        if (indexer.GetMethod is not null) sb.Append($"get => _value[{args}]; ");
        if (indexer.SetMethod is not null) sb.Append($"set => _value[{args}] = value; ");
        sb.AppendLine("}");
    }

    private static void EmitEvent(StringBuilder sb, IEventSymbol @event) =>
        sb.AppendLine($"        public event {@event.Type.ToDisplayString()} {@event.Name} {{ add => _value.{@event.Name} += value; remove => _value.{@event.Name} -= value; }}");

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
