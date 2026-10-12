using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IfItQuacks.CodeFixes;

/// <summary>Adds the missing <c>partial</c> modifiers that IFITQUACKS002 and IFITQUACKS010 report.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MakePartialCodeFix)), Shared]
public sealed class MakePartialCodeFix : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["IFITQUACKS002", "IFITQUACKS010"];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            // The generated declaration extends the type and every type around it, so all of them have to be partial.
            var types = root.FindNode(diagnostic.Location.SourceSpan).AncestorsAndSelf()
                .OfType<TypeDeclarationSyntax>()
                .Where(t => !t.Modifiers.Any(SyntaxKind.PartialKeyword))
                .ToList();
            if (types.Count == 0)
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Make '{types[0].Identifier.Text}' partial",
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNodes(types, (_, rewritten) => AddPartial(rewritten)))),
                    nameof(MakePartialCodeFix)),
                diagnostic);
        }
    }

    // 'partial' has to come right before the type keyword, so it goes after every other modifier.
    private static TypeDeclarationSyntax AddPartial(TypeDeclarationSyntax type)
    {
        if (type.Modifiers.Count > 0)
            return type.AddModifiers(SyntaxFactory.Token(SyntaxKind.PartialKeyword).WithTrailingTrivia(SyntaxFactory.Space));

        var partial = SyntaxFactory.Token(type.Keyword.LeadingTrivia, SyntaxKind.PartialKeyword, SyntaxFactory.TriviaList(SyntaxFactory.Space));
        return type.WithKeyword(type.Keyword.WithLeadingTrivia()).WithModifiers(SyntaxFactory.TokenList(partial));
    }
}
