using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IfItQuacks.Generator;

/// <summary>
/// Redirects a call like 'Greet(new Person())' that binds to an overload like 'Greet(object)' to the <c>[DuckTyped]</c> method,
/// when C# would pick that method if Person implemented the interface.
/// </summary>
internal static class OverloadRedirectAnalysis
{
    public static IMethodSymbol? FindDuckTypedOverload(IMethodSymbol bound, DuckTypedMethodIndex duckMethods) =>
        bound is { MethodKind: MethodKind.Ordinary, IsGenericMethod: false } && !KnownSymbols.IsDuckTyped(bound)
            ? bound.ContainingType.GetMembers(bound.Name).OfType<IMethodSymbol>().FirstOrDefault(m =>
                m is { IsGenericMethod: false, DeclaringSyntaxReferences.Length: > 0 } && KnownSymbols.IsDuckTyped(m) && duckMethods.Contains(m))
            : null;

    public static CallSiteOutput? Analyze(SemanticModel semanticModel, InvocationExpressionSyntax invocation,
        IMethodSymbol overload, IMethodSymbol duckMethod, CancellationToken ct)
    {
        if (InvocationSyntax.IsBaseCall(invocation) || !CanReplace(overload, duckMethod) ||
            InvocationSyntax.MapArgumentsToParameters(invocation, duckMethod, isExtensionCall: false) is not { } arguments ||
            InvocationSyntax.MapArgumentsToParameters(invocation, overload, isExtensionCall: false) is not { } overloadArguments)
            return null;

        var compilation = semanticModel.Compilation;
        var overloadParameters = overloadArguments.ToDictionary(a => a.Value, a => overload.Parameters[a.Key]);
        var duckParameters = DuckTypedSignature.GetDuckParameters(duckMethod);
        var toAdapt = new List<DuckArgument>();
        foreach (var argument in arguments)
        {
            var parameter = duckMethod.Parameters[argument.Key];
            var expression = argument.Value;
            var overloadParameter = overloadParameters[expression];
            if (!duckParameters.Contains(parameter, SymbolEqualityComparer.Default))
            {
                if (!SymbolEqualityComparer.Default.Equals(parameter.Type, overloadParameter.Type) || parameter.RefKind != overloadParameter.RefKind)
                    return null;
                continue;
            }

            var argumentType = ArgumentType.Of(semanticModel, expression, ct);
            var shape = ShapeOf(parameter);
            if (argumentType is null || compilation.HasBuiltInImplicitConversion(argumentType, shape))
                continue;

            if (!CanRedirect(expression, argumentType, shape, overloadParameter, compilation))
                return null;

            toAdapt.Add(new DuckArgument(parameter, expression, argumentType));
        }

        if (toAdapt.Count == 0 || !BindsToDuckTypedMethodOnceAdapted(semanticModel, invocation, toAdapt, duckMethod) ||
            semanticModel.GetInterceptableLocation(invocation, ct) is not { } location)
            return null;

        var adapters = new GeneratedAdapters();
        var adapted = toAdapt.ToDictionary(a => a.Parameter.Ordinal,
            a => new ResolvedArgument(a.ConcreteType, adapters.Add(AdapterFactory.Create(ShapeOf(a.Parameter), a.ConcreteType, compilation))));

        var callArguments = arguments.OrderBy(a => a.Key).Select(a =>
            $"{SourceSyntax.Identifier(duckMethod.Parameters[a.Key].Name)}: " +
            DuckMethodArgument(duckMethod.Parameters[a.Key], overloadParameters[a.Value], adapted, duckParameters));

        return adapters.WithInterceptor(InterceptorEmitter.ForRedirect(location, overload, duckMethod, callArguments));
    }

    private static bool CanReplace(IMethodSymbol overload, IMethodSymbol duckMethod) =>
        overload.IsStatic == duckMethod.IsStatic && (!overload.IsReadOnly || duckMethod.IsReadOnly) &&
        overload.RefKind == RefKind.None && duckMethod.RefKind == RefKind.None &&
        SymbolEqualityComparer.Default.Equals(overload.ReturnType, duckMethod.ReturnType) &&
        !overload.Parameters.Any(p => p.IsParams) && !duckMethod.Parameters.Any(p => p.IsParams);

    private static bool CanRedirect(ExpressionSyntax expression, ITypeSymbol argumentType, INamedTypeSymbol shape, IParameterSymbol overloadParameter,
        Compilation compilation) =>
        argumentType.TypeKind != TypeKind.Error && !argumentType.IsAnonymousType && TypeVisibility.IsNameable(argumentType) &&
        !argumentType.ContainsAnyTypeParameter() &&
        IsMoreSpecific(shape, overloadParameter.Type, compilation) &&
        ArgumentVerifier.Verify(expression, shape, argumentType, compilation, out _) is null;

    private static bool IsMoreSpecific(ITypeSymbol type, ITypeSymbol than, Compilation compilation) =>
        compilation.HasBuiltInImplicitConversion(type, than) && !compilation.ClassifyCommonConversion(than, type).IsImplicit;

    private static bool BindsToDuckTypedMethodOnceAdapted(SemanticModel semanticModel, InvocationExpressionSyntax invocation,
        List<DuckArgument> toAdapt, IMethodSymbol duckMethod)
    {
        var shapes = toAdapt.ToDictionary(a => a.Expression, a => ShapeOf(a.Parameter));
        var speculative = invocation.ReplaceNodes(shapes.Keys, (original, _) =>
            SyntaxFactory.ParseExpression($"default({shapes[original].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})"));
        return semanticModel.GetSpeculativeSymbolInfo(invocation.SpanStart, speculative, SpeculativeBindingOption.BindAsExpression).Symbol is IMethodSymbol picked &&
               SymbolEqualityComparer.Default.Equals(picked.OriginalDefinition, duckMethod);
    }

    private static string DuckMethodArgument(IParameterSymbol parameter, IParameterSymbol overloadParameter,
        Dictionary<int, ResolvedArgument> adapted, ImmutableArray<IParameterSymbol> duckParameters)
    {
        var value = SourceSyntax.Identifier(overloadParameter.Name);
        var shape = $"global::{parameter.Type.WithoutNullability().ToDisplayString()}";
        if (adapted.TryGetValue(parameter.Ordinal, out var argument))
            return $"({shape})(new {argument.AdapterReference}(({argument.ConcreteType.ToDisplayString()})(object){value}!))";

        return duckParameters.Contains(parameter, SymbolEqualityComparer.Default) ? $"({shape})(object){value}!" : SourceSyntax.Argument(overloadParameter);
    }

    private static INamedTypeSymbol ShapeOf(IParameterSymbol parameter) => (INamedTypeSymbol)parameter.Type.WithoutNullability();
}
