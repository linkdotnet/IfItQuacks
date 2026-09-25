using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace IfItQuacks.Generator;

/// <summary>Emits the interceptor methods that replace verified calls. <see cref="CallSiteSources"/> numbers and collects them.</summary>
internal static class InterceptorEmitter
{
    public static string ForDuckTypedCall(InterceptableLocation location, IMethodSymbol method,
        IReadOnlyDictionary<int, ResolvedArgument> adaptedArguments, bool isExtensionCall)
    {
        // Anonymous types can't be named in the signature, so the interceptor stays generic like the fallback it replaces.
        var isGeneric = adaptedArguments.Values.Any(a => !CanBeNamed(a.ConcreteType));
        // A call on a receiver is intercepted by an extension method, whatever the intercepted method is declared as.
        var parameters = method.Parameters.Select(p =>
            (isExtensionCall && p.Ordinal == 0 ? "this " : "") + SourceSyntax.Parameter(p, ParameterType(p, adaptedArguments, isGeneric)));

        // Casting to the shape interface makes overload resolution pick the real method instead of the generated generic fallback.
        var arguments = method.Parameters.Select(p => adaptedArguments.TryGetValue(p.Ordinal, out var adapted)
            ? $"(global::{p.Type.WithoutNullability().ToDisplayString()})({AdaptedValue(p, adapted, isGeneric)})"
            : SourceSyntax.Argument(p));

        var typeParameters = isGeneric
            ? $"<{string.Join(", ", adaptedArguments.Keys.OrderBy(o => o).Select(o => FallbackSignature.TypeParameterName(method.Parameters[o])))}>"
            : "";
        return Interceptor(location, method, typeParameters, WithReceiver(method, parameters), arguments);
    }

    // Keeps the intercepted overload's signature, but calls the [DuckTyped] method with named arguments, so its own defaults apply.
    public static string ForRedirect(InterceptableLocation location, IMethodSymbol overload, IMethodSymbol duckMethod, IEnumerable<string> arguments) =>
        Interceptor(location, duckMethod, "", WithReceiver(overload, overload.Parameters.Select(p => SourceSyntax.Parameter(p, p.Type.ToDisplayString()))), arguments);

    public static string ForConversion(InterceptableLocation location, string returnType, string parameters, string body) =>
        $"        {location.GetInterceptsLocationAttributeSyntax()}\n" +
        $"        public static {returnType} Interceptor_{GeneratedCode.InterceptorIndexPlaceholder}({parameters}) => {body};\n";

    private static string ParameterType(IParameterSymbol parameter, IReadOnlyDictionary<int, ResolvedArgument> adaptedArguments, bool isGeneric)
    {
        if (!adaptedArguments.TryGetValue(parameter.Ordinal, out var adapted))
            return parameter.Type.ToDisplayString();

        return isGeneric ? FallbackSignature.TypeParameterName(parameter) : adapted.ConcreteType.ToDisplayString();
    }

    private static string AdaptedValue(IParameterSymbol parameter, ResolvedArgument argument, bool isGeneric)
    {
        var value = SourceSyntax.Identifier(parameter.Name);
        if (isGeneric)
            value = CanBeNamed(argument.ConcreteType) ? $"({argument.ConcreteType.ToDisplayString()})(object){value}!" : $"(object){value}!";
        return argument.AdapterReference is null ? value : $"new {argument.AdapterReference}({value})";
    }

    private static bool CanBeNamed(ITypeSymbol type) => !type.IsAnonymousType && TypeVisibility.IsNameable(type);

    private static IEnumerable<string> WithReceiver(IMethodSymbol method, IEnumerable<string> parameters)
    {
        if (method.IsStatic)
            return parameters;

        return parameters.Prepend($"this {ReceiverRefKind(method)}global::{method.ContainingType.ToDisplayString()} @this");
    }

    // Struct receivers are passed by reference, so mutations made by the method reach the caller's value.
    private static string ReceiverRefKind(IMethodSymbol method) => method switch
    {
        { ContainingType.IsValueType: false } => "",
        { IsReadOnly: true } => "in ",
        _ => "ref ",
    };

    private static string Interceptor(InterceptableLocation location, IMethodSymbol method, string typeParameters,
        IEnumerable<string> parameters, IEnumerable<string> arguments)
    {
        var receiver = method.IsStatic ? $"global::{method.ContainingType.ToDisplayString()}" : "@this";
        var call = $"{FallbackOverloadEmitter.ThroughVirtualSlot(method, receiver)}.{FallbackOverloadEmitter.AccessibleName(method)}({string.Join(", ", arguments)});";

        var source = new StringBuilder();
        source.AppendLine("        " + location.GetInterceptsLocationAttributeSyntax());
        source.AppendLine($"        public static {method.ReturnType.ToDisplayString()} Interceptor_{GeneratedCode.InterceptorIndexPlaceholder}{typeParameters}({string.Join(", ", parameters)})");
        source.AppendLine("        {");
        source.AppendLine(method.ReturnsVoid ? $"            {call}" : $"            return {call}");
        source.AppendLine("        }");
        return source.ToString();
    }
}
