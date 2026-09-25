using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

/// <summary>
/// The generic overloads generated next to a non-generic <c>[DuckTyped]</c> method, so a call with an argument that only
/// matches structurally compiles before its interceptor replaces it.
/// </summary>
internal static class FallbackSignature
{
    private const int MaxParametersWithVariantPerSubset = 4;

    // One variant per subset of generic parameters, so null, default and omitted arguments can keep the interface type.
    public static IEnumerable<ImmutableArray<IParameterSymbol>> GetGenericParameterSubsets(ImmutableArray<IParameterSymbol> duckParameters) =>
        HasVariantPerSubset(duckParameters)
            ? Enumerable.Range(1, (1 << duckParameters.Length) - 1)
                .Select(mask => duckParameters.Where((_, i) => (mask & (1 << i)) != 0).ToImmutableArray())
            : [duckParameters];

    public static bool HasVariantPerSubset(ImmutableArray<IParameterSymbol> duckParameters) =>
        duckParameters.Length <= MaxParametersWithVariantPerSubset;

    public static string TypeParameterName(IParameterSymbol parameter) => $"TDuck{parameter.Ordinal}";

    // Parameters differing only in ref, out or in can't overload each other, so only by-reference versus by-value counts.
    public static bool Matches(IMethodSymbol candidate, IMethodSymbol method, ImmutableArray<IParameterSymbol> genericParameters) =>
        candidate.TypeParameters.Length == genericParameters.Length && candidate.Parameters.Length == method.Parameters.Length &&
        method.Parameters.All(p =>
        {
            var other = candidate.Parameters[p.Ordinal];
            var typeParameterOrdinal = genericParameters.IndexOf(p, SymbolEqualityComparer.Default);
            return (other.RefKind == RefKind.None) == (p.RefKind == RefKind.None) && (typeParameterOrdinal >= 0
                ? other.Type is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } typeParameter && typeParameter.Ordinal == typeParameterOrdinal
                : SymbolEqualityComparer.Default.Equals(other.Type, p.Type));
        });
}
