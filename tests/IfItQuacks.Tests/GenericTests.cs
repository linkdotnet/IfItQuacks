using Microsoft.CodeAnalysis;
using Xunit;

namespace IfItQuacks.Tests;

public class GenericTests
{
    [Fact]
    public void GenericShape_GenericMethod_InfersTypeArgumentsPerArgumentType()
    {
        const string source = """
            using IfItQuacks;

            [DuckShape]
            public interface IContainer<T> { T Get(); }

            public class IntBox { public int Get() => 42; }
            public class Box<T>(T v) { public T Get() => v; }

            public static partial class Ops
            {
                [DuckTyped]
                public static T Foo<T>(IContainer<T> c) => c.Get();
            }

            public static class Entry
            {
                public static string Run()
                {
                    int a = Ops.Foo(new IntBox());
                    string b = Ops.Foo(new Box<string>("x"));
                    return a + "|" + b;
                }
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var assembly = GeneratorTestHelper.EmitAndLoad(compilation);
        var result = assembly.GetType("Entry")!.GetMethod("Run")!.Invoke(null, null);
        Assert.Equal("42|x", result);
    }

    [Fact]
    public void ClosedGenericShape_NonGenericMethod_DuckTypes()
    {
        const string source = """
            using IfItQuacks;

            [DuckShape]
            public interface IContainer<T> { T Get(); }

            public class IntBox { public int Get() => 42; }

            public static partial class Ops
            {
                [DuckTyped]
                public static int Twice(IContainer<int> c) => c.Get() * 2;
            }

            public static class Entry
            {
                public static int Run() => Ops.Twice(new IntBox());
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var assembly = GeneratorTestHelper.EmitAndLoad(compilation);
        var result = assembly.GetType("Entry")!.GetMethod("Run")!.Invoke(null, null);
        Assert.Equal(84, result);
    }

    [Fact]
    public void GenericMethod_MultipleTypeParametersAndNestedGenerics_InfersTypeArguments()
    {
        const string source = """
            using IfItQuacks;
            using System.Collections.Generic;

            [DuckShape]
            public interface ILookup<TKey, TValue>
            {
                TKey Key { get; }
                IReadOnlyList<TValue> Values { get; }
            }

            public class Tags
            {
                public string Key => "tags";
                public IReadOnlyList<int> Values { get; } = [1, 2, 3];
            }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Describe<TKey, TValue>(ILookup<TKey, TValue> lookup) =>
                    lookup.Key + "=" + string.Join(",", lookup.Values);
            }

            public static class Entry
            {
                public static string Run() => Ops.Describe(new Tags());
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var assembly = GeneratorTestHelper.EmitAndLoad(compilation);
        var result = assembly.GetType("Entry")!.GetMethod("Run")!.Invoke(null, null);
        Assert.Equal("tags=1,2,3", result);
    }

    [Fact]
    public void GenericMethod_TypeThatAlreadyImplementsShape_StillWorks()
    {
        const string source = """
            using IfItQuacks;

            [DuckShape]
            public interface IContainer<T> { T Get(); }

            public class RealBox : IContainer<int> { public int Get() => 7; }
            public class IntBox { public int Get() => 42; }

            public static partial class Ops
            {
                [DuckTyped]
                public static T Foo<T>(IContainer<T> c) => c.Get();
            }

            public static class Entry
            {
                public static int Run() => Ops.Foo(new RealBox()) + Ops.Foo(new IntBox());
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var assembly = GeneratorTestHelper.EmitAndLoad(compilation);
        var result = assembly.GetType("Entry")!.GetMethod("Run")!.Invoke(null, null);
        Assert.Equal(49, result);
    }

    [Fact]
    public void GenericMethod_StructuralMismatch_ReportsIfItQuacks001()
    {
        const string source = """
            using IfItQuacks;

            [DuckShape]
            public interface IContainer<T> { T Get(); }

            public class Rock { }

            public static partial class Ops
            {
                [DuckTyped]
                public static T Foo<T>(IContainer<T> c) => c.Get();
            }

            public static class Entry
            {
                public static void Run() => Ops.Foo(new Rock());
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "IFITQUACKS001");
    }

    [Fact]
    public void GenericMethod_TypeParameterNotUsedByParameter_ReportsIfItQuacks004()
    {
        const string source = """
            using IfItQuacks;

            [DuckShape]
            public interface IDoable { void Do(); }

            public static partial class Ops
            {
                [DuckTyped]
                public static T Foo<T>(IDoable a) => default!;
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "IFITQUACKS004");
    }

    [Fact]
    public void GenericMethod_MutableStructArgument_ReportsIfItQuacks006()
    {
        const string source = """
            using IfItQuacks;

            [DuckShape]
            public interface IContainer<T> { T Get(); }

            public struct IntBox { public int Value; public int Get() => Value; }

            public static partial class Ops
            {
                [DuckTyped]
                public static T Foo<T>(IContainer<T> c) => c.Get();
            }

            public static class Entry
            {
                public static int Run() => Ops.Foo(new IntBox());
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "IFITQUACKS006");
    }
}
