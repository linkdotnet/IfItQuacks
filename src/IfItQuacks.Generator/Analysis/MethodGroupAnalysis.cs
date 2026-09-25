using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IfItQuacks.Generator;

/// <summary>
/// A method group like 'Ops.Describe' in 'items.Select(Ops.Describe)' can't be intercepted, so a concrete overload is
/// generated for the delegate it converts to.
/// </summary>
internal static class MethodGroupAnalysis
{
    public static ExpressionSyntax? GetMethodGroupExpression(SyntaxNode node)
    {
        if (node is not SimpleNameSyntax name || name.Parent is null)
            return null;

        var expression = name.Parent is MemberAccessExpressionSyntax member && member.Name == name ? (ExpressionSyntax)member : name;
        if (expression.Parent is null)
            return null;

        return expression.Parent switch
        {
            InvocationExpressionSyntax invocation when invocation.Expression == expression => null,
            MemberAccessExpressionSyntax or MemberBindingExpressionSyntax or NameSyntax or TypeSyntax => null,
            _ when CannotHoldMethodGroup(expression.Parent) => null,
            _ => expression,
        };
    }

    public static string GetName(ExpressionSyntax methodGroup) => methodGroup switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        SimpleNameSyntax name => name.Identifier.Text,
        _ => string.Empty,
    };

    public static CallSiteOutput? Analyze(ExpressionSyntax methodGroup, DuckTypedMethodIndex duckMethods, Compilation compilation, CancellationToken ct)
    {
        if (!compilation.ContainsSyntaxTree(methodGroup.SyntaxTree))
            return null;

        var semanticModel = compilation.GetSemanticModel(methodGroup.SyntaxTree);
        var symbolInfo = semanticModel.GetSymbolInfo(methodGroup, ct);
        var duckMethod = (symbolInfo.Symbol as IMethodSymbol ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault())?.OriginalDefinition;
        if (duckMethod is null || duckMethod.IsGenericMethod || duckMethod.DeclaringSyntaxReferences.Length == 0 || !duckMethods.Contains(duckMethod))
            return null;

        if (semanticModel.GetTypeInfo(methodGroup, ct).ConvertedType is not INamedTypeSymbol { DelegateInvokeMethod: { } invoke } ||
            invoke.Parameters.Length != duckMethod.Parameters.Length)
            return null;

        var adapters = new GeneratedAdapters();
        var adapted = new Dictionary<int, ResolvedArgument>();
        foreach (var parameter in DuckTypedSignature.GetDuckParameters(duckMethod))
        {
            if (invoke.Parameters[parameter.Ordinal].Type is not INamedTypeSymbol delegateParameterType ||
                !NeedsAdapter(methodGroup, delegateParameterType, parameter, compilation))
                continue;

            var shape = (INamedTypeSymbol)parameter.Type.WithoutNullability();
            adapted[parameter.Ordinal] = new ResolvedArgument(delegateParameterType, adapters.Add(AdapterFactory.Create(shape, delegateParameterType, compilation)));
        }

        return adapted.Count == 0 ? null : adapters.WithOverload(OverloadEmitter.ForDuckTypedCall(duckMethod, duckMethod, adapted));
    }

    private static bool NeedsAdapter(ExpressionSyntax methodGroup, INamedTypeSymbol delegateParameterType, IParameterSymbol parameter, Compilation compilation) =>
        delegateParameterType.TypeKind != TypeKind.Error && !delegateParameterType.ContainsAnyTypeParameter() && TypeVisibility.IsNameable(delegateParameterType) &&
        !SymbolEqualityComparer.Default.Equals(delegateParameterType, parameter.Type) &&
        ArgumentVerifier.Verify(methodGroup, (INamedTypeSymbol)parameter.Type.WithoutNullability(), delegateParameterType, compilation, out var implementsDirectly) is null &&
        !implementsDirectly;

    // Delegate combination and '??' are the only operators taking a method group.
    private static bool CannotHoldMethodGroup(SyntaxNode parent) => parent switch
    {
        BinaryExpressionSyntax binary => !binary.IsKind(SyntaxKind.AddExpression) && !binary.IsKind(SyntaxKind.SubtractExpression) &&
                                         !binary.IsKind(SyntaxKind.CoalesceExpression),
        PostfixUnaryExpressionSyntax postfix => !postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression),
        PrefixUnaryExpressionSyntax or ElementAccessExpressionSyntax or ConditionalAccessExpressionSyntax or InterpolationSyntax or
            IsPatternExpressionSyntax or IfStatementSyntax or WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or
            SwitchStatementSyntax or SwitchExpressionSyntax or ThrowStatementSyntax or ThrowExpressionSyntax or AwaitExpressionSyntax or
            LockStatementSyntax or UsingStatementSyntax or ForEachStatementSyntax or ExpressionStatementSyntax => true,
        _ => false,
    };
}
