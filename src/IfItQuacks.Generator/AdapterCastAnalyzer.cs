using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace IfItQuacks.Generator;

/// <summary>
/// Reports casts and type tests in a <c>[DuckTyped]</c> method that expect the caller's instance instead of the adapter it receives.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AdapterCastAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Diagnostics.AdapterCast];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static compilationStart =>
        {
            if (compilationStart.Compilation.Assembly.GetTypeByMetadataName(KnownSymbols.DuckTypedAttributeMetadataName) is null)
                return;

            compilationStart.RegisterSymbolStartAction(static symbolStart =>
            {
                var method = (IMethodSymbol)symbolStart.Symbol;
                if (KnownSymbols.IsDuckTyped(method) && DuckTypedMethodValidator.IsValid(method, symbolStart.Compilation))
                    symbolStart.RegisterOperationBlockAction(blocks => ReportAdapterCasts(blocks, method));
            }, SymbolKind.Method);
        });
    }

    private static void ReportAdapterCasts(OperationBlockAnalysisContext context, IMethodSymbol method)
    {
        var adaptedParameters = DuckTypedSignature.GetParametersReceivingAdapters(method);
        foreach (var block in context.OperationBlocks)
        {
            foreach (var diagnostic in AdapterCastFinder.Find(block, adaptedParameters, context.Compilation))
                context.ReportDiagnostic(diagnostic);
        }
    }
}
