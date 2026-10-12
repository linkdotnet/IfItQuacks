using System.Reflection;
using IfItQuacks.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace IfItQuacks.Tests;

internal static class CodeFixTestHelper
{
    public static async Task<string> ApplyFix(CodeFixProvider fixer, string source, string diagnosticId, string? title = null)
    {
        var actions = await GetFixes(fixer, source, diagnosticId);
        var action = title is null ? actions.Actions[0] : Assert.Single(actions.Actions, a => a.Title == title);
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        return (await solution.GetDocument(actions.Document.Id)!.GetTextAsync()).ToString();
    }

    public static async Task<IReadOnlyList<string>> GetFixTitles(CodeFixProvider fixer, string source, string diagnosticId) =>
        [.. (await GetFixes(fixer, source, diagnosticId)).Actions.Select(a => a.Title)];

    private static async Task<(Document Document, List<CodeAction> Actions)> GetFixes(CodeFixProvider fixer, string source, string diagnosticId)
    {
        Assert.Contains(diagnosticId, fixer.FixableDiagnosticIds);

        using var workspace = new AdhocWorkspace();
        var projectInfo = ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "Test", "Test", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: GeneratorTestHelper.ParseOptions,
            metadataReferences: GeneratorTestHelper.References);
        var project = workspace.AddProject(projectInfo);
        var document = workspace.AddDocument(project.Id, "Test.cs", SourceText.From(source));

        var compilation = (await document.Project.GetCompilationAsync())!;
        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(compilation, GeneratorTestHelper.ParseOptions);
        var tree = await document.GetSyntaxTreeAsync();
        var diagnostic = diagnostics.First(d => d.Id == diagnosticId && d.Location.SourceTree == tree);

        // The fix runs where the IDE runs it: in a project whose compilation contains the generated 'Duck' class.
        var generator = new AnalyzerFileReference(typeof(IfItQuacksGenerator).Assembly.Location, new LoadedAssemblyLoader());
        document = document.Project.AddAnalyzerReference(generator).GetDocument(document.Id)!;

        var actions = new List<CodeAction>();
        await fixer.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        return (document, actions);
    }

    private sealed class LoadedAssemblyLoader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
        }

        public Assembly LoadFromPath(string fullPath) => typeof(IfItQuacksGenerator).Assembly;
    }
}
