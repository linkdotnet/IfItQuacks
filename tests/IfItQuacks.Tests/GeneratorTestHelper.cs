using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using IfItQuacks.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace IfItQuacks.Tests;

internal static class GeneratorTestHelper
{
    private static readonly IReadOnlyList<MetadataReference> References = BuildReferences();

    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Preview)
        .WithFeatures([new KeyValuePair<string, string>("InterceptorsPreviewNamespaces", "IfItQuacks.Generated")]);

    public static (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(string source, OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        IEnumerable<MetadataReference>? additionalReferences = null)
    {
        var compilation = CreateCompilation(outputKind, additionalReferences, source);

        var generator = new IfItQuacksGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create([generator.AsSourceGenerator()], parseOptions: ParseOptions);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        var allDiagnostics = outputCompilation.GetDiagnostics()
            .Concat(generatorDiagnostics)
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .ToImmutableArray();

        return (outputCompilation, allDiagnostics);
    }

    public static CSharpCompilation CreateCompilation(OutputKind outputKind, IEnumerable<MetadataReference>? additionalReferences, params string[] sources) =>
        CSharpCompilation.Create(
            assemblyName: "IfItQuacks.Tests.Generated." + Guid.NewGuid().ToString("N"),
            syntaxTrees: sources.Select(source => CSharpSyntaxTree.ParseText(source, ParseOptions)),
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
        return AssemblyLoadContext.Default.LoadFromStream(stream);
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
