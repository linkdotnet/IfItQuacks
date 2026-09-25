using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IfItQuacks.Generator;

/// <summary>Calls to <c>Duck.As</c>, <c>Duck.Stub</c>, <c>Duck.Merge</c> and <c>Duck.To</c>, each replaced by an interceptor building the result.</summary>
internal static class DuckConversionAnalysis
{
    public static CallSiteOutput? Analyze(SemanticModel semanticModel, InvocationExpressionSyntax invocation, IMethodSymbol conversion, CancellationToken ct) =>
        conversion.Name switch
        {
            KnownSymbols.DuckStubMethod => AnalyzeStub(semanticModel, invocation, conversion, ct),
            KnownSymbols.DuckMergeMethod => AnalyzeMerge(semanticModel, invocation, conversion, ct),
            KnownSymbols.DuckToMethod => AnalyzeTo(semanticModel, invocation, conversion, ct),
            _ => AnalyzeAs(semanticModel, invocation, conversion, ct),
        };

    private static CallSiteOutput? AnalyzeAs(SemanticModel semanticModel, InvocationExpressionSyntax invocation, IMethodSymbol conversion, CancellationToken ct)
    {
        if (GetShape(conversion) is not { } shape)
            return NotAShape(invocation, conversion);

        if (invocation.ArgumentList.Arguments.Count != 1 || shape.ContainsAnyTypeParameter())
            return null;

        var compilation = semanticModel.Compilation;
        var value = invocation.ArgumentList.Arguments[0].Expression;
        if (ArgumentType.Concrete(semanticModel, value, ct) is not { } valueType || !ArgumentVerifier.IsNamedOrSequence(valueType, shape, compilation))
            return null;

        if (ArgumentVerifier.Verify(value, shape, valueType, compilation, out var implementsDirectly) is { } diagnostic)
            return CallSiteOutput.ForDiagnostics([diagnostic]);

        if (!implementsDirectly && ArgumentVerifier.FindInaccessibleType(value, shape, valueType, allowNested: true) is { } inaccessible)
            return CallSiteOutput.ForDiagnostics([inaccessible]);

        if (semanticModel.GetInterceptableLocation(invocation, ct) is not { } location)
            return null;

        var adapters = new GeneratedAdapters();
        var result = implementsDirectly ? "value" : $"new {adapters.Add(AdapterFactory.Create(shape, valueType, compilation))}({FromObject(valueType, "value")})";
        var shapeName = $"global::{shape.ToDisplayString()}";
        return adapters.WithInterceptor(InterceptorEmitter.ForConversion(location, shapeName, "object value", $"({shapeName})({result})"));
    }

    private static CallSiteOutput? AnalyzeStub(SemanticModel semanticModel, InvocationExpressionSyntax invocation, IMethodSymbol conversion, CancellationToken ct)
    {
        if (GetShape(conversion) is not { } shape)
            return NotAShape(invocation, conversion);

        if (shape.ContainsAnyTypeParameter())
            return null;

        // A stub implements what is missing, but it can't invent an instance for a static abstract member.
        if (ShapeMatcher.FindUnsupportedMember(shape, allowGenericMethods: true) is { } unsupported)
            return CallSiteOutput.ForDiagnostics([Diagnostics.CreateUnsupportedShapeMember(invocation.GetLocation(), shape, unsupported)]);

        switch (invocation.ArgumentList.Arguments.Count)
        {
            case 0:
                return CreateStubCallSite(semanticModel, invocation, shape, stubbedType: null, ct);

            case 1:
                var value = invocation.ArgumentList.Arguments[0].Expression;
                if (ArgumentType.Concrete(semanticModel, value, ct) is not INamedTypeSymbol valueType)
                    return null;

                var diagnostic = ArgumentVerifier.VerifyWrappedValue(value.GetLocation(), value, shape, valueType);
                if (diagnostic is null && ShapeMatcher.FindStubMismatch(shape, valueType, semanticModel.Compilation) is { } mismatch)
                    diagnostic = Diagnostics.CreateShapeMismatch(value.GetLocation(), valueType, shape, mismatch);

                return diagnostic is null
                    ? CreateStubCallSite(semanticModel, invocation, shape, valueType, ct)
                    : CallSiteOutput.ForDiagnostics([diagnostic]);

            default:
                return null;
        }
    }

    private static CallSiteOutput? CreateStubCallSite(SemanticModel semanticModel, InvocationExpressionSyntax invocation,
        INamedTypeSymbol shape, INamedTypeSymbol? stubbedType, CancellationToken ct)
    {
        if (semanticModel.GetInterceptableLocation(invocation, ct) is not { } location)
            return null;

        var adapterName = AdapterEmitter.GetStubAdapterName(shape, stubbedType);
        var adapter = new GeneratedFile(adapterName, AdapterEmitter.EmitStub(shape, stubbedType, adapterName, semanticModel.Compilation));
        var parameters = stubbedType is null ? "" : "object value";
        var argument = stubbedType is null ? "" : FromObject(stubbedType, "value");
        var interceptor = InterceptorEmitter.ForConversion(location, $"global::{shape.ToDisplayString()}", parameters,
            $"new global::{GeneratedCode.Namespace}.{adapterName}({argument})");
        return CallSiteOutput.ForInterceptor(interceptor, adapter);
    }

