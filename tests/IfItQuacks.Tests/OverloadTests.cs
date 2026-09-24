using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace IfItQuacks.Tests;

public class OverloadTests
{
    [Fact]
    public void ObjectOverload_TakesNonMatchingArguments_AndStructuralMatchGoesToDuckTypedMethod()
    {
        const string source = """
            using IfItQuacks;

            public interface INamed { string Name { get; } }
            public class Person { public string Name => "Steven"; }
            public class Car { public int Wheels => 4; public override string ToString() => "Car"; }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Greet(INamed n) => $"duck:{n.Name}";

                public static string Greet(object o) => $"object:{o}";
            }

            public static class Entry
            {
                public static string Run() => string.Join(",", Ops.Greet(new Person()), Ops.Greet(42), Ops.Greet(new Car()));
            }
            """;

        Assert.Equal("duck:Steven,object:42,object:Car", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ExactOverload_WinsOverDuckTypedMethod()
    {
        const string source = """
            using IfItQuacks;

            public interface INamed { string Name { get; } }
            public class Person { public string Name => "Steven"; }
            public class Robot { public string Name => "R2"; }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Greet(INamed n) => $"duck:{n.Name}";

                public static string Greet(Person p) => $"person:{p.Name}";
            }

            public static class Entry
            {
                public static string Run() => string.Join(",", Ops.Greet(new Person()), Ops.Greet(new Robot()));
            }
            """;

        Assert.Equal("person:Steven,duck:R2", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ArgumentImplementingInterface_GoesToDuckTypedMethod()
    {
        const string source = """
            using IfItQuacks;

            public interface INamed { string Name { get; } }
            public class Named : INamed { public string Name => "Real"; }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Greet(INamed n) => $"duck:{n.Name}";

                public static string Greet(object o) => "object";
            }

            public static class Entry
            {
                public static string Run() => Ops.Greet(new Named());
            }
            """;

        Assert.Equal("duck:Real", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void OverloadWithMoreParameters_RedirectsWithDefaults_UnlessNamedArgumentOnlyFitsOverload()
    {
        const string source = """
            using IfItQuacks;

            public interface INamed { string Name { get; } }
            public class Person { public string Name => "Steven"; }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Greet(INamed n, int times = 2, string suffix = "!") => $"duck:{n.Name}x{times}{suffix}";

                public static string Greet(object o, int times) => $"object:{times}";
            }

            public static class Entry
            {
                public static string Run() => string.Join(",", Ops.Greet(new Person(), 3), Ops.Greet(times: 1, o: new Person()), Ops.Greet(42, 5));
            }
            """;

        Assert.Equal("duck:Stevenx3!,object:1,object:5", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void InstanceMethodOverload_IsRedirectedOnlyWhereDuckTypedMethodIsAccessible()
    {
        const string source = """
            using IfItQuacks;

            public interface INamed { string Name { get; } }
            public class Person { public string Name => "Steven"; }

            public partial class Greeter
            {
                private readonly string prefix = "hi";

                [DuckTyped]
                private string Greet(INamed n) => $"{prefix} duck:{n.Name}";

                public string Greet(object o) => $"{prefix} object:{o}";

                public string Both() => Greet(new Person()) + "," + Greet(42);
            }

            public static class Entry
            {
                public static string Run() => new Greeter().Greet(new Person()) + "," + new Greeter().Both();
            }
            """;

        Assert.Equal("hi object:Person,hi duck:Steven,hi object:42", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void OverloadWithoutPrioritySupport_ReportsIfItQuacks004()
    {
        const string source = """
            using IfItQuacks;

            public interface INamed { string Name { get; } }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Greet(INamed n) => n.Name;

                public static string Greet(object o) => "object";
            }

            public static class Entry
            {
                public static string Run() => Ops.Greet(42);
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.RunGenerator(source, languageVersion: LanguageVersion.CSharp12);

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "IFITQUACKS004");
        Assert.Contains("Greet(object", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.DoesNotContain(compilation.SyntaxTrees, t => t.FilePath.Contains("Fallback", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Id != "IFITQUACKS004");
    }

    [Fact]
    public void GenericOverloadWithFallbackSignature_ReportsIfItQuacks004()
    {
        const string source = """
            using IfItQuacks;

            public interface INamed { string Name { get; } }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Greet(INamed n) => n.Name;

                public static string Greet<T>(T value) => "generic";
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "IFITQUACKS004");
        Assert.Contains("Greet<T>(T", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.DoesNotContain(compilation.SyntaxTrees, t => t.FilePath.Contains("Fallback", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Id != "IFITQUACKS004");
    }

    [Fact]
    public void GenericOverloadMatchingOnePartialFallbackVariant_ReportsIfItQuacks004()
    {
        const string source = """
            using IfItQuacks;

            public interface INamed { string Name { get; } }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Pair(INamed first, INamed second) => first.Name + second.Name;

                public static string Pair<T>(T first, INamed second) => "generic";
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Single(diagnostics, d => d.Id == "IFITQUACKS004");
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Id != "IFITQUACKS004");
    }

    [Fact]
    public void GenericOverloadWithDifferentSignature_KeepsFallback()
    {
        const string source = """
            using IfItQuacks;

            public interface INamed { string Name { get; } }
            public class Person { public string Name => "Steven"; }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Greet(INamed n) => $"duck:{n.Name}";

                public static string Greet<T>(T value, int times) => $"generic:{times}";

                public static string Greet<T>(System.Collections.Generic.List<T> values) => "list";
            }

            public static class Entry
            {
                public static string Run() => string.Join(",", Ops.Greet(new Person()), Ops.Greet(42, 2), Ops.Greet(new System.Collections.Generic.List<int>()));
            }
            """;

        Assert.Equal("duck:Steven,generic:2,list", GeneratorTestHelper.CompileAndRun(source));
    }

    [Theory]
    [InlineData("public static string Log(INamed n, ref int x) => n.Name;", "public static string Log<T>(T n, out int x) { x = 0; return \"generic\"; }")]
    [InlineData("public static string Log(int count, INamed n) => n.Name;", "public static string Log<T>(int count, T n) => \"generic\";")]
    public void GenericOverloadWithFallbackSignature_AfterOtherParameters_ReportsIfItQuacks004(string duckMethod, string overload)
    {
        var source = $$"""
            using IfItQuacks;

            public interface INamed { string Name { get; } }

            public static partial class Ops
            {
                [DuckTyped]
                {{duckMethod}}

                {{overload}}
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Single(diagnostics, d => d.Id == "IFITQUACKS004");
        Assert.DoesNotContain(diagnostics, d => d.Id != "IFITQUACKS004");
    }
}
