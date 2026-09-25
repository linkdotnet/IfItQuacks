using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

/// <summary>Emits the <see cref="FallbackSignature"/> overloads of a non-generic <c>[DuckTyped]</c> method.</summary>
internal static class FallbackOverloadEmitter
{
    public static GeneratedFile Emit(IMethodSymbol method, Compilation compilation)
    {
        var yieldsToOverloads = KnownSymbols.SupportsOverloadPriority(compilation);
        var variants = FallbackSignature.GetGenericParameterSubsets(DuckTypedSignature.GetDuckParameters(method))
            .Select(genericParameters => EmitVariant(method, genericParameters, yieldsToOverloads));

        var methodSource = string.Join("\n\n", variants);
        if (NeedsForwarder(method))
            methodSource += "\n\n" + EmitForwarder(method);

        var priorityAttributePolyfill = yieldsToOverloads && KnownSymbols.LacksOverloadPriorityAttribute(compilation)
            ? EmbeddedSources.OverloadPriorityAttributePolyfill + "\n"
            : "";
        return new GeneratedFile(
            GeneratedCode.HintName("Fallback", method.ContainingType, method.Name),
            GeneratedCode.Header + priorityAttributePolyfill + TypeWrapper.WrapInContainingScope(method.ContainingType, methodSource));
    }

    public static string AccessibleName(IMethodSymbol method) => NeedsForwarder(method) ? ForwarderName(method) : method.Name;

    // Name lookup on the derived type finds the generated fallback before the override, so an override is called through the type declaring the virtual method.
    public static string ThroughVirtualSlot(IMethodSymbol method, string receiver) =>
        method.IsOverride ? $"((global::{method.OverriddenRoot().ContainingType.ToDisplayString()}){receiver})" : receiver;

    private static string EmitVariant(IMethodSymbol method, ImmutableArray<IParameterSymbol> genericParameters, bool yieldsToOverloads)
    {
        var typeParameterNames = genericParameters.ToDictionary(p => p.Ordinal, FallbackSignature.TypeParameterName);

        var parameters = string.Join(", ", method.Parameters.Select(p => typeParameterNames.TryGetValue(p.Ordinal, out var name)
            ? SourceSyntax.DeclareParameter(method, p, name) + GenericDefaultValue(p)
            : SourceSyntax.DeclareParameter(method, p, p.Type.ToDisplayString()) + SourceSyntax.DefaultValue(p)));
        var arguments = string.Join(", ", method.Parameters.Select(p => typeParameterNames.TryGetValue(p.Ordinal, out var name)
            ? RuntimeCast(p, name)
            : SourceSyntax.Argument(p)));
        var yieldToOverloadsAttribute = yieldsToOverloads ? $" [global::{KnownSymbols.OverloadResolutionPriorityAttributeMetadataName}(-1)]" : "";

        return $$"""
            {{GeneratedCode.EditorBrowsableNever}}{{yieldToOverloadsAttribute}}
            {{SourceSyntax.AccessibilityKeyword(method.DeclaredAccessibility)}} {{(method.IsStatic ? "static " : "")}}{{method.ReturnType.ToDisplayString()}} {{method.Name}}<{{string.Join(", ", typeParameterNames.Values)}}>({{parameters}})
            {
                {{(method.ReturnsVoid ? "" : "return ")}}{{(method.IsOverride ? ThroughVirtualSlot(method, "this") + "." : "")}}{{method.Name}}({{arguments}});
            }
            """;
    }

    // Interceptors live in their own namespace, so methods they can't access are called through an internal forwarder on the containing type.
    private static bool NeedsForwarder(IMethodSymbol method) =>
        method.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal;

    private static string ForwarderName(IMethodSymbol method) => $"__IfItQuacks_{method.Name}";

    private static string EmitForwarder(IMethodSymbol method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => SourceSyntax.Parameter(p, p.Type.ToDisplayString())));
        var arguments = string.Join(", ", method.Parameters.Select(SourceSyntax.Argument));
        var modifiers = (method.IsStatic ? "static " : "") + (method.IsReadOnly ? "readonly " : "");
        return $"{GeneratedCode.EditorBrowsableNever}\n" +
               $"internal {modifiers}{method.ReturnType.ToDisplayString()} {ForwarderName(method)}({parameters}) => {method.Name}({arguments});";
    }

    private static string GenericDefaultValue(IParameterSymbol parameter) => parameter.HasExplicitDefaultValue ? " = default!" : "";

    // Calls that couldn't be verified at compile time (e.g. from generic code) still work if the value implements the interface at runtime.
    private static string RuntimeCast(IParameterSymbol parameter, string typeParameterName)
    {
        var identifier = SourceSyntax.Identifier(parameter.Name);
        var interfaceName = $"global::{parameter.Type.WithoutNullability().ToDisplayString()}";
        var allowNull = parameter.Type.NullableAnnotation == NullableAnnotation.Annotated ? $"{identifier} is null ? null : " : "";
        return $"{identifier} is {interfaceName} __duck{parameter.Ordinal} ? __duck{parameter.Ordinal} : {allowNull}" +
               $"throw new global::IfItQuacks.DuckTypeMismatchException(typeof({typeParameterName}), typeof({interfaceName}))";
    }
}
