using Microsoft.CodeAnalysis;
using Xunit;

namespace IfItQuacks.Tests;

public class IfItQuacksGeneratorTests
{
    [Fact]
    public void MethodShape_DuckTypesUnrelatedClasses_WithoutSharedInterface()
    {
        const string source = """
            using IfItQuacks;
            using System.Text;

            [DuckShape]
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

            [DuckShape]
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

            [DuckShape]
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

    [Theory]
    [InlineData("ref")]
    [InlineData("in")]
    [InlineData("out")]
    [InlineData("ref readonly")]
    public void ByReferenceParameter_ReportsIfItQuacks004(string modifier)
    {
        var source = $$"""
            using IfItQuacks;

            [DuckShape]
            public interface IDoable { void Do(); }

            public static partial class Ops
            {
                [DuckTyped]
                public static void Foo({{modifier}} IDoable a) => throw null!;
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "IFITQUACKS004");
    }

    [Theory]
    [InlineData("public struct Counter")]
    [InlineData("public ref struct Counter")]
    public void MutableOrRefStructArgument_ReportsIfItQuacks006(string declaration)
    {
        var source = $$"""
            using IfItQuacks;

            [DuckShape]
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
    public void ReadonlyStructArgument_DuckTypes()
    {
        const string source = """
            using IfItQuacks;

            [DuckShape]
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
    public void StructuralMismatch_ReportsIfItQuacks001()
    {
        const string source = """
            using IfItQuacks;

            [DuckShape]
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

            [DuckShape]
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

    [Fact]
    public void NonShapeParameter_ReportsIfItQuacks003()
    {
        const string source = """
            using IfItQuacks;

            public interface IDoable { void Do(); }

            public static partial class Ops
            {
                [DuckTyped]
                public static void Foo(IDoable a) => a.Do();
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "IFITQUACKS003");
    }

    [Fact]
    public void TypeThatAlreadyImplementsShape_StillDispatchesCorrectly()
    {
        const string source = """
            using IfItQuacks;

            [DuckShape]
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
