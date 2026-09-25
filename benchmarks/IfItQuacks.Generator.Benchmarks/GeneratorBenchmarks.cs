using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace IfItQuacks.Generator.Benchmarks;

public enum Scenario
{
    NoDuckTyping,
    DistinctTypes,
    SameTypeCallSites,
    DuckAs,
    PlainCode,
}

[MemoryDiagnoser]
public class GeneratorBenchmarks
{
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Preview)
        .WithFeatures([
            new KeyValuePair<string, string>("InterceptorsPreviewNamespaces", "IfItQuacks.Generated"),
            new KeyValuePair<string, string>("InterceptorsNamespaces", "IfItQuacks.Generated"),
        ]);

    private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(path => path.Contains("Microsoft.NETCore.App", StringComparison.Ordinal))
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToArray();

    private CSharpCompilation _compilation = null!;
    private CSharpCompilation _edited = null!;
    private CSharpCompilation _editedCallSiteFile = null!;
    private GeneratorDriver _warmDriver = null!;

    [Params(1, 10, 100, 1000)]
    public int N { get; set; }

    [ParamsAllValues]
    public Scenario Scenario { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _compilation = CSharpCompilation.Create(
            "Bench",
            [Parse(Source(Scenario, N)), Parse("public class Unrelated { }")],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        _edited = _compilation.ReplaceSyntaxTree(
            _compilation.SyntaxTrees[^1],
            Parse("public class Unrelated { public void M() => System.Console.WriteLine(1); }"));

        _editedCallSiteFile = _compilation.ReplaceSyntaxTree(
            _compilation.SyntaxTrees[0],
            Parse(Source(Scenario, N) + "public static class Appended { }"));

        _warmDriver = CreateDriver().RunGenerators(_compilation);

        CreateDriver().RunGeneratorsAndUpdateCompilation(_compilation, out var output, out var generatorDiagnostics);
        var errors = generatorDiagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }
    }

    // Clone drops the bound symbols a previous iteration cached, so each run pays the full cost of a fresh build.
    [Benchmark]
    public GeneratorDriver ColdRun() => CreateDriver().RunGenerators(_compilation.Clone());

    [Benchmark]
    public GeneratorDriver IncrementalRun() => _warmDriver.RunGenerators(_edited);

    // Typing in a file with call sites changes its checksum, so every interceptor location in it changes.
    [Benchmark]
    public GeneratorDriver IncrementalEditCallSiteFile() => _warmDriver.RunGenerators(_editedCallSiteFile);

    [Benchmark(Baseline = true)]
    public int CompileWithoutGenerator() => _compilation.Clone().GetDiagnostics().Length;

    [Benchmark]
    public int CompileWithGenerator()
    {
        CreateDriver().RunGeneratorsAndUpdateCompilation(_compilation.Clone(), out var output, out _);
        return output.GetDiagnostics().Length;
    }

    private static CSharpGeneratorDriver CreateDriver() =>
        CSharpGeneratorDriver.Create([new IfItQuacksGenerator().AsSourceGenerator()], parseOptions: ParseOptions);

    private static SyntaxTree Parse(string source) => CSharpSyntaxTree.ParseText(source, ParseOptions);

    private static string Source(Scenario scenario, int n)
    {
        var source = new StringBuilder("""
            using IfItQuacks;

            public interface INamed { string Name { get; } }

            public static partial class Greeter
            {
                [DuckTyped]
                public static string Greet(INamed named) => named.Name;

                public static string Plain(object value) => value.ToString()!;
            }

            """);

        var typeCount = scenario == Scenario.SameTypeCallSites ? 1 : n;
        for (var i = 0; i < typeCount; i++)
        {
            source.AppendLine(CultureInfo.InvariantCulture, $"public class Type{i} {{ public string Name => \"{i}\"; }}");
        }

        source.AppendLine("public static class Entry {");
        for (var i = 0; i < n; i++)
        {
            if (scenario == Scenario.PlainCode)
            {
                source.AppendLine(CultureInfo.InvariantCulture,
                    $"public static object Run{i}(int value) {{ var doubled = value * 2; var total = doubled + value; return Greeter.Plain(total > value ? total : doubled); }}");
                continue;
            }

            var call = scenario switch
            {
                Scenario.NoDuckTyping => $"Greeter.Plain(new Type{i}());",
                Scenario.DistinctTypes => $"Greeter.Greet(new Type{i}());",
                Scenario.SameTypeCallSites => "Greeter.Greet(new Type0());",
                Scenario.DuckAs => $"Duck.As<INamed>(new Type{i}());",
                _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
            };
            source.AppendLine(CultureInfo.InvariantCulture, $"public static object Run{i}() => {call}");
        }

        return source.AppendLine("}").ToString();
    }
}
