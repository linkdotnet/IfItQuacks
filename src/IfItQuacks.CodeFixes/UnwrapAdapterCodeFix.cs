using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;

namespace IfItQuacks.CodeFixes;

/// <summary>Casts or tests <c>Duck.Unwrap(parameter)</c> instead of the adapter, as IFITQUACKS012 suggests.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UnwrapAdapterCodeFix)), Shared]
public sealed class UnwrapAdapterCodeFix : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["IFITQUACKS012"];

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
            // The analyzer points its additional location at the parameter reference that is cast or tested.
            if (diagnostic.AdditionalLocations.Count == 0 ||
                root.FindNode(diagnostic.AdditionalLocations[0].SourceSpan, getInnermostNodeForTie: true) is not ExpressionSyntax value)
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Use 'Duck.Unwrap'",
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(value, Unwrap(value)))),
                    nameof(UnwrapAdapterCodeFix)),
                diagnostic);
        }
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax value)
    {
        ExpressionSyntax unwrapped = SyntaxFactory.InvocationExpression(
                SyntaxFactory.ParseExpression("global::IfItQuacks.Duck.Unwrap").WithAdditionalAnnotations(Simplifier.Annotation),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(value.WithoutTrivia()))));

        // Unwrap returns 'object?', which a cast to a non-nullable type would warn about.
        if (value.Parent is CastExpressionSyntax)
            unwrapped = SyntaxFactory.PostfixUnaryExpression(SyntaxKind.SuppressNullableWarningExpression, unwrapped);

        return unwrapped.WithTriviaFrom(value);
    }
}
