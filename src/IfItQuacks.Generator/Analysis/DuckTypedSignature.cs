using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

/// <summary>Which parameters of a <c>[DuckTyped]</c> method accept arguments that only match structurally.</summary>
internal static class DuckTypedSignature
{
    public static ImmutableArray<IParameterSymbol> GetDuckParameters(IMethodSymbol method) =>
        [.. method.Parameters.Where(p => p is { RefKind: RefKind.None, Type.TypeKind: TypeKind.Interface })];

    public static ImmutableArray<ITypeParameterSymbol> GetDuckConstraintTypeParameters(IMethodSymbol method) =>
        [.. method.TypeParameters.Where(tp => tp.ConstraintTypes.Length == 1 &&
                                              tp.ConstraintTypes[0] is INamedTypeSymbol { TypeKind: TypeKind.Interface } &&
                                              method.Parameters.Any(p => IsPassedByValueAs(p, tp)))];

    public static Dictionary<IParameterSymbol, ITypeSymbol> GetParametersReceivingAdapters(IMethodSymbol method)
    {
        var shapes = GetDuckParameters(method).ToDictionary<IParameterSymbol, IParameterSymbol, ITypeSymbol>(p => p, p => p.Type, SymbolEqualityComparer.Default);
        foreach (var typeParameter in GetDuckConstraintTypeParameters(method))
        {
            foreach (var parameter in method.Parameters.Where(p => IsPassedByValueAs(p, typeParameter)))
                shapes[parameter] = typeParameter.ConstraintTypes[0];
        }

        return shapes;
    }

    private static bool IsPassedByValueAs(IParameterSymbol parameter, ITypeParameterSymbol typeParameter) =>
        parameter.RefKind == RefKind.None && SymbolEqualityComparer.Default.Equals(parameter.Type, typeParameter);
}
