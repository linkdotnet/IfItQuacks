using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

internal static class DuckTypedMethodAnalysis
{
    public static DuckTypedMethodOutput Analyze(GeneratorAttributeSyntaxContext context)
    {
        var method = (IMethodSymbol)context.TargetSymbol;
        var compilation = context.SemanticModel.Compilation;
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        if (!DuckTypedMethodValidator.Validate(method, compilation, context.TargetNode.GetLocation(), diagnostics))
            return new DuckTypedMethodOutput(null, null, new EquatableArray<Diagnostic>(diagnostics.ToImmutable()));

        var fallback = method.IsGenericMethod ? null : FallbackOverloadEmitter.Emit(method, compilation);
        return new DuckTypedMethodOutput(DuckMethodRef.From(method), fallback, new EquatableArray<Diagnostic>(diagnostics.ToImmutable()));
    }
}
