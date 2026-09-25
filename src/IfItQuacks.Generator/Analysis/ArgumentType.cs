using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IfItQuacks.Generator;

/// <summary>The type an argument is adapted from.</summary>
internal static class ArgumentType
{
    public static ITypeSymbol? Of(SemanticModel semanticModel, ExpressionSyntax expression, CancellationToken ct)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression, ct);
        // 'Person?' and 'Person' are the same type at runtime, so both share one adapter.
        if (typeInfo.Type is { } type)
            return type.WithoutNullability();

        if (typeInfo.ConvertedType is { TypeKind: TypeKind.Delegate } converted)
            return converted;

        return expression is AnonymousFunctionExpressionSyntax or SimpleNameSyntax or MemberAccessExpressionSyntax &&
               GetMethodSymbol(semanticModel.GetSymbolInfo(expression, ct)) is { } method
            ? NaturalDelegateType(method, semanticModel.Compilation)
            : null;
    }

    /// <summary>The argument's type when it is fully known: no error type and free of type parameters.</summary>
    public static ITypeSymbol? Concrete(SemanticModel semanticModel, ExpressionSyntax expression, CancellationToken ct) =>
        Of(semanticModel, expression, ct) is { TypeKind: not TypeKind.Error } type && !type.ContainsAnyTypeParameter() ? type : null;

    private static IMethodSymbol? GetMethodSymbol(SymbolInfo symbolInfo) =>
        (symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.SingleOrDefault()) as IMethodSymbol;

    private static INamedTypeSymbol? NaturalDelegateType(IMethodSymbol method, Compilation compilation)
    {
        if (method.IsGenericMethod || method.RefKind != RefKind.None || method.Parameters.Length > 15 ||
            method.Parameters.Any(p => p.RefKind != RefKind.None) ||
            method.ReturnType.ContainsErrorType() || method.Parameters.Any(p => p.Type.ContainsErrorType()))
            return null;

        var typeArguments = method.Parameters.Select(p => p.Type).ToList();
        if (method.ReturnsVoid)
        {
            var action = compilation.GetTypeByMetadataName(typeArguments.Count == 0 ? "System.Action" : $"System.Action`{typeArguments.Count}");
            return typeArguments.Count == 0 ? action : action?.Construct([.. typeArguments]);
        }

        typeArguments.Add(method.ReturnType);
        return compilation.GetTypeByMetadataName($"System.Func`{typeArguments.Count}")?.Construct([.. typeArguments]);
    }
}
