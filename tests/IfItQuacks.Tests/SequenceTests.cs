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

    [Fact]
    public void List_ReadsAdaptedElements()
    {
        const string source = Shapes + """
            public static class Entry
            {
                public static string Run()
                {
                    IList<INamed> view = Duck.As<IList<INamed>>(new List<Person> { new("A"), new("B") });
                    return $"{view.Count}|{view[1].Name}|{view.IsReadOnly}|{string.Join("+", view.Select(v => v.Name))}";
                }
            }
            """;

        Assert.Equal("2|B|False|A+B", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void List_WritesUnwrapTheAdapterBackToTheOriginal()
    {
        const string source = Shapes + """
            public static class Entry
            {
                public static string Run()
                {
                    var people = new List<Person> { new("A"), new("B") };
                    var view = Duck.As<IList<INamed>>(people);
                    var b = view[1];
                    view.Add(view[0]);
                    view.Insert(0, b);
                    view[1] = b;
                    var index = view.IndexOf(b);
                    var contains = view.Contains(b);
                    view.RemoveAt(0);
                    var removed = view.Remove(b);
                    return $"{string.Join("+", people.Select(p => p.Name))}|{index}|{contains}|{removed}";
                }
            }
            """;

        Assert.Equal("B+A|0|True|True", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void List_RejectsValuesThatAreNotTheOriginalElementType()
    {
        const string source = Shapes + """
            public class Other : INamed { public string Name => "other"; }

            public static class Entry
            {
                public static string Run()
                {
                    var view = Duck.As<ICollection<INamed>>(new List<Person> { new("A") });
                    var other = new Other();
                    var found = view.Contains(other);
                    var removed = view.Remove(other);
                    try { view.Add(other); return "added"; }
                    catch (ArgumentException) { return $"{found}|{removed}|rejected"; }
                }
            }
            """;

        Assert.Equal("False|False|rejected", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void Collection_CopiesAdaptedElements()
    {
        const string source = Shapes + """
            public static class Entry
            {
                public static string Run()
                {
                    var view = Duck.As<ICollection<INamed>>(new List<Person> { new("A"), new("B") });
                    var copy = new INamed[3];
                    view.CopyTo(copy, 1);
                    view.Clear();
                    return $"{copy[0] is null}|{copy[1].Name}|{copy[2].Name}|{view.Count}";
                }
            }
            """;

        Assert.Equal("True|A|B|0", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void Collection_OverAnArray_KeepsTheArraysRestrictions()
    {
        const string source = Shapes + """
            public static class Entry
            {
                public static void Run() => Duck.As<ICollection<INamed>>(new[] { new Person("A") }).Clear();
            }
            """;

        var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() => GeneratorTestHelper.CompileAndRun(source));
        Assert.IsType<NotSupportedException>(exception.InnerException);
    }

    [Fact]
    public void List_PassedToADuckTypedMethod_IsAdapted()
    {
        const string source = Shapes + """
            public static partial class Ops
            {
                [DuckTyped]
                public static string Rename(IList<INamed> people) => $"{people.Count}:{people[0].Name}";
            }

            public static class Entry
            {
                public static string Run() => Ops.Rename(new List<Person> { new("Steven") });
            }
            """;

        Assert.Equal("1:Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ReadOnlyDictionary_AdaptsItsValues()
    {
        const string source = Shapes + """
            public static class Entry
            {
                public static string Run()
                {
                    var view = Duck.As<IReadOnlyDictionary<string, INamed>>(new Dictionary<string, Person> { ["a"] = new("A"), ["b"] = new("B") });
                    var found = view.TryGetValue("b", out var b);
                    var missing = view.TryGetValue("c", out var c);
                    return $"{view.Count}|{view["a"].Name}|{found}:{b!.Name}|{missing}:{c is null}|{view.ContainsKey("a")}|" +
                           $"{string.Join("+", view.Keys)}|{string.Join("+", view.Values.Select(v => v.Name))}|{string.Join("+", view.Select(p => p.Key + "=" + p.Value.Name))}";
                }
            }
            """;

        Assert.Equal("2|A|True:B|False:True|True|a+b|A+B|a=A+b=B", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void NestedSequences_AreAdaptedLevelByLevel()
    {
        const string source = Shapes + """
            public static partial class Ops
            {
                [DuckTyped]
                public static string Join(IEnumerable<IEnumerable<INamed>> groups) => string.Join("|", groups.Select(g => string.Join("+", g.Select(p => p.Name))));
            }

            public static class Entry
            {
                public static string Run()
                {
                    var lists = new List<List<Person>> { new() { new("A"), new("B") }, new() { new("C") } };
                    var arrays = Duck.As<IReadOnlyList<IReadOnlyList<INamed>>>(new[] { new[] { new Person("D") } });
                    return Ops.Join(lists) + "/" + arrays[0][0].Name;
                }
            }
            """;

        Assert.Equal("A+B|C/D", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void NestedSequence_OfElementsThatDoNotMatch_ReportsIfItQuacks001()
    {
        const string source = Shapes + """
            public class Rock { }

            public static class Entry
            {
                public static object Run() => Duck.As<IEnumerable<IEnumerable<INamed>>>(new List<List<Rock>>());
            }
            """;

        Assert.Contains("IFITQUACKS001", GeneratorTestHelper.GetDiagnosticIds(source));
    }

    [Fact]
    public void List_OfElementsImplementingTheInterface_IsAdaptedWithoutElementAdapters()
    {
        const string source = Shapes + """
            public class Named(string name) : INamed { public string Name => name; }
            public class Other : INamed { public string Name => "other"; }

            public static class Entry
            {
                public static string Run()
                {
                    var named = new List<Named> { new("A") };
                    var view = Duck.As<IList<INamed>>(named);
                    view.Add(new Named("B"));
                    var rejected = false;
                    try { view.Add(new Other()); } catch (ArgumentException) { rejected = true; }
                    return $"{string.Join("+", named.Select(n => n.Name))}|{view[1].Name}|{view.IndexOf(named[1])}|{rejected}";
                }
            }
            """;

        Assert.Equal("A+B|B|1|True", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ReadOnlyDictionary_OfValuesImplementingTheInterface_IsAdapted()
    {
        const string source = Shapes + """
            public class Named(string name) : INamed { public string Name => name; }

            public static class Entry
            {
                public static string Run() =>
                    Duck.As<IReadOnlyDictionary<string, INamed>>(new Dictionary<string, Named> { ["a"] = new("A") })["a"].Name;
            }
            """;

        Assert.Equal("A", GeneratorTestHelper.CompileAndRun(source));
    }
}
