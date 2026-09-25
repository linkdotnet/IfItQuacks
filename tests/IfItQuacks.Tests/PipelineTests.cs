using Microsoft.CodeAnalysis;
using Xunit;

namespace IfItQuacks.Tests;

public class PipelineTests
{
    private const string Shapes = """
        using IfItQuacks;

        public interface IDoable { void Do(); }

        public class A { public void Do() { } }

        public static partial class Ops
        {
            [DuckTyped]
            public static void Foo(IDoable a) => a.Do();
        }

        public static class Entry
        {
            public static void Run()
            {
                Ops.Foo(new A());
                IDoable doable = Duck.As<IDoable>(new A());
            }
        }
        """;

    [Fact]
    public void EditingUnrelatedFile_DoesNotRegenerateCallSites()
    {
        var compilation = GeneratorTestHelper.CreateCompilation(OutputKind.DynamicallyLinkedLibrary, null, Shapes, "public class Unrelated { }");
        var driver = GeneratorTestHelper.CreateTrackingDriver().RunGenerators(compilation, TestContext.Current.CancellationToken);

        var unrelatedTree = compilation.SyntaxTrees.Last();
        var edited = compilation.ReplaceSyntaxTree(unrelatedTree,
            GeneratorTestHelper.ParseText("public class Unrelated { public void M() => System.Console.WriteLine(1); }"));
        var result = driver.RunGenerators(edited, TestContext.Current.CancellationToken).GetRunResult().Results.Single();

        var callSiteOutputs = result.TrackedSteps["IfItQuacks.CallSites"].SelectMany(step => step.Outputs).ToList();
        Assert.Equal(2, callSiteOutputs.Count);
        Assert.All(callSiteOutputs, output =>
            Assert.True(output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged, output.Reason.ToString()));
        Assert.All(result.TrackedOutputSteps.SelectMany(step => step.Value).SelectMany(step => step.Outputs), output =>
            Assert.True(output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged, output.Reason.ToString()));
    }

    [Fact]
    public void EditingCallSiteFile_RegeneratesOnlyInterceptors()
    {
        var compilation = GeneratorTestHelper.CreateCompilation(OutputKind.DynamicallyLinkedLibrary, null, Shapes);
        var driver = GeneratorTestHelper.CreateTrackingDriver().RunGenerators(compilation, TestContext.Current.CancellationToken);

        var edited = compilation.ReplaceSyntaxTree(compilation.SyntaxTrees.Single(), GeneratorTestHelper.ParseText(Shapes + "public class Appended { }"));
        var result = driver.RunGenerators(edited, TestContext.Current.CancellationToken).GetRunResult().Results.Single();

        Assert.All(result.TrackedSteps["IfItQuacks.Adapters"].SelectMany(step => step.Outputs), output =>
            Assert.True(output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged, output.Reason.ToString()));
        Assert.Contains(result.TrackedSteps["IfItQuacks.Interceptors"].SelectMany(step => step.Outputs),
            output => output.Reason == IncrementalStepRunReason.Modified);
    }

    [Fact]
    public void EditingUnrelatedFile_DoesNotRegenerateMappedShapes()
    {
        const string mapped = """
            using IfItQuacks;

            public class Customer { public int Id { get; set; } public string Name { get; set; } = ""; }

            [DuckShape<Customer>(Omit = [nameof(Customer.Id)])]
            public partial interface ICustomerView;
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(OutputKind.DynamicallyLinkedLibrary, null, mapped, "public class Unrelated { }");
        var driver = GeneratorTestHelper.CreateTrackingDriver().RunGenerators(compilation, TestContext.Current.CancellationToken);

        var unrelatedTree = compilation.SyntaxTrees.Last();
        var edited = compilation.ReplaceSyntaxTree(unrelatedTree,
            GeneratorTestHelper.ParseText("public class Unrelated { public void M() => System.Console.WriteLine(1); }"));
        var result = driver.RunGenerators(edited, TestContext.Current.CancellationToken).GetRunResult().Results.Single();

        var outputs = result.TrackedSteps["IfItQuacks.MappedShapes"].SelectMany(step => step.Outputs).ToList();
        Assert.NotEmpty(outputs);
        Assert.All(outputs, output =>
            Assert.True(output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged, output.Reason.ToString()));
    }

    [Fact]
    public void ProjectReferencingAnotherProjectUsingIfItQuacks_HasNoTypeConflicts()
    {
        var (library, libraryDiagnostics) = GeneratorTestHelper.RunGenerator(Shapes);
        Assert.Empty(libraryDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        const string consumer = """
            using IfItQuacks;

            public class B { public void Do() { } }

            public static partial class ConsumerOps
            {
                [DuckTyped]
                public static void Bar(IDoable a) => a.Do();
            }

            public static class ConsumerEntry
            {
                public static void Run()
                {
                    ConsumerOps.Bar(new B());
                    IDoable doable = Duck.As<IDoable>(new B());
                }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(consumer, additionalReferences: [library.ToMetadataReference()]);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void GeneratedOverloads_AreHiddenFromIntelliSense()
    {
        const string source = """
            using IfItQuacks;

            public interface IContainer<T> { T Get(); }

            public class IntBox { public int Get() => 42; }

            public static partial class Ops
            {
                [DuckTyped]
                public static void Foo(IContainer<int> c) { }

                [DuckTyped]
                public static T Unwrap<T>(IContainer<T> c) => c.Get();
            }

            public static class Entry
            {
                public static int Run() => Ops.Unwrap(new IntBox());
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generated = compilation.GetTypeByMetadataName("Ops")!.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary && m.GetAttributes().All(a => a.AttributeClass?.Name != "DuckTypedAttribute"))
            .ToList();

        Assert.Equal(2, generated.Count);
        Assert.All(generated, m => Assert.Contains(m.GetAttributes(), a => a.AttributeClass?.Name == "EditorBrowsableAttribute"));
    }
}
