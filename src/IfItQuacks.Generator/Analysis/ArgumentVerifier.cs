using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IfItQuacks.Generator;

/// <summary>Checks whether an argument can be adapted to a shape, and describes why not.</summary>
internal static class ArgumentVerifier
{
    public static Diagnostic? Verify(ExpressionSyntax argument, INamedTypeSymbol shape, ITypeSymbol argumentType,
        Compilation compilation, out bool implementsDirectly)
    {
        // Covers explicit implementations and variance (e.g. List<string> as IEnumerable<object>), which structural matching can't see.
        implementsDirectly = compilation.HasBuiltInImplicitConversion(argumentType, shape);
        if (implementsDirectly)
            return null;

        if (SequenceAdapterEmitter.TryMatch(shape, argumentType, compilation) is { } match)
            return VerifySequence(argument, shape, argumentType, match);

        if (FindUnsupportedMember(argument, shape) is { } unsupported)
            return unsupported;

        if (argumentType is not INamedTypeSymbol concreteType)
            return Diagnostics.CreateShapeMismatch(argument.GetLocation(), argumentType, shape, "only named types and sequences can be adapted");

        if ((ShapeMatcher.FindMismatch(shape, concreteType, compilation) ?? FindUnnameableProperty(concreteType)) is { } mismatch)
            return Diagnostics.CreateShapeMismatch(argument.GetLocation(), concreteType, shape, mismatch);

        return FindUnsupportedStruct(concreteType, implementsDirectly) is { } unsupportedStruct
            ? Diagnostics.CreateUnsupportedStructArgument(argument.GetLocation(), concreteType, shape, unsupportedStruct)
            : null;
    }

    // Only a sequence can be adapted without being a named type, e.g. a Person[] passed as IEnumerable<INamed>.
    public static bool IsNamedOrSequence(ITypeSymbol type, INamedTypeSymbol shape, Compilation compilation) =>
        type is INamedTypeSymbol || SequenceAdapterEmitter.TryMatch(shape, type, compilation) is not null;

    /// <summary>Checks a value a stub or merge adapter holds on to, which is copied as it is.</summary>
    public static Diagnostic? VerifyWrappedValue(Location location, ExpressionSyntax value, INamedTypeSymbol shape, INamedTypeSymbol valueType)
    {
        if (FindUnsupportedStruct(valueType, implementsDirectly: false) is { } unsupportedStruct)
            return Diagnostics.CreateUnsupportedStructArgument(location, valueType, shape, unsupportedStruct);

        if (FindUnnameableProperty(valueType) is { } unnameable)
            return Diagnostics.CreateShapeMismatch(location, valueType, shape, unnameable);

        return FindInaccessibleType(value, shape, valueType, allowNested: false);
    }

    public static Diagnostic? FindInaccessibleType(ExpressionSyntax argument, INamedTypeSymbol shape, ITypeSymbol type, bool allowNested)
    {
        if (TypeVisibility.IsNameable(type) || (allowNested && TypeVisibility.GetAdapterHost(type) is not null))
            return null;

        var reason = allowNested && type is INamedTypeSymbol { ContainingType: { } host } && TypeVisibility.IsNameable(host)
            ? $"{TypeVisibility.InaccessibleReason}; declare '{host.Name}' and its containing types 'partial' to nest the adapter in it"
            : TypeVisibility.InaccessibleReason;
        return Diagnostics.CreateShapeMismatch(argument.GetLocation(), type, shape, reason);
    }

    public static Diagnostic? FindUnsupportedMember(SyntaxNode node, INamedTypeSymbol shape) =>
        ShapeMatcher.FindUnsupportedMember(shape) is { } member ? Diagnostics.CreateUnsupportedShapeMember(node.GetLocation(), shape, member) : null;

    public static string? FindUnnameableProperty(INamedTypeSymbol type) =>
        type.IsAnonymousType && AdapterEmitter.FindUnnameableProperty(type) is { } property
            ? $"property '{property}' has a type that contains an anonymous type"
            : null;

    // A struct implementing the shape is boxed by the compiler itself, so only adapter-wrapped mutable structs would silently lose mutations.
    public static string? FindUnsupportedStruct(ITypeSymbol type, bool implementsDirectly)
    {
        if (type.IsRefLikeType)
            return "ref structs cannot be converted to an interface";

        if (type.IsValueType && !type.IsReadOnly && !implementsDirectly)
            return "mutable structs are copied into an adapter, so mutations made through the shape would be lost";

        return null;
    }

    private static Diagnostic? VerifySequence(ExpressionSyntax argument, INamedTypeSymbol shape, ITypeSymbol sequenceType,
        SequenceAdapterEmitter.SequenceMatch match)
    {
        if (FindUnsupportedMember(argument, match.ElementShape) is { } unsupported)
            return unsupported;

        if (FindUnsupportedStruct(match.SourceElement, implementsDirectly: false) is { } unsupportedElement)
            return Diagnostics.CreateUnsupportedStructArgument(argument.GetLocation(), match.SourceElement, match.ElementShape, unsupportedElement);

        return FindUnsupportedStruct(sequenceType, implementsDirectly: false) is { } unsupportedStruct
            ? Diagnostics.CreateUnsupportedStructArgument(argument.GetLocation(), sequenceType, shape, unsupportedStruct)
            : null;
    }
}
