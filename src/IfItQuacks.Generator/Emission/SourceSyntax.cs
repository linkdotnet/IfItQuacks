using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace IfItQuacks.Generator;

internal static class SourceSyntax
{
    public static string AccessibilityKeyword(Accessibility a) => a switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Private => "private",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => "internal",
    };

    public static string RefKindPrefix(RefKind kind) => kind switch
    {
        RefKind.Ref => "ref ",
        RefKind.Out => "out ",
        RefKind.In => "in ",
        RefKind.RefReadOnlyParameter => "ref readonly ",
        _ => "",
    };

    public static string RefReturnPrefix(RefKind kind) => kind switch
    {
        RefKind.Ref => "ref ",
        RefKind.RefReadOnly => "ref readonly ",
        _ => "",
    };

    public static string RefKindArgumentPrefix(RefKind kind) => kind switch
    {
        RefKind.Ref => "ref ",
        RefKind.Out => "out ",
        RefKind.In or RefKind.RefReadOnlyParameter => "in ",
        _ => "",
    };

    public static string Identifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    public static string Parameter(IParameterSymbol parameter, string type) =>
        $"{(parameter.IsParams ? "params " : "")}{RefKindPrefix(parameter.RefKind)}{type} {Identifier(parameter.Name)}";

    // An extension method's first parameter keeps its 'this', so a generated overload can still be called on a receiver.
    public static string DeclareParameter(IMethodSymbol method, IParameterSymbol parameter, string type) =>
        (method.IsExtensionMethod && parameter.Ordinal == 0 ? "this " : "") + Parameter(parameter, type);

    public static string Argument(IParameterSymbol parameter) =>
        $"{RefKindArgumentPrefix(parameter.RefKind)}{Identifier(parameter.Name)}";

    public static string DefaultValue(IParameterSymbol parameter)
    {
        if (!parameter.HasExplicitDefaultValue)
            return "";

        if (parameter.ExplicitDefaultValue is null)
            return " = default";

        var literal = SymbolDisplay.FormatPrimitive(parameter.ExplicitDefaultValue, quoteStrings: true, useHexadecimalNumbers: false);
        return $" = ({parameter.Type.ToDisplayString()})({literal})";
    }
}
