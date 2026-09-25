using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace IfItQuacks.Generator;

internal static class GeneratedSourceWriter
{
    public static void Write(SourceProductionContext context, DuckTypedMethodOutput method)
    {
        Report(context, method.Diagnostics);
        if (method.Fallback is not null)
            Add(context, method.Fallback);
    }

    public static void Write(SourceProductionContext context, MappedShapeOutput shape)
    {
        Report(context, shape.Diagnostics);
        if (shape.File is not null)
            Add(context, shape.File);
    }

    public static void Report(SourceProductionContext context, EquatableArray<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
            context.ReportDiagnostic(diagnostic);
    }

    public static void Add(SourceProductionContext context, EquatableArray<GeneratedFile> files)
    {
        foreach (var file in files)
            Add(context, file);
    }

    private static void Add(SourceProductionContext context, GeneratedFile file) =>
        context.AddSource(file.Name, SourceText.From(file.Source, Encoding.UTF8));
}
