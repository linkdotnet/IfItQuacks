using Xunit;

namespace IfItQuacks.Tests;

public class SequenceTests
{
    private const string Shapes = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using IfItQuacks;

        public interface INamed { string Name { get; } }

        public class Person(string name) { public string Name => name; }

        """;

    [Fact]
    public void Sequence_OfAnUnrelatedType_IsAdaptedElementByElement()
    {
        const string source = Shapes + """
            public static partial class Ops
            {
                [DuckTyped]
                public static string Join(IEnumerable<INamed> people) => string.Join(",", people.Select(p => p.Name));
            }

            public static class Entry
            {
                public static string Run() => Ops.Join(new List<Person> { new("Steven"), new("Donald") });
            }
            """;

        Assert.Equal("Steven,Donald", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void Sequence_WorksForAnArray()
    {
        const string source = Shapes + """
            public static partial class Ops
            {
                [DuckTyped]
                public static string Join(IEnumerable<INamed> people) => string.Join(",", people.Select(p => p.Name));
            }

            public static class Entry
            {
                public static string Run() => Ops.Join(new[] { new Person("Steven") });
            }
            """;

        Assert.Equal("Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void Sequence_AsAReadOnlyList_KeepsCountAndIndexer()
    {
        const string source = Shapes + """
            public static class Entry
            {
                public static string Run()
                {
                    IReadOnlyList<INamed> view = Duck.As<IReadOnlyList<INamed>>(new List<Person> { new("A"), new("B") });
                    return $"{view.Count}|{view[1].Name}|{string.Join("+", view.Select(v => v.Name))}";
                }
            }
            """;

        Assert.Equal("2|B|A+B", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void Sequence_IsAView_SoLaterChangesAreVisible()
    {
        const string source = Shapes + """
            public static class Entry
            {
                public static string Run()
                {
                    var people = new List<Person> { new("A") };
                    var view = Duck.As<IEnumerable<INamed>>(people);
                    people.Add(new Person("B"));
                    return string.Join("+", view.Select(v => v.Name));
                }
            }
            """;

        Assert.Equal("A+B", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void Sequence_OfElementsThatDoNotMatch_ReportsIfItQuacks001()
    {
        const string source = Shapes + """
            public class Rock { }

            public static class Entry
            {
                public static object Run() => Duck.As<IEnumerable<INamed>>(new List<Rock> { new() });
            }
            """;

        Assert.Contains("IFITQUACKS001", GeneratorTestHelper.GetDiagnosticIds(source));
    }

    [Fact]
    public void Sequence_OfElementsImplementingTheInterface_NeedsNoAdapter()
    {
        const string source = Shapes + """
            public class Named : INamed { public string Name => "direct"; }

            public static class Entry
            {
                public static string Run()
                {
                    var view = Duck.As<IEnumerable<INamed>>(new List<Named> { new() });
                    return string.Join("+", view.Select(v => v.Name));
                }
            }
            """;

        Assert.Equal("direct", GeneratorTestHelper.CompileAndRun(source));
    }
}
