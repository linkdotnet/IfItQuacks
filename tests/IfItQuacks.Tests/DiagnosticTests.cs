using System.Globalization;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IfItQuacks.Tests;

public class DiagnosticTests
{
    [Theory]
    [InlineData("ref")]
    [InlineData("in")]
    [InlineData("out")]
    [InlineData("ref readonly")]
    public void ByReferenceInterfaceParameter_IsNotDuckTyped_ReportsIfItQuacks003(string modifier)
    {
        var source = $$"""
            using IfItQuacks;

            public interface IDoable { void Do(); }

            public static partial class Ops
            {
                [DuckTyped]
                public static void Foo({{modifier}} IDoable a) => throw null!;
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "IFITQUACKS003");
    }

    [Theory]
    [InlineData("public struct Counter")]
    [InlineData("public ref struct Counter")]
    public void MutableOrRefStructArgument_ReportsIfItQuacks006(string declaration)
    {
        var source = $$"""
            using IfItQuacks;

            public interface ICounter { void Increment(); int Count { get; } }

            {{declaration}} { public int Count { get; private set; } public void Increment() => Count++; }

            public static partial class Ops
            {
                [DuckTyped]
                public static int Bump(ICounter c) { c.Increment(); return c.Count; }
            }

            public static class Entry
            {
                public static int Run() => Ops.Bump(new Counter());
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "IFITQUACKS006");
    }

    [Fact]
    public void StructuralMismatch_ReportsIfItQuacks001()
    {
        const string source = """
            using IfItQuacks;

            public interface IDoable { void Do(); }

            public class C { public string NotDo() => ""; }

            public static partial class Ops
            {
                [DuckTyped]
                public static void Foo(IDoable a) => a.Do();
            }

            public static class Entry
            {
                public static void Run() => Ops.Foo(new C());
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "IFITQUACKS001");
    }

    [Theory]
    [InlineData("(Person)(object)n", true)]
    [InlineData("n as Person", true)]
    [InlineData("n is Person", true)]
    [InlineData("n is Person { Name: \"x\" } p", true)]
    [InlineData("n is not Person", true)]
    [InlineData("n switch { Person => 1, _ => 0 }", true)]
    [InlineData("(Person)Duck.Unwrap(n)!", false)]
    [InlineData("(object)n", false)]
    [InlineData("n is INamed", false)]
    [InlineData("n is System.IDisposable", false)]
    [InlineData("n is null", false)]
    [InlineData("n is { Name: string }", false)]
    public void CastOfDuckTypedParameter_ReportsIfItQuacks012(string expression, bool reported)
    {
        var source = $$"""
            using IfItQuacks;

            public interface INamed { string Name { get; } }
            public class Person { public string Name => ""; }

            public static partial class Ops
            {
                [DuckTyped]
                public static object? F(INamed n) => {{expression}};
            }
            """;

        Assert.Equal(reported, GeneratorTestHelper.GetDiagnosticIds(source).Contains("IFITQUACKS012"));
    }

    [Fact]
    public void CastOfConstrainedParameter_ReportsIfItQuacks012_WithExplanation()
    {
        const string source = """
            using IfItQuacks;

            public interface INamed { string Name { get; } }
            public class Person { public string Name => ""; }

            public static partial class Ops
            {
                [DuckTyped]
                public static int F<T>(T named) where T : INamed
                {
                    switch (named)
                    {
                        case Person:
                            return 1;
                    }

                    return ((Person)(object)named).Name.Length;
                }
            }
            """;

        var warnings = GeneratorTestHelper.RunGenerator(source).Diagnostics.Where(d => d.Id == "IFITQUACKS012").ToList();

        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, w => Assert.Equal(DiagnosticSeverity.Warning, w.Severity));
        Assert.Contains(
            "Whenever the argument doesn't implement 'INamed' itself, 'named' receives a generated adapter instead of the caller's instance, " +
            "so this cast to 'Person' never succeeds for such calls; use 'Duck.Unwrap(named)' to get the original instance",
            warnings.Select(w => w.GetMessage(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void NonPartialContainingType_ReportsIfItQuacks002()
    {
        const string source = """
            using IfItQuacks;

            public interface IDoable { void Do(); }

            public static class Ops
            {
                [DuckTyped]
                public static void Foo(IDoable a) => a.Do();
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "IFITQUACKS002");
    }
}
