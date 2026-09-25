using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IfItQuacks.Generator;

/// <summary>Decides what a call needs generated: an interceptor, a concrete overload, a diagnostic or nothing.</summary>
internal static class CallSiteAnalysis
{
    private sealed record ArgumentPlan(List<DuckArgument> ToAdapt, Dictionary<int, ResolvedArgument> Resolved);

    public static CallSiteOutput? Analyze(InvocationExpressionSyntax invocation, DuckTypedMethodIndex duckMethods, Compilation compilation, CancellationToken ct)
    {
        if (!compilation.ContainsSyntaxTree(invocation.SyntaxTree))
            return null;

        var semanticModel = compilation.GetSemanticModel(invocation.SyntaxTree);
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
        ImmutableArray<IMethodSymbol> candidates = symbolInfo.Symbol is IMethodSymbol bound
            ? [bound]
            : [.. symbolInfo.CandidateSymbols.OfType<IMethodSymbol>()];

        if (candidates.FirstOrDefault(c => KnownSymbols.IsDuckConversion(c, compilation)) is { } conversion)
            return DuckConversionAnalysis.Analyze(semanticModel, invocation, conversion, ct);

        if (DuckTypedMethodLookup.Find(semanticModel, invocation, candidates, duckMethods, ct) is not { } target)
        {
            return symbolInfo.Symbol is IMethodSymbol overload && OverloadRedirectAnalysis.FindDuckTypedOverload(overload, duckMethods) is { } duckMethod
                ? OverloadRedirectAnalysis.Analyze(semanticModel, invocation, overload, duckMethod, ct)
                : null;
        }

        var bindsWithoutFallback = !target.Method.IsGenericMethod && symbolInfo.Symbol is not null && KnownSymbols.SupportsOverloadPriority(compilation);
        if (!duckMethods.Contains(target.Method) || bindsWithoutFallback ||
            InvocationSyntax.MapArgumentsToParameters(invocation, target.Method, target.IsExtensionCall) is not { } arguments)
            return null;

        if (DuckTypedSignature.GetDuckConstraintTypeParameters(target.Method) is { IsEmpty: false } constraintTypeParameters)
            return ConstraintCallAnalysis.Analyze(semanticModel, target.Method, constraintTypeParameters, arguments, ct);

        return AnalyzeDuckTypedCall(semanticModel, invocation, target, arguments, ct);
    }

    private static CallSiteOutput? AnalyzeDuckTypedCall(SemanticModel semanticModel, InvocationExpressionSyntax invocation, CallTarget target,
        Dictionary<int, ExpressionSyntax> arguments, CancellationToken ct)
    {
        var duckMethod = target.Method;
        if (PlanArguments(semanticModel, duckMethod, arguments, ct) is not { } plan || (plan.ToAdapt.Count == 0 && plan.Resolved.Count == 0))
            return null;

        if (InvocationSyntax.IsBaseCall(invocation))
        {
            return CallSiteOutput.ForDiagnostics([Diagnostic.Create(Diagnostics.UnsupportedCall, invocation.GetLocation(), duckMethod.Name,
                "a 'base' call is not virtual, which neither an interceptor nor the fallback overload can reproduce")]);
        }

        var compilation = semanticModel.Compilation;
        var calledMethod = duckMethod;
        if (duckMethod.IsGenericMethod)
        {
            var unsupported = FindUnsupportedGenericArguments(plan.ToAdapt);
            if (!unsupported.IsEmpty)
                return CallSiteOutput.ForDiagnostics(unsupported);

            if (TypeArgumentInference.TryConstruct(duckMethod, plan.ToAdapt.Select(a => (a.Parameter, (INamedTypeSymbol)a.ConcreteType))) is not { } constructed)
                return CallSiteOutput.ForDiagnostics([CreateInferenceFailure(duckMethod, plan.ToAdapt, compilation)]);

            calledMethod = constructed;
        }

        var adapters = new GeneratedAdapters();
        var diagnostics = AdaptArguments(plan, calledMethod, allowNestedAdapters: !duckMethod.IsGenericMethod, adapters, compilation);
        if (!diagnostics.IsEmpty)
            return CallSiteOutput.ForDiagnostics(diagnostics);

        if (duckMethod.IsGenericMethod)
            return adapters.IsEmpty ? null : adapters.WithOverload(OverloadEmitter.ForDuckTypedCall(duckMethod, calledMethod, plan.Resolved));

        return semanticModel.GetInterceptableLocation(invocation, ct) is { } location
            ? adapters.WithInterceptor(InterceptorEmitter.ForDuckTypedCall(location, duckMethod, plan.Resolved, target.IsExtensionCall))
            : null;
    }

