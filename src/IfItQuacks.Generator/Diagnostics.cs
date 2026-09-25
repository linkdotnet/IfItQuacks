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
        title: "Unsupported interface member",
        messageFormat: "Interface '{0}' member '{1}' is not supported: generic methods and static abstract members can't be adapted",
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
        title: "Duck conversion target must be an interface",
        messageFormat: "'Duck.{0}<{1}>' requires an interface",
        category: "IfItQuacks",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedCall = new(
        id: "IFITQUACKS008",
        title: "Unsupported duck-typed call",
        messageFormat: "Call to '{0}' cannot be duck-typed: {1}",
        category: "IfItQuacks",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedConversionTarget = new(
        id: "IFITQUACKS009",
        title: "Unsupported Duck.To target",
        messageFormat: "Type '{0}' cannot be built from '{1}': {2}",
        category: "IfItQuacks",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MappedShapeNotPartialInterface = new(
        id: "IFITQUACKS010",
        title: "Mapped shape target must be a partial interface",
        messageFormat: "'{0}' is marked [DuckShape<>] but {1}",
        category: "IfItQuacks",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedMappedShape = new(
        id: "IFITQUACKS011",
        title: "Unsupported [DuckShape<>] usage",
        messageFormat: "'{0}' cannot be derived from '{1}': {2}",
        category: "IfItQuacks",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AdapterCast = new(
        id: "IFITQUACKS012",
        title: "Duck-typed parameter is cast to a concrete type",
        messageFormat: "Whenever the argument doesn't implement '{1}' itself, '{0}' receives a generated adapter instead of the caller's instance, so this {2} '{3}' never succeeds for such calls; use 'Duck.Unwrap({0})' to get the original instance",
        category: "IfItQuacks",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An argument that only matches the interface structurally is wrapped in a generated adapter, and that adapter is what the method receives - " +
                     "for a duck-typed constraint the type argument is the adapter type itself. A cast, 'as' or type pattern for the original type therefore " +
                     "fails for these calls, while it succeeds for arguments implementing the interface. Conversion operators can't help: the compiler only " +
                     "sees the interface or type parameter. 'Duck.Unwrap' returns the original instance in both cases.");

    public static readonly DiagnosticDescriptor InaccessibleType = new(
        id: "IFITQUACKS013",
        title: "Type is not accessible to generated code",
        messageFormat: "Type '{0}' cannot be duck-typed to '{1}': '{2}' is {3}, so the generated code can't name it; declare it 'internal' or 'public'",
        category: "IfItQuacks",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Adapters and interceptors are generated outside the types of your code, so they can only use types the whole assembly can access. " +
                     "A private, protected or private protected nested type, or a file-local type, can't be named there.");
}
