using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;

namespace IfItQuacks.CodeFixes;

/// <summary>Renames a member that IFITQUACKS001 found under another casing ("did you mean ...") to the name the interface expects.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RenameMemberCodeFix)), Shared]
public sealed class RenameMemberCodeFix : CodeFixProvider
{
    // Written by the generator as "actual:Expected;..." - keep in sync with Diagnostics.RenamesProperty.
    private const string RenamesProperty = "Renames";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["IFITQUACKS001"];

    /// <inheritdoc />
    public override FixAllProvider? GetFixAllProvider() => null;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        if (!diagnostic.Properties.TryGetValue(RenamesProperty, out var renames) || renames is null)
            return;

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
            return;

        var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        var expression = node is ArgumentSyntax argument ? argument.Expression : node;
        if (semanticModel.GetTypeInfo(expression, context.CancellationToken).Type is not INamedTypeSymbol type)
            return;

        foreach (var rename in renames.Split(';'))
        {
            var names = rename.Split(':');
            if (names.Length != 2 || FindDeclaredMember(type, names[0]) is not { } member)
                continue;

            var solution = context.Document.Project.Solution;
            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Rename '{names[0]}' to '{names[1]}'",
                    ct => Renamer.RenameSymbolAsync(solution, member, new SymbolRenameOptions(), names[1], ct),
                    $"{nameof(RenameMemberCodeFix)}.{names[0]}"),
                diagnostic);
        }
    }

    private static ISymbol? FindDeclaredMember(INamedTypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers(name).FirstOrDefault(m => m.DeclaringSyntaxReferences.Length > 0) is { } member)
                return member;
        }

        return null;
    }
}
