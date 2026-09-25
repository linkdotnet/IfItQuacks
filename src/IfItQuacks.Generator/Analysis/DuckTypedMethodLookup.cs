using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IfItQuacks.Generator;

internal sealed record CallTarget(IMethodSymbol Method, bool IsExtensionCall);

/// <summary>Finds the <c>[DuckTyped]</c> method a call is meant for, which the compiler can't bind until the call is adapted.</summary>
internal static class DuckTypedMethodLookup
{
    public static CallTarget? Find(SemanticModel semanticModel, InvocationExpressionSyntax invocation, ImmutableArray<IMethodSymbol> candidates,
        DuckTypedMethodIndex duckMethods, CancellationToken ct)
    {
        var candidate = candidates.FirstOrDefault(c => (c.ReducedFrom ?? c).OriginalDefinition is { DeclaringSyntaxReferences.Length: > 0 } d && KnownSymbols.IsDuckTyped(d));
        if (candidate is null && candidates.Length > 0)
        {
            var receiverType = GetReceiverType(semanticModel, invocation, ct);
            candidate = candidates.Select(c => FindDuckTypedInHierarchy(c, receiverType)).FirstOrDefault(m => m is not null);
        }

        if (candidate is not null)
            return new CallTarget((candidate.ReducedFrom ?? candidate).OriginalDefinition, candidate.MethodKind == MethodKind.ReducedExtension);

        return FindExtension(invocation, duckMethods, semanticModel.Compilation) is { } extension
            ? new CallTarget(extension, IsExtensionCall: true)
            : null;
    }

    // A call binds to the method declaring the virtual slot or to an override, while [DuckTyped] may sit on any of them.
    private static IMethodSymbol? FindDuckTypedInHierarchy(IMethodSymbol candidate, ITypeSymbol? receiverType)
    {
        for (var method = candidate; method is not null; method = method.OverriddenMethod)
        {
            if (method.OriginalDefinition is { DeclaringSyntaxReferences.Length: > 0 } definition && KnownSymbols.IsDuckTyped(definition))
                return definition;
        }

        if (!candidate.IsVirtual && !candidate.IsAbstract && !candidate.IsOverride)
            return null;

        for (var type = receiverType as INamedTypeSymbol; type is not null; type = type.BaseType)
        {
            if (type.GetMembers(candidate.Name).OfType<IMethodSymbol>().FirstOrDefault(m =>
                    m is { IsOverride: true, DeclaringSyntaxReferences.Length: > 0 } && KnownSymbols.IsDuckTyped(m) && m.Overrides(candidate)) is { } duckOverride)
                return duckOverride;
        }

        return null;
    }

    // 'person.Describe()' binds to nothing until the generated extension exists, so the [DuckTyped] method is looked up by name.
    private static IMethodSymbol? FindExtension(InvocationExpressionSyntax invocation, DuckTypedMethodIndex duckMethods, Compilation compilation)
    {
        if (InvocationSyntax.GetReceiver(invocation) is null || InvocationSyntax.GetInvokedName(invocation) is not { } name)
            return null;

        return duckMethods.Extensions
            .Where(m => m.Name == name)
            .Select(m => compilation.GetTypeByMetadataName(m.ContainingType)?.GetMembers(name).OfType<IMethodSymbol>()
                .FirstOrDefault(member => member.IsExtensionMethod && KnownSymbols.IsDuckTyped(member)))
            .FirstOrDefault(m => m is not null);
    }

    private static ITypeSymbol? GetReceiverType(SemanticModel semanticModel, InvocationExpressionSyntax invocation, CancellationToken ct) =>
        InvocationSyntax.GetReceiver(invocation) is { } receiver
            ? semanticModel.GetTypeInfo(receiver, ct).Type
            : semanticModel.GetEnclosingSymbol(invocation.SpanStart, ct)?.ContainingType;
}
