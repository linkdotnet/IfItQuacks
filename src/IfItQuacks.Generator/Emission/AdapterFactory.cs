using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

internal static class AdapterFactory
{
    public static AdapterSet Create(INamedTypeSymbol shape, ITypeSymbol concreteType, Compilation compilation)
    {
        if (SequenceAdapterEmitter.TryMatch(shape, concreteType, compilation) is { } match)
            return CreateSequenceAdapter(shape, concreteType, match, compilation);

        var named = (INamedTypeSymbol)concreteType;
        var name = AdapterEmitter.GetAdapterName(shape, named);
        if (TypeVisibility.GetAdapterHost(named) is { } host)
        {
            var (prefix, suffix) = TypeWrapper.WrapTemplate(host);
            var nested = new OverloadMember(GeneratedCode.HintName("Adapters", host), GeneratedCode.Header + prefix, suffix,
                AdapterEmitter.Emit(shape, named, name, compilation, takesObject: true).Trim());
            return new AdapterSet(name, [], $"global::{host.ToDisplayString()}.{name}", nested);
        }

        return new AdapterSet(name, [new GeneratedFile(name, AdapterEmitter.Emit(shape, named, name, compilation))], $"global::{GeneratedCode.Namespace}.{name}");
    }

    private static AdapterSet CreateSequenceAdapter(INamedTypeSymbol shape, ITypeSymbol sequenceType, SequenceAdapterEmitter.SequenceMatch match,
        Compilation compilation)
    {
        AdapterSet? element;
        if (SequenceAdapterEmitter.IsAssignableElement(match, compilation))
        {
            element = null;
        }
        else if (SequenceAdapterEmitter.IsNamedElement(match, compilation))
        {
            var sourceElement = (INamedTypeSymbol)match.SourceElement;
            var elementName = AdapterEmitter.GetAdapterName(match.ElementShape, sourceElement);
            element = new AdapterSet(elementName, [new GeneratedFile(elementName, AdapterEmitter.Emit(match.ElementShape, sourceElement, elementName, compilation))], elementName);
        }
        else
        {
            element = CreateSequenceAdapter(match.ElementShape, match.SourceElement, SequenceAdapterEmitter.TryMatch(match.ElementShape, match.SourceElement, compilation)!, compilation);
        }

        var sequenceName = SequenceAdapterEmitter.GetAdapterName(shape, sequenceType);
        var sequence = new GeneratedFile(sequenceName, SequenceAdapterEmitter.Emit(shape, sequenceType, match, element?.Name, sequenceName));
        return new AdapterSet(sequenceName, [sequence, .. element?.Files ?? []], $"global::{GeneratedCode.Namespace}.{sequenceName}");
    }
}
