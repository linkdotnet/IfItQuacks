using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IfItQuacks.Generator;

internal static class InvocationSyntax
{
    public static string? GetInvokedName(InvocationExpressionSyntax invocation) => GetInvokedNameSyntax(invocation)?.Identifier.Text;

    // The type argument of a Duck conversion can't be inferred from its 'object' parameters, so it is always written out.
    public static bool CanBeDuckConversion(SimpleNameSyntax invokedName) =>
        invokedName is GenericNameSyntax
        {
            Identifier.Text: KnownSymbols.DuckAsMethod or KnownSymbols.DuckStubMethod or KnownSymbols.DuckMergeMethod or KnownSymbols.DuckToMethod,
            TypeArgumentList.Arguments.Count: 1,
        };

    public static ExpressionSyntax? GetReceiver(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Expression,
        MemberBindingExpressionSyntax => invocation.Ancestors().OfType<ConditionalAccessExpressionSyntax>().FirstOrDefault()?.Expression,
        _ => null,
    };

    public static bool IsBaseCall(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax { Expression: BaseExpressionSyntax };

    public static Dictionary<int, ExpressionSyntax>? MapArgumentsToParameters(InvocationExpressionSyntax invocation, IMethodSymbol method, bool isExtensionCall)
    {
        var firstArgumentOrdinal = isExtensionCall ? 1 : 0;
        var arguments = new Dictionary<int, ExpressionSyntax>();
        var argumentList = invocation.ArgumentList.Arguments;
        for (var i = 0; i < argumentList.Count; i++)
        {
            var argument = argumentList[i];
            IParameterSymbol? parameter;
            if (argument.NameColon is { } nameColon)
                parameter = method.Parameters.FirstOrDefault(p => p.Name == nameColon.Name.Identifier.ValueText);
            else if (i + firstArgumentOrdinal < method.Parameters.Length)
                parameter = method.Parameters[i + firstArgumentOrdinal];
            else
                parameter = method.Parameters.LastOrDefault(p => p.IsParams);

            if (parameter is null)
                return null;

            arguments[parameter.Ordinal] = argument.Expression;
        }

        if (isExtensionCall)
        {
            if (GetReceiver(invocation) is not { } receiver)
                return null;
            arguments[0] = receiver;
        }

        return arguments;
    }

    public static SimpleNameSyntax? GetInvokedNameSyntax(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        SimpleNameSyntax name => name,
        MemberAccessExpressionSyntax member => member.Name,
        // 'receiver?.Greet(duck)': the conditional access holds the receiver, the invocation only the member name.
        MemberBindingExpressionSyntax binding => binding.Name,
        _ => null,
    };
}
