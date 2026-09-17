using Xunit;

namespace IfItQuacks.Tests;

public class AnonymousTypeTests
{
    private const string Shapes = """
        using IfItQuacks;

        public interface IPerson { string Name { get; } int Age { get; } }

        public interface INamed { string Name { get; } }
        """;

    [Fact]
    public void DuckAs_AnonymousType()
    {
        var source = Shapes + """

            public static class Entry
            {
                public static string Run()
                {
                    var person = Duck.As<IPerson>(new { Name = "Steven", Age = 42, Ignored = true });
                    return person.Name + person.Age;
                }
            }
            """;

        Assert.Equal("Steven42", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void DuckTypedStaticMethod_AnonymousTypeMixedWithNamedTypeAndRegularParameter()
    {
        var source = Shapes + """

            public class Pet { public string Name => "Duck"; }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Meet(INamed first, int times, INamed second) => $"{first.Name}x{times}{second.Name}";
            }

            public static class Entry
            {
                public static string Run() => Ops.Meet(new { Name = "Steven" }, 2, new Pet()) + "|" + Ops.Meet(second: new { Name = "B" }, times: 1, first: new { Name = "A" });
            }
            """;

        Assert.Equal("Stevenx2Duck|Ax1B", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void DuckTypedInstanceMethod_AnonymousType()
    {
        var source = Shapes + """

            public partial class Greeter
            {
                [DuckTyped]
                public string Greet(IPerson person) => $"Hello {person.Name} ({person.Age})";
            }

            public static class Entry
            {
                public static string Run() => new Greeter().Greet(new { Name = "Steven", Age = 42 });
            }
            """;

        Assert.Equal("Hello Steven (42)", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void NestedAnonymousTypes_AndAssignableProperties()
    {
        var source = Shapes + """

            public interface IOrder { object Customer { get; } long Total { get; } }

            public static class Entry
            {
                public static string Run()
                {
                    var order = Duck.As<IOrder>(new { Customer = new { Name = "Steven", Tags = new[] { "a" } }, Total = 3 });
                    return order.Customer + "|" + order.Total;
                }
            }
            """;

        Assert.Equal("{ Name = Steven, Tags = System.String[] }|3", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void SameAnonymousShapeTwice_SharesAdapter()
    {
        var source = Shapes + """

            public static class Entry
            {
                public static string Run() => Duck.As<INamed>(new { Name = "A" }).Name + Duck.As<INamed>(new { Name = "B" }).Name;
            }
            """;

        Assert.Equal("AB", GeneratorTestHelper.CompileAndRun(source));
    }

    [Theory]
    [InlineData("public static class Entry { public static INamed Run() => Duck.As<INamed>(new { Title = \"x\" }); }")]
    [InlineData("public interface IWritable { string Name { get; set; } } public static class Entry { public static IWritable Run() => Duck.As<IWritable>(new { Name = \"x\" }); }")]
    [InlineData("public static class Entry { public static INamed Run() => Duck.As<INamed>(new { Name = \"x\", Items = new[] { new { A = 1 } } }); }")]
    [InlineData("public static partial class Ops { [DuckTyped] public static T Get<T>(IBox<T> box) => box.Value; } public interface IBox<T> { T Value { get; } } public static class Entry { public static int Run() => Ops.Get(new { Value = 1 }); }")]
    public void UnsupportedAnonymousType_ReportsIfItQuacks001(string code)
    {
        Assert.Contains("IFITQUACKS001", GeneratorTestHelper.GetDiagnosticIds(Shapes + "\n" + code));
    }
}
