using Microsoft.CodeAnalysis;
using Xunit;

namespace IfItQuacks.Tests;

public class DuckTypedCallTests
{
    [Fact]
    public void MethodShape_DuckTypesUnrelatedClasses_WithoutSharedInterface()
    {
        const string source = """
            using IfItQuacks;
            using System.Text;

            public interface IDoable { void Do(); }

            public class A { public void Do() { Sink.Log.Append("A.Do;"); } }
            public class B { public void Do() { Sink.Log.Append("B.Do;"); } }

            public static class Sink { public static StringBuilder Log = new(); }

            public static partial class Ops
            {
                [DuckTyped]
                public static void Foo(IDoable a) => a.Do();
            }

            public static class Entry
            {
                public static string Run()
                {
                    Ops.Foo(new A());
                    Ops.Foo(new B());
                    return Sink.Log.ToString();
                }
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var assembly = GeneratorTestHelper.EmitAndLoad(compilation);

        var typeA = assembly.GetType("A")!;
        var typeB = assembly.GetType("B")!;
        Assert.Empty(typeA.GetInterfaces());
        Assert.Empty(typeB.GetInterfaces());

        var result = assembly.GetType("Entry")!.GetMethod("Run")!.Invoke(null, null);
        Assert.Equal("A.Do;B.Do;", result);
    }

    [Fact]
    public void TopLevelStatements_DuckTypesUnrelatedClasses()
    {
        const string source = """
            using IfItQuacks;
            using System.Text;

            Ops.Foo(new A());
            Ops.Foo(new B());

            public interface IDoable { void Do(); }

            public class A { public void Do() { Sink.Log.Append("A.Do;"); } }
            public class B { public void Do() { Sink.Log.Append("B.Do;"); } }

            public static class Sink { public static StringBuilder Log = new(); }

            public static partial class Ops
            {
                [DuckTyped]
                public static void Foo(IDoable a) => a.Do();
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.RunGenerator(source, OutputKind.ConsoleApplication);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var assembly = GeneratorTestHelper.EmitAndLoad(compilation);
        assembly.EntryPoint!.Invoke(null, [Array.Empty<string>()]);

        var log = assembly.GetType("Sink")!.GetField("Log")!.GetValue(null);
        Assert.Equal("A.Do;B.Do;", log!.ToString());
    }

    [Fact]
    public void PropertyShape_DuckTypesReadWriteProperty()
    {
        const string source = """
            using IfItQuacks;

            public interface INameable { string Name { get; set; } }

            public class Person { public string Name { get; set; } = ""; }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Greet(INameable n)
                {
                    n.Name = n.Name.ToUpperInvariant();
                    return "Hello, " + n.Name;
                }
            }

            public static class Entry
            {
                public static string Run()
                {
                    var person = new Person { Name = "steven" };
                    return Ops.Greet(person);
                }
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var assembly = GeneratorTestHelper.EmitAndLoad(compilation);
        var result = assembly.GetType("Entry")!.GetMethod("Run")!.Invoke(null, null);
        Assert.Equal("Hello, STEVEN", result);
    }

    [Fact]
    public void ReadonlyStructArgument_DuckTypes()
    {
        const string source = """
            using IfItQuacks;

            public interface IGreeter { string Greet(string name); }

            public readonly struct Greeter { public string Greet(string name) => "Hello, " + name; }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Welcome(IGreeter g) => g.Greet("Steven");
            }

            public static class Entry
            {
                public static string Run() => Ops.Welcome(new Greeter());
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var assembly = GeneratorTestHelper.EmitAndLoad(compilation);
        var result = assembly.GetType("Entry")!.GetMethod("Run")!.Invoke(null, null);
        Assert.Equal("Hello, Steven", result);
    }

    [Fact]
    public void TypeThatAlreadyImplementsShape_StillDispatchesCorrectly()
    {
        const string source = """
            using IfItQuacks;

            public interface IDoable { void Do(); }

            public class RealImpl : IDoable { public void Do() { } }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Foo(IDoable a) { a.Do(); return "ok"; }
            }

            public static class Entry
            {
                public static string Run() => Ops.Foo(new RealImpl());
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var assembly = GeneratorTestHelper.EmitAndLoad(compilation);
        var result = assembly.GetType("Entry")!.GetMethod("Run")!.Invoke(null, null);
        Assert.Equal("ok", result);
    }
}
