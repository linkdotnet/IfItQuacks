using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace IfItQuacks.Generator;

/// <summary>
/// Finds casts and type tests in a <c>[DuckTyped]</c> method that expect the caller's instance, although an adapter
/// arrives whenever the argument doesn't implement the interface itself.
/// </summary>
internal static class AdapterCastFinder
{
    private const string Cast = "cast to";
    private const string TypeTest = "type test for";

    public static IEnumerable<Diagnostic> Find(IOperation body, IReadOnlyDictionary<IParameterSymbol, ITypeSymbol> shapes, Compilation compilation)
    {
        foreach (var (value, type, kind, syntax) in body.Descendants().SelectMany(Candidates))
        {
            // An interface or type parameter may still be implemented by the adapter, so only concrete types are certain to miss.
            if (Unconverted(value) is IParameterReferenceOperation { Parameter: var parameter } &&
                shapes.TryGetValue(parameter, out var shape) &&
                type is { TypeKind: not (TypeKind.Interface or TypeKind.TypeParameter or TypeKind.Error) } &&
                !compilation.ClassifyCommonConversion(parameter.Type, type).IsImplicit)
            {
                yield return Diagnostic.Create(Diagnostics.AdapterCast, syntax.GetLocation(),
                    parameter.Name, shape.ToDisplayString(), kind, type.ToDisplayString());
            }
        }
    }

    private static IEnumerable<(IOperation Value, ITypeSymbol? Type, string Kind, SyntaxNode Syntax)> Candidates(IOperation operation) => operation switch
    {
        IConversionOperation { IsImplicit: false } conversion => [(conversion.Operand, conversion.Type, Cast, conversion.Syntax)],
        IIsTypeOperation isType => [(isType.ValueOperand, isType.TypeOperand, TypeTest, isType.Syntax)],
        IIsPatternOperation isPattern => Matched(isPattern.Value, isPattern.Pattern),
        ISwitchExpressionOperation switchExpression => switchExpression.Arms.SelectMany(arm => Matched(switchExpression.Value, arm.Pattern)),
        ISwitchOperation switchStatement => switchStatement.Cases
            .SelectMany(c => c.Clauses)
            .OfType<IPatternCaseClauseOperation>()
            .SelectMany(clause => Matched(switchStatement.Value, clause.Pattern)),
        _ => [],
    };

    // Only top-level patterns test the value itself; property subpatterns test its members.
    private static IEnumerable<(IOperation Value, ITypeSymbol? Type, string Kind, SyntaxNode Syntax)> Matched(IOperation value, IPatternOperation pattern) => pattern switch
    {
        IBinaryPatternOperation binary => Matched(value, binary.LeftPattern).Concat(Matched(value, binary.RightPattern)),
        INegatedPatternOperation negated => Matched(value, negated.Pattern),
        IDeclarationPatternOperation declaration => [(value, declaration.MatchedType, TypeTest, pattern.Syntax)],
        ITypePatternOperation type => [(value, type.MatchedType, TypeTest, pattern.Syntax)],
        IRecursivePatternOperation recursive => [(value, recursive.MatchedType, TypeTest, pattern.Syntax)],
        _ => [],
    };

    private static IOperation Unconverted(IOperation operation) =>
        operation is IConversionOperation conversion ? Unconverted(conversion.Operand) : operation;
}
