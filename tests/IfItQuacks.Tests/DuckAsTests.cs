using Microsoft.CodeAnalysis;
using Xunit;

namespace IfItQuacks.Tests;

public class DuckAsTests
{
    [Fact]
    public void DuckAs_ConvertsUnrelatedClasses_SoTheyCanBeStored()
    {
        const string source = """
            using IfItQuacks;
            using System.Collections.Generic;
            using System.Linq;

            public interface IDoable { string Do(); }

            public class A { public string Do() => "A"; }
            public class B { public string Do() => "B"; }

            public static class Entry
            {
                public static string Run()
                {
                    List<IDoable> doables = [Duck.As<IDoable>(new A()), Duck.As<IDoable>(new B())];
                    return string.Join(",", doables.Select(d => d.Do()));
                }
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var assembly = GeneratorTestHelper.EmitAndLoad(compilation);
        var result = assembly.GetType("Entry")!.GetMethod("Run")!.Invoke(null, null);
        Assert.Equal("A,B", result);
    }

    [Fact]
    public void DuckAs_ForwardsToTheOriginalInstance()
    {
        const string source = """
            using IfItQuacks;

            public interface INameable { string Name { get; set; } }

            public class Person { public string Name { get; set; } = ""; }

            public static class Entry
            {
                public static string Run()
                {
                    var person = new Person { Name = "steven" };
                    var nameable = Duck.As<INameable>(person);
                    nameable.Name = "Steven";
                    return person.Name;
                }
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var assembly = GeneratorTestHelper.EmitAndLoad(compilation);
        var result = assembly.GetType("Entry")!.GetMethod("Run")!.Invoke(null, null);
        Assert.Equal("Steven", result);
    }

    [Fact]
    public void DuckAs_ClosedGenericShape_WithReadonlyStruct()
    {
        const string source = """
            using IfItQuacks;

            public interface IContainer<T> { T Get(); }

            public readonly struct IntBox(int value) { public int Get() => value; }

            public static class Entry
            {
                public static int Run() => Duck.As<IContainer<int>>(new IntBox(42)).Get();
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var assembly = GeneratorTestHelper.EmitAndLoad(compilation);
        var result = assembly.GetType("Entry")!.GetMethod("Run")!.Invoke(null, null);
        Assert.Equal(42, result);
    }

    [Fact]
    public void DuckAs_TypeThatAlreadyImplementsShape_ReturnsSameInstance()
    {
        const string source = """
            using IfItQuacks;

            public interface IDoable { void Do(); }

            public class RealImpl : IDoable { public void Do() { } }

            public static class Entry
            {
                public static bool Run()
                {
                    var impl = new RealImpl();
                    return ReferenceEquals(impl, Duck.As<IDoable>(impl));
                }
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var assembly = GeneratorTestHelper.EmitAndLoad(compilation);
        var result = assembly.GetType("Entry")!.GetMethod("Run")!.Invoke(null, null);
        Assert.True((bool)result!);
    }

    [Fact]
    public void DuckAs_StructuralMismatch_ReportsIfItQuacks001()
    {
        const string source = """
            using IfItQuacks;

            public interface IDoable { void Do(); }

            public class Rock { }

            public static class Entry
            {
                public static IDoable Run() => Duck.As<IDoable>(new Rock());
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "IFITQUACKS001");
    }

    [Fact]
    public void DuckAs_ClassTarget_ReportsIfItQuacks007()
    {
        const string source = """
            using IfItQuacks;

            public class A { }

            public static class Entry
            {
                public static string Run() => Duck.As<string>(new A());
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "IFITQUACKS007");
    }

    [Fact]
    public void DuckAs_MutableStruct_ReportsIfItQuacks006()
    {
        const string source = """
            using IfItQuacks;

            public interface ICounter { void Increment(); }

            public struct Counter { public int Count; public void Increment() => Count++; }

            public static class Entry
            {
                public static ICounter Run() => Duck.As<ICounter>(new Counter());
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "IFITQUACKS006");
    }

    [Fact]
    public void DuckAs_ArgumentTypedAsObject_ReportsIfItQuacks001()
    {
        const string source = """
            using IfItQuacks;

            public interface IDoable { void Do(); }

            public class A { public void Do() { } }

            public static class Entry
            {
                public static IDoable Run() => Duck.As<IDoable>((object)new A());
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "IFITQUACKS001");
    }

    [Fact]
    public void DuckAs_OpenTypeParameterArgument_ThrowsAtRuntime()
    {
        const string source = """
            using IfItQuacks;

            public interface IDoable { void Do(); }

            public class A { public void Do() { } }

            public static class Entry
            {
                public static IDoable Convert<T>(T value) where T : class => Duck.As<IDoable>(value);
                public static IDoable Run() => Convert(new A());
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var assembly = GeneratorTestHelper.EmitAndLoad(compilation);
        var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            assembly.GetType("Entry")!.GetMethod("Run")!.Invoke(null, null));
        Assert.Equal("DuckTypeMismatchException", exception.InnerException!.GetType().Name);
    }
}
