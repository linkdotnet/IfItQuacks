using Xunit;

namespace IfItQuacks.Tests;

public class AdapterIdentityTests
{
    private const string Shapes = """
        using IfItQuacks;
        using System.Collections.Generic;

        public interface INamed { string Name { get; } }

        public class Person { public string Name => "Steven"; public override string ToString() => "Person " + Name; }
        public readonly record struct Point(int X) { public string Name => "P" + X; }
        public class Tag : INamed { public string Name => "Tag"; }
        """;

    [Fact]
    public void AdaptersOfSameInstance_AreEqual_AndDeduplicatedInHashSet()
    {
        var source = Shapes + """

            public static class Entry
            {
                public static string Run()
                {
                    var person = new Person();
                    var a = Duck.As<INamed>(person);
                    var b = Duck.As<INamed>(person);
                    var other = Duck.As<INamed>(new Person());
                    var set = new HashSet<INamed> { a, b, other };
                    return $"{a.Equals(b)}|{a.Equals(person)}|{a.Equals(other)}|{a.GetHashCode() == person.GetHashCode()}|{set.Count}|{ReferenceEquals(a, b)}";
                }
            }
            """;

        Assert.Equal("True|True|False|True|2|False", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ToString_IsForwarded_ForClassesAndStructs()
    {
        var source = Shapes + """

            public static class Entry
            {
                public static string Run() => Duck.As<INamed>(new Person()) + "|" + Duck.As<INamed>(new Point(1)) + "|" + Duck.As<INamed>(new { Name = "x" });
            }
            """;

        Assert.Equal("Person Steven|Point { X = 1, Name = P1 }|{ Name = x }", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void StructAdapters_CompareByValue()
    {
        var source = Shapes + """

            public static class Entry
            {
                public static bool Run() => Duck.As<INamed>(new Point(1)).Equals(Duck.As<INamed>(new Point(1)));
            }
            """;

        Assert.True((bool)GeneratorTestHelper.CompileAndRun(source)!);
    }

    [Fact]
    public void Unwrap_ReturnsOriginalInstance()
    {
        var source = Shapes + """

            public partial class Registry
            {
                public object? Last;

                [DuckTyped]
                public void Add(INamed named) => Last = Duck.Unwrap(named);
            }

            public static class Entry
            {
                public static string Run()
                {
                    var person = new Person();
                    var tag = new Tag();
                    var registry = new Registry();
                    registry.Add(person);
                    var fromMethod = ReferenceEquals(registry.Last, person);
                    return $"{fromMethod}|{Duck.Unwrap(Duck.As<INamed>(person)) is Person}|{ReferenceEquals(Duck.Unwrap(Duck.As<INamed>(tag)), tag)}|{Duck.Unwrap(null) is null}";
                }
            }
            """;

        Assert.Equal("True|True|True|True", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ShapeDeclaringObjectMembers_StillCompiles()
    {
        const string source = """
            using IfItQuacks;

            public interface IDescribable { string ToString(); int GetHashCode(); }

            public class Item { public override string ToString() => "item"; public override int GetHashCode() => 7; }

            public static class Entry
            {
                public static string Run()
                {
                    var d = Duck.As<IDescribable>(new Item());
                    return d.ToString() + d.GetHashCode();
                }
            }
            """;

        Assert.Equal("item7", GeneratorTestHelper.CompileAndRun(source));
    }
}
