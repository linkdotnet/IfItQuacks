using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IfItQuacks.Generator;

internal static class MappedShapeAnalysis
{
    public static MappedShapeOutput Analyze(GeneratorAttributeSyntaxContext context)
    {
        var target = (INamedTypeSymbol)context.TargetSymbol;
        var location = context.TargetNode.GetLocation();
        if (FindUnsupportedTarget(target, context.TargetNode) is { } reason)
            return new MappedShapeOutput(null, new EquatableArray<Diagnostic>([Diagnostic.Create(Diagnostics.MappedShapeNotPartialInterface, location, target.Name, reason)]));

        var options = context.Attributes.Select(MappedShapeOptions.Read).OfType<MappedShapeOptions>().ToImmutableArray();
        if (options.IsEmpty)
            return new MappedShapeOutput(null, default);

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var members = MappedShapeEmitter.Emit(target, options, diagnostics, location);
        if (members is null)
            return new MappedShapeOutput(null, new EquatableArray<Diagnostic>(diagnostics.ToImmutable()));

        var file = new GeneratedFile(
            GeneratedCode.HintName("Shape", target),
            GeneratedCode.Header + TypeWrapper.WrapInContainingScope(target, members.TrimEnd('\n', '\r')));
        return new MappedShapeOutput(file, default);
    }

    // The generated members go into a second declaration of the interface, so it and every type around it has to be partial.
    private static string? FindUnsupportedTarget(INamedTypeSymbol target, SyntaxNode node)
    {
        if (node is InterfaceDeclarationSyntax declaration && !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            return "it is not declared 'partial'";

        if (target.IsInGenericType())
            return "its containing type is generic";

        if (target.IsInFileLocalType())
            return "its containing type is file-local";

        return target.EnclosingTypes().Skip(1).Any(t => !t.IsDeclaredPartial()) ? "one of its containing types is not declared 'partial'" : null;
    }
}
