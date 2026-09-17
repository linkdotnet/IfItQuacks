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
