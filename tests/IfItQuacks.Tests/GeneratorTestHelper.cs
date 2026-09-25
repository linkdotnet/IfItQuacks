using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using IfItQuacks.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Xunit;

namespace IfItQuacks.Tests;

internal static class GeneratorTestHelper
{
    private static readonly IReadOnlyList<MetadataReference> References = BuildReferences();

    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest)
        .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "IfItQuacks.Generated")]);

    public static (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(string source, OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        IEnumerable<MetadataReference>? additionalReferences = null, LanguageVersion languageVersion = LanguageVersion.Latest)
    {
        var parseOptions = ParseOptions.WithLanguageVersion(languageVersion);
        var compilation = CreateCompilation(outputKind, additionalReferences, parseOptions, source);

        var generator = new IfItQuacksGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create([generator.AsSourceGenerator()], parseOptions: parseOptions);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        var allDiagnostics = outputCompilation.WithAnalyzers([new AdapterCastAnalyzer()]).GetAllDiagnosticsAsync().GetAwaiter().GetResult()
            .Concat(generatorDiagnostics)
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .ToImmutableArray();

        return (outputCompilation, allDiagnostics);
    }

    public static object? CompileAndRun(string source, string typeName = "Entry", string methodName = "Run")
    {
        var (compilation, diagnostics) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        return EmitAndLoad(compilation).GetType(typeName)!.GetMethod(methodName)!.Invoke(null, null);
    }

    public static ImmutableArray<string> GetDiagnosticIds(string source) =>
        RunGenerator(source).Diagnostics.Select(d => d.Id).ToImmutableArray();

    public static CSharpCompilation CreateCompilation(OutputKind outputKind, IEnumerable<MetadataReference>? additionalReferences, params string[] sources) =>
        CreateCompilation(outputKind, additionalReferences, ParseOptions, sources);

    private static CSharpCompilation CreateCompilation(OutputKind outputKind, IEnumerable<MetadataReference>? additionalReferences,
        CSharpParseOptions parseOptions, params string[] sources) =>
        CSharpCompilation.Create(
            assemblyName: "IfItQuacks.Tests.Generated." + Guid.NewGuid().ToString("N"),
            syntaxTrees: sources.Select(source => CSharpSyntaxTree.ParseText(source, parseOptions)),
            references: References.Concat(additionalReferences ?? []),
            options: new CSharpCompilationOptions(outputKind, allowUnsafe: true));

    public static GeneratorDriver CreateTrackingDriver() =>
        CSharpGeneratorDriver.Create(
            [new IfItQuacksGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    public static SyntaxTree ParseText(string source) => CSharpSyntaxTree.ParseText(source, ParseOptions);

    public static Assembly EmitAndLoad(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            var errors = string.Join(Environment.NewLine,
                result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException("Compilation failed:" + Environment.NewLine + errors);
        }

        stream.Seek(0, SeekOrigin.Begin);
        return new AssemblyLoadContext(compilation.AssemblyName, isCollectible: true).LoadFromStream(stream);
    }

    private static List<MetadataReference> BuildReferences()
    {
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);

        return trustedAssemblies
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
    }
}