    private static ArgumentPlan? PlanArguments(SemanticModel semanticModel, IMethodSymbol duckMethod, Dictionary<int, ExpressionSyntax> arguments,
        CancellationToken ct)
    {
        var plan = new ArgumentPlan([], []);
        var duckParameters = DuckTypedSignature.GetDuckParameters(duckMethod);
        foreach (var parameter in duckParameters)
        {
            if (!arguments.TryGetValue(parameter.Ordinal, out var expression))
            {
                if (duckMethod.IsGenericMethod)
                    return null;
                continue;
            }

            var argumentType = ArgumentType.Of(semanticModel, expression, ct);
            if (!duckMethod.IsGenericMethod && BindsToFallbackKeepingInterface(duckParameters, parameter, argumentType))
                continue;

            if (!duckMethod.IsGenericMethod && argumentType is IArrayTypeSymbol array &&
                semanticModel.Compilation.HasBuiltInImplicitConversion(array, parameter.Type))
            {
                plan.Resolved[parameter.Ordinal] = new ResolvedArgument(array, null);
                continue;
            }

            if (argumentType is null || !CanAdapt(argumentType, parameter, duckMethod.IsGenericMethod, semanticModel.Compilation))
                return null;

            plan.ToAdapt.Add(new DuckArgument(parameter, expression, argumentType));
        }

        return plan;
    }

    // Null and default literals and values already typed as the interface bind to a fallback variant keeping that parameter as is.
    private static bool BindsToFallbackKeepingInterface(ImmutableArray<IParameterSymbol> duckParameters, IParameterSymbol parameter, ITypeSymbol? argumentType) =>
        FallbackSignature.HasVariantPerSubset(duckParameters) && (argumentType is null || SymbolEqualityComparer.Default.Equals(argumentType, parameter.Type));

    private static bool CanAdapt(ITypeSymbol argumentType, IParameterSymbol parameter, bool isGenericMethod, Compilation compilation)
    {
        if (argumentType.TypeKind == TypeKind.Error)
            return false;

        return isGenericMethod
            ? argumentType is INamedTypeSymbol && !argumentType.ContainsAnyTypeParameter()
            : ArgumentVerifier.IsNamedOrSequence(argumentType, (INamedTypeSymbol)parameter.Type, compilation);
    }

    private static ImmutableArray<Diagnostic> FindUnsupportedGenericArguments(IEnumerable<DuckArgument> arguments)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var (parameter, expression, argumentType) in arguments)
        {
            var concreteType = (INamedTypeSymbol)argumentType;
            var shape = (INamedTypeSymbol)parameter.Type;
            if (!concreteType.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, shape.OriginalDefinition)) &&
                ArgumentVerifier.FindUnsupportedMember(expression, shape) is { } unsupported)
                diagnostics.Add(unsupported);
            else if (concreteType.IsAnonymousType)
                diagnostics.Add(Diagnostics.CreateShapeMismatch(expression.GetLocation(), concreteType, parameter.Type, "anonymous types are not supported by generic [DuckTyped] methods"));
        }

        return diagnostics.ToImmutable();
    }

    private static Diagnostic CreateInferenceFailure(IMethodSymbol duckMethod, List<DuckArgument> arguments, Compilation compilation)
    {
        string? FindMismatch(DuckArgument argument) =>
            ShapeMatcher.FindMismatch((INamedTypeSymbol)argument.Parameter.Type, (INamedTypeSymbol)argument.ConcreteType, compilation);

        var failing = arguments.FirstOrDefault(a => FindMismatch(a) is not null) ?? arguments[0];
        return Diagnostics.CreateShapeMismatch(failing.Expression.GetLocation(), failing.ConcreteType, failing.Parameter.Type,
            FindMismatch(failing) ?? $"type arguments for '{duckMethod.Name}' could not be inferred");
    }

    private static ImmutableArray<Diagnostic> AdaptArguments(ArgumentPlan plan, IMethodSymbol calledMethod, bool allowNestedAdapters,
        GeneratedAdapters adapters, Compilation compilation)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var (parameter, expression, concreteType) in plan.ToAdapt)
        {
            var shape = (INamedTypeSymbol)calledMethod.Parameters[parameter.Ordinal].Type.WithoutNullability();
            var diagnostic = ArgumentVerifier.Verify(expression, shape, concreteType, compilation, out var implementsDirectly);
            if (diagnostic is null && !implementsDirectly)
                diagnostic = ArgumentVerifier.FindInaccessibleType(expression, shape, concreteType, allowNestedAdapters);
            if (diagnostic is not null)
            {
                diagnostics.Add(diagnostic);
                continue;
            }

            var adapterReference = implementsDirectly ? null : adapters.Add(AdapterFactory.Create(shape, concreteType, compilation));
            plan.Resolved[parameter.Ordinal] = new ResolvedArgument(concreteType, adapterReference);
        }

        return diagnostics.ToImmutable();
    }
}
