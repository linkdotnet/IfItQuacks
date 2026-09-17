using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

internal sealed class DuckOverload(IMethodSymbol method, INamedTypeSymbol concreteType, string adapterName)
{
    public IMethodSymbol Method { get; } = method;
    public INamedTypeSymbol ConcreteType { get; } = concreteType;
    public string AdapterName { get; } = adapterName;
}
