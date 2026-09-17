using Xunit;

namespace IfItQuacks.Tests;

public class FieldTests
{
    [Fact]
    public void Field_SatisfiesReadWriteProperty()
    {
        const string source = """
            using IfItQuacks;

            [DuckShape]
            public interface INameable { string Name { get; set; } }

            public class Person { public string Name = "steven"; }

            public static partial class Ops
            {
                [DuckTyped]
                public static void Shout(INameable n) => n.Name = n.Name.ToUpperInvariant();
            }

            public static class Entry
            {
                public static string Run()
                {
                    var person = new Person();
                    Ops.Shout(person);
                    return person.Name;
                }
            }
            """;

        Assert.Equal("STEVEN", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ReadonlyField_SatisfiesGetOnlyProperty_IncludingReadonlyStructs()
    {
        const string source = """
            using IfItQuacks;

            [DuckShape]
            public interface IPoint { int X { get; } object Y { get; } }

            public class ClassPoint { public readonly int X = 1; public readonly string Y = "a"; }
            public readonly struct StructPoint { public readonly int X; public readonly string Y; public StructPoint(int x) { X = x; Y = "b"; } }

            public static class Entry
            {
                public static string Run()
                {
                    var a = Duck.As<IPoint>(new ClassPoint());
                    var b = Duck.As<IPoint>(new StructPoint(2));
                    return $"{a.X}{a.Y}{b.X}{b.Y}";
                }
            }
            """;

        Assert.Equal("1a2b", GeneratorTestHelper.CompileAndRun(source));
    }

    [Theory]
    [InlineData("public readonly string Name = \"\";")]
    [InlineData("public static string Name = \"\";")]
    [InlineData("internal string Name = \"\";")]
    [InlineData("public int Name;")]
    public void UnsuitableField_ReportsIfItQuacks001(string field)
    {
        var source = $$"""
            using IfItQuacks;

            [DuckShape]
            public interface INameable { string Name { get; set; } }

            public class Person { {{field}} }

            public static class Entry
            {
                public static INameable Run() => Duck.As<INameable>(new Person());
            }
            """;

        Assert.Contains("IFITQUACKS001", GeneratorTestHelper.GetDiagnosticIds(source));
    }

    [Fact]
    public void GenericMethod_InfersTypeArgumentFromField()
    {
        const string source = """
            using IfItQuacks;

            [DuckShape]
            public interface IHolder<T> { T Value { get; } }

            public class IntHolder { public int Value = 42; }

            public static partial class Ops
            {
                [DuckTyped]
                public static T Get<T>(IHolder<T> holder) => holder.Value;
            }

            public static class Entry
            {
                public static int Run() => Ops.Get(new IntHolder());
            }
            """;

        Assert.Equal(42, GeneratorTestHelper.CompileAndRun(source));
    }
}
