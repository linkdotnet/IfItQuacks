using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

internal static class Diagnostics
{
    public static readonly DiagnosticDescriptor ShapeMismatch = new(
        id: "IFITQUACKS001",
        title: "Argument does not structurally satisfy interface",
        messageFormat: "Type '{0}' does not structurally satisfy '{1}': {2}",
        category: "IfItQuacks",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ContainingTypeNotPartial = new(
        id: "IFITQUACKS002",
        title: "Duck-typed method's containing type must be partial",
        messageFormat: "Method '{0}' is marked [DuckTyped] but its containing type '{1}' is not declared 'partial'",
        category: "IfItQuacks",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ParameterNotShape = new(
        id: "IFITQUACKS003",
        title: "Duck-typed method needs an interface parameter",
        messageFormat: "Method '{0}' is marked [DuckTyped] but none of its parameters is an interface passed by value",
        category: "IfItQuacks",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedSignature = new(
        id: "IFITQUACKS004",
        title: "Unsupported [DuckTyped] method signature",
        messageFormat: "Method '{0}' is marked [DuckTyped] but {1}",
        category: "IfItQuacks",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedShapeMember = new(
        id: "IFITQUACKS005",
        title: "Unsupported shape member",
        messageFormat: "Interface '{0}' member '{1}' is not supported: generic methods, ref returns and static abstract members can't be adapted",
        category: "IfItQuacks",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedStructArgument = new(
        id: "IFITQUACKS006",
        title: "Unsupported struct argument",
        messageFormat: "Type '{0}' cannot be duck-typed to '{1}': {2}",
        category: "IfItQuacks",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConversionTargetNotShape = new(
        id: "IFITQUACKS007",
        title: "Duck.As type argument must be an interface",
        messageFormat: "'Duck.As<{0}>' requires an interface",
        category: "IfItQuacks",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
