using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IfItQuacks.Generator;

/// <summary>
/// Calls to a <c>[DuckTyped]</c> method with duck-typed constraints, e.g. 'where T : IAddable&lt;T&gt;'. The type argument
/// becomes an adapter implementing the constraint, so static abstract members and operators can be matched structurally.
/// </summary>
internal static class ConstraintCallAnalysis
{
    public static CallSiteOutput? Analyze(SemanticModel semanticModel, IMethodSymbol duckMethod,
        ImmutableArray<ITypeParameterSymbol> constraintTypeParameters, Dictionary<int, ExpressionSyntax> arguments, CancellationToken ct)
    {
        var adapters = new GeneratedAdapters();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        if (BindConstraintTypeParameters(semanticModel, duckMethod, constraintTypeParameters, arguments, adapters, diagnostics, ct) is not { } typeArguments)
            return null;

        var adaptedArguments = AdaptShapeParametersBesideConstraints(semanticModel, duckMethod, arguments, adapters, diagnostics, ct);
        if (diagnostics.Count > 0)
            return CallSiteOutput.ForDiagnostics(diagnostics);

        var needsNoAdapter = typeArguments.Values.All(b => b.AdapterReference is null) && adaptedArguments.Count == 0;
        if (typeArguments.Count != constraintTypeParameters.Length || needsNoAdapter)
            return null;

        return OverloadEmitter.ForConstraintCall(duckMethod, typeArguments, adaptedArguments) is { } overload ? adapters.WithOverload(overload) : null;
    }

    private static Dictionary<ITypeParameterSymbol, TypeParameterBinding>? BindConstraintTypeParameters(SemanticModel semanticModel,
        IMethodSymbol duckMethod, ImmutableArray<ITypeParameterSymbol> constraintTypeParameters, Dictionary<int, ExpressionSyntax> arguments,
        GeneratedAdapters adapters, ImmutableArray<Diagnostic>.Builder diagnostics, CancellationToken ct)
    {
        var compilation = semanticModel.Compilation;
        var typeArguments = new Dictionary<ITypeParameterSymbol, TypeParameterBinding>(SymbolEqualityComparer.Default);
        foreach (var typeParameter in constraintTypeParameters)
        {
            if (FindCommonArgument(semanticModel, duckMethod, typeParameter, arguments, ct) is not { } argument)
                return null;

            var (expression, concreteType) = argument;
            var shape = (INamedTypeSymbol)typeParameter.ConstraintTypes[0].SubstituteTypeParameter(typeParameter, concreteType);
            if (SatisfiesConstraint(concreteType, shape))
            {
                typeArguments[typeParameter] = new TypeParameterBinding(concreteType, null);
                continue;
            }

            if (VerifySelfShapeArgument(expression, shape, concreteType, compilation) is { } diagnostic)
            {
                diagnostics.Add(diagnostic);
                continue;
            }

            var adapterName = AdapterEmitter.GetAdapterName(shape, concreteType);
            adapters.Add(new GeneratedFile(adapterName, SelfShapeAdapterEmitter.Emit(shape, concreteType, adapterName, compilation)));
            typeArguments[typeParameter] = new TypeParameterBinding(concreteType, $"global::{GeneratedCode.Namespace}.{adapterName}");
        }

        return typeArguments;
    }

    private static (ExpressionSyntax Expression, INamedTypeSymbol Type)? FindCommonArgument(SemanticModel semanticModel, IMethodSymbol duckMethod,
        ITypeParameterSymbol typeParameter, Dictionary<int, ExpressionSyntax> arguments, CancellationToken ct)
    {
        var typedArguments = duckMethod.Parameters
            .Where(p => SymbolEqualityComparer.Default.Equals(p.Type, typeParameter))
            .Select(p => arguments.TryGetValue(p.Ordinal, out var expression)
                ? (Expression: expression, Type: ArgumentType.Concrete(semanticModel, expression, ct) as INamedTypeSymbol)
                : default)
            .ToImmutableArray();

        return typedArguments.All(a => a.Type is not null) && typedArguments.Select(a => a.Type).Distinct(SymbolEqualityComparer.Default).Count() == 1
            ? (typedArguments[0].Expression, typedArguments[0].Type!)
            : null;
    }