    private static CallSiteOutput? AnalyzeMerge(SemanticModel semanticModel, InvocationExpressionSyntax invocation, IMethodSymbol conversion, CancellationToken ct)
    {
        if (GetShape(conversion) is not { } shape)
            return NotAShape(invocation, conversion);

        if (shape.ContainsAnyTypeParameter() || invocation.ArgumentList.Arguments.Count != conversion.Parameters.Length)
            return null;

        if (ArgumentVerifier.FindUnsupportedMember(invocation, shape) is { } unsupported)
            return CallSiteOutput.ForDiagnostics([unsupported]);

        var sources = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (ArgumentType.Concrete(semanticModel, argument.Expression, ct) is not INamedTypeSymbol sourceType)
                return null;

            if (ArgumentVerifier.VerifyWrappedValue(argument.GetLocation(), argument.Expression, shape, sourceType) is { } diagnostic)
                return CallSiteOutput.ForDiagnostics([diagnostic]);

            sources.Add(sourceType);
        }

        var compilation = semanticModel.Compilation;
        var merged = sources.ToImmutable();
        if (ShapeMatcher.GetShapeMembers(shape).Where(ShapeMatcher.IsRequired)
                .FirstOrDefault(m => AdapterEmitter.FindSource(merged, m, compilation) is null) is { } missing)
        {
            return CallSiteOutput.ForDiagnostics([Diagnostic.Create(Diagnostics.ShapeMismatch, invocation.GetLocation(),
                string.Join("' + '", merged.Select(s => s.ToDisplayString())), shape.ToDisplayString(),
                $"no value provides '{missing.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'")]);
        }

        if (semanticModel.GetInterceptableLocation(invocation, ct) is not { } location)
            return null;

        var adapterName = AdapterEmitter.GetMergeAdapterName(shape, merged);
        var adapter = new GeneratedFile(adapterName, AdapterEmitter.EmitMerge(shape, merged, adapterName, compilation));
        var parameters = string.Join(", ", merged.Select((_, i) => $"object value{i}"));
        var arguments = string.Join(", ", merged.Select((source, i) => FromObject(source, $"value{i}")));
        var interceptor = InterceptorEmitter.ForConversion(location, $"global::{shape.ToDisplayString()}", parameters,
            $"new global::{GeneratedCode.Namespace}.{adapterName}({arguments})");
        return CallSiteOutput.ForInterceptor(interceptor, adapter);
    }

    private static CallSiteOutput? AnalyzeTo(SemanticModel semanticModel, InvocationExpressionSyntax invocation, IMethodSymbol conversion, CancellationToken ct)
    {
        if (conversion.TypeArguments[0].WithoutNullability() is not INamedTypeSymbol target || target.ContainsAnyTypeParameter() ||
            invocation.ArgumentList.Arguments.Count != 1)
            return null;

        var compilation = semanticModel.Compilation;
        if (ArgumentType.Concrete(semanticModel, invocation.ArgumentList.Arguments[0].Expression, ct) is not INamedTypeSymbol source)
            return null;

        var unsupportedReason = ArgumentVerifier.FindUnnameableProperty(source)
                                ?? (TypeVisibility.IsNameable(source) ? null : TypeVisibility.InaccessibleReason)
                                ?? StructuralCopyEmitter.FindMismatch(target, source, compilation);
        if (unsupportedReason is not null)
            return CallSiteOutput.ForDiagnostics([Diagnostics.CreateUnsupportedConversionTarget(invocation.GetLocation(), target, source, unsupportedReason)]);

        if (semanticModel.GetInterceptableLocation(invocation, ct) is not { } location)
            return null;

        var factoryName = StructuralCopyEmitter.GetFactoryName(target, source);
        var factory = new GeneratedFile(factoryName, StructuralCopyEmitter.Emit(target, source, factoryName, compilation));
        var interceptor = InterceptorEmitter.ForConversion(location, target.ToDisplayString(), "object value",
            $"global::{GeneratedCode.Namespace}.{factoryName}.Create({FromObject(source, "value")})");
        return CallSiteOutput.ForInterceptor(interceptor, factory);
    }

    private static INamedTypeSymbol? GetShape(IMethodSymbol conversion) =>
        conversion.TypeArguments[0].WithoutNullability() is INamedTypeSymbol { TypeKind: TypeKind.Interface } shape ? shape : null;

    private static CallSiteOutput NotAShape(InvocationExpressionSyntax invocation, IMethodSymbol conversion) =>
        CallSiteOutput.ForDiagnostics([Diagnostic.Create(Diagnostics.ConversionTargetNotShape, invocation.GetLocation(),
            conversion.Name, conversion.TypeArguments[0].ToDisplayString())]);

    // The interceptor takes the 'object' the Duck method declares, so it casts back to the value's type wherever that type can be named.
    private static string FromObject(ITypeSymbol type, string value) =>
        type.IsAnonymousType || !TypeVisibility.IsNameable(type) ? value : $"({type.ToDisplayString()}){value}";
}
