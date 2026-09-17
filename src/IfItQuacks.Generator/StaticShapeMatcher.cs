using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

/// <summary>
/// Matches the <c>static abstract</c> members of a self-referencing interface (the generic math pattern)
/// against the static members and operators of a concrete type.
/// </summary>
internal static class StaticShapeMatcher
{
    public static IEnumerable<ISymbol> GetStaticMembers(INamedTypeSymbol shape) =>
        shape.GetMembers()
            .Concat(shape.AllInterfaces.SelectMany(i => i.GetMembers()))
            .Where(IsRequiredStatic);

    public static ISymbol? FindUnsupportedMember(INamedTypeSymbol shape) =>
        shape.GetMembers()
            .Concat(shape.AllInterfaces.SelectMany(i => i.GetMembers()))
            .FirstOrDefault(member => member is { IsStatic: true, IsAbstract: true } && member switch
            {
                IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } op => OperatorToken(op) is null,
                IMethodSymbol { MethodKind: MethodKind.Ordinary } method => method.IsGenericMethod,
                IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet } => false,
                IPropertySymbol { IsIndexer: true } => true,
                IPropertySymbol => false,
                _ => true,
            });

    public static string? FindMismatch(INamedTypeSymbol shape, INamedTypeSymbol concreteType, Compilation compilation)
    {
        foreach (var member in GetStaticMembers(shape))
        {
            if (FindCounterpart(member, concreteType, compilation) is not null)
                continue;

            return member switch
            {
                IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } op =>
                    $"missing operator '{OperatorToken(op)}'",
                IMethodSymbol method =>
                    $"missing static method '{method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'",
                _ => $"missing static member '{member.Name}' of type '{((IPropertySymbol)member).Type.ToDisplayString()}'",
            };
        }
        return null;
    }

    public static ISymbol? FindCounterpart(ISymbol member, INamedTypeSymbol concreteType, Compilation compilation) => member switch
    {
        IMethodSymbol method => ShapeMatcher.GetAllMembers(concreteType).OfType<IMethodSymbol>()
            .FirstOrDefault(candidate => IsPublicStatic(candidate) && !candidate.IsGenericMethod &&
                                         candidate.Name == method.Name && candidate.MethodKind == method.MethodKind &&
                                         ShapeMatcher.IsAssignable(candidate.ReturnType, method.ReturnType, compilation) &&
                                         ShapeMatcher.ParametersMatch(candidate.Parameters, method.Parameters, compilation)),
        IPropertySymbol property => ShapeMatcher.GetAllMembers(concreteType).OfType<IPropertySymbol>()
            .FirstOrDefault(candidate => IsPublicStatic(candidate) && candidate.Name == property.Name &&
                                         (property.GetMethod is null || candidate.GetMethod is { DeclaredAccessibility: Accessibility.Public }) &&
                                         (property.SetMethod is null || candidate.SetMethod is { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false }) &&
                                         (property.GetMethod is null || ShapeMatcher.IsAssignable(candidate.Type, property.Type, compilation)) &&
                                         (property.SetMethod is null || ShapeMatcher.IsAssignable(property.Type, candidate.Type, compilation))),
        _ => null,
    };

    /// <summary>The C# token for an operator's metadata name, or <c>null</c> for operators that can't be forwarded.</summary>
    public static string? OperatorToken(IMethodSymbol method) => method.Name switch
    {
        "op_Addition" => "+",
        "op_Subtraction" => "-",
        "op_Multiply" => "*",
        "op_Division" => "/",
        "op_Modulus" => "%",
        "op_BitwiseAnd" => "&",
        "op_BitwiseOr" => "|",
        "op_ExclusiveOr" => "^",
        "op_LeftShift" => "<<",
        "op_RightShift" => ">>",
        "op_UnsignedRightShift" => ">>>",
        "op_Equality" => "==",
        "op_Inequality" => "!=",
        "op_LessThan" => "<",
        "op_GreaterThan" => ">",
        "op_LessThanOrEqual" => "<=",
        "op_GreaterThanOrEqual" => ">=",
        "op_UnaryPlus" => "+",
        "op_UnaryNegation" => "-",
        "op_LogicalNot" => "!",
        "op_OnesComplement" => "~",
        "op_Increment" => "++",
        "op_Decrement" => "--",
        _ => null,
    };

    private static bool IsRequiredStatic(ISymbol member) =>
        member is { IsStatic: true, IsAbstract: true } &&
        member is IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.UserDefinedOperator } or IPropertySymbol { IsIndexer: false };

    private static bool IsPublicStatic(ISymbol member) => member.IsStatic && member.DeclaredAccessibility == Accessibility.Public;
}