    // A type satisfying the constraint itself needs no adapter; the call compiles as it is.
    private static bool SatisfiesConstraint(INamedTypeSymbol type, INamedTypeSymbol shape) =>
        type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, shape));

    private static Diagnostic? VerifySelfShapeArgument(ExpressionSyntax expression, INamedTypeSymbol shape, INamedTypeSymbol concreteType, Compilation compilation)
    {
        if (ArgumentVerifier.FindInaccessibleType(expression, shape, concreteType, allowNested: false) is { } inaccessible)
            return inaccessible;

        var location = expression.GetLocation();
        // The overload names the argument's type in its signature, which an anonymous type has no name for.
        if (concreteType.IsAnonymousType)
            return Diagnostics.CreateShapeMismatch(location, concreteType, shape, "anonymous types are not supported by duck-typed constraints");

        if (StaticShapeMatcher.FindUnsupportedMember(shape) is { } unsupportedStatic)
            return Diagnostics.CreateUnsupportedShapeMember(location, shape, unsupportedStatic);

        if (ShapeMatcher.GetShapeMembers(shape).FirstOrDefault(m => m is IMethodSymbol { IsGenericMethod: true } || UsesSelfType(m, concreteType)) is { } unsupportedInstance)
            return Diagnostics.CreateUnsupportedShapeMember(location, shape, unsupportedInstance);

        if ((StaticShapeMatcher.FindMismatch(shape, concreteType, compilation) ?? ShapeMatcher.FindMismatch(shape, concreteType, compilation)) is { } mismatch)
            return Diagnostics.CreateShapeMismatch(location, concreteType, shape, mismatch);

        return ArgumentVerifier.FindUnsupportedStruct(concreteType, implementsDirectly: false) is { } unsupportedStruct
            ? Diagnostics.CreateUnsupportedStructArgument(location, concreteType, shape, unsupportedStruct)
            : null;
    }

    // The adapter forwards instance members to the argument, which it can't do for a signature using the argument's own type.
    private static bool UsesSelfType(ISymbol member, INamedTypeSymbol self) => member switch
    {
        IMethodSymbol method => method.ReturnType.Mentions(self) || method.Parameters.Any(p => p.Type.Mentions(self)),
        IPropertySymbol property => property.Type.Mentions(self) || property.Parameters.Any(p => p.Type.Mentions(self)),
        IEventSymbol @event => @event.Type.Mentions(self),
        _ => false,
    };

    // Interface parameters next to the constraints keep the regular adapter, boxed as the interface.
    private static Dictionary<int, ResolvedArgument> AdaptShapeParametersBesideConstraints(SemanticModel semanticModel, IMethodSymbol duckMethod,
        Dictionary<int, ExpressionSyntax> arguments, GeneratedAdapters adapters, ImmutableArray<Diagnostic>.Builder diagnostics, CancellationToken ct)
    {
        var compilation = semanticModel.Compilation;
        var adapted = new Dictionary<int, ResolvedArgument>();
        foreach (var parameter in DuckTypedSignature.GetDuckParameters(duckMethod))
        {
            if (!arguments.TryGetValue(parameter.Ordinal, out var expression))
                continue;

            var argumentType = ArgumentType.Of(semanticModel, expression, ct);
            if (argumentType is null || SymbolEqualityComparer.Default.Equals(argumentType, parameter.Type) ||
                argumentType.ContainsErrorType() || argumentType.ContainsAnyTypeParameter())
                continue;

            var shape = (INamedTypeSymbol)parameter.Type;
            if (ArgumentVerifier.Verify(expression, shape, argumentType, compilation, out var implementsDirectly) is { } diagnostic)
            {
                diagnostics.Add(diagnostic);
                continue;
            }

            if (implementsDirectly)
                continue;

            if (ArgumentVerifier.FindInaccessibleType(expression, shape, argumentType, allowNested: false) is { } inaccessible)
            {
                diagnostics.Add(inaccessible);
                continue;
            }

            if (argumentType is not INamedTypeSymbol { IsAnonymousType: false } named)
            {
                diagnostics.Add(Diagnostics.CreateShapeMismatch(expression.GetLocation(), argumentType, shape,
                    "anonymous types and sequences are not supported next to duck-typed constraints"));
                continue;
            }

            adapted[parameter.Ordinal] = new ResolvedArgument(named, adapters.Add(AdapterFactory.Create(shape, named, compilation)));
        }

        return adapted;
    }
}
