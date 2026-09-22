using Xunit;

namespace IfItQuacks.Tests;

public class DuckTypedSignatureTests
{
    private const string Shapes = """
        using IfItQuacks;
        using System.Linq;

        public interface INamed { string Name { get; } }

        public interface IContainer<T> { T Get(); }

        public class Person { public string Name => "Steven"; }
        public class Pet { public string Name => "Duck"; }
        public class Tag : INamed { public string Name => "Tag"; }
        public class IntBox(int value) { public int Get() => value; }
        public class Rock { }
        """;

    [Fact]
    public void StaticMethod_WithSeveralShapeAndRegularParameters()
    {
        const string source = Shapes + """
            public static partial class Ops
            {
                [DuckTyped]
                public static string Meet(INamed first, int times, INamed second) =>
                    string.Join(",", Enumerable.Repeat(first.Name + "+" + second.Name, times));
            }

            public static class Entry
            {
                public static string Run() => Ops.Meet(new Person(), 2, new Pet());
            }
            """;

        Assert.Equal("Steven+Duck,Steven+Duck", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void NamedArgumentsInAnyOrder_AndOmittedDefaults()
    {
        const string source = Shapes + """
            public static partial class Ops
            {
                [DuckTyped]
                public static string Meet(INamed first, INamed second, string separator = "&", int times = 1) =>
                    string.Join(",", Enumerable.Repeat(first.Name + separator + second.Name, times));
            }

            public static class Entry
            {
                public static string Run() => Ops.Meet(second: new Pet(), first: new Person()) + "|" + Ops.Meet(new Pet(), new Person(), times: 2);
            }
            """;

        Assert.Equal("Steven&Duck|Duck&Steven,Duck&Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void MixOfAdaptedArgumentsAndArgumentsImplementingTheShape()
    {
        const string source = Shapes + """
            public static partial class Ops
            {
                [DuckTyped]
                public static string Meet(INamed first, INamed second) => first.Name + "+" + second.Name;
            }

            public static class Entry
            {
                public static string Run() => Ops.Meet(new Tag(), new Pet());
            }
            """;

        Assert.Equal("Tag+Duck", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void RefOutAndParamsParameters_ArePassedThrough()
    {
        const string source = Shapes + """
            public static partial class Ops
            {
                [DuckTyped]
                public static bool TryDescribe(INamed named, ref int calls, out string description, params string[] suffixes)
                {
                    calls++;
                    description = named.Name + string.Concat(suffixes);
                    return true;
                }
            }

            public static class Entry
            {
                public static string Run()
                {
                    var calls = 0;
                    Ops.TryDescribe(new Person(), ref calls, out var first, "!", "?");
                    Ops.TryDescribe(new Pet(), ref calls, out var second);
                    return first + second + calls;
                }
            }
            """;

        Assert.Equal("Steven!?Duck2", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void KeywordParameterNames_AreEscaped()
    {
        const string source = Shapes + """
            public static partial class Ops
            {
                [DuckTyped]
                public static string Describe(INamed @event, string @class) => @event.Name + @class;
            }

            public static class Entry
            {
                public static string Run() => Ops.Describe(new Person(), "!");
            }
            """;

        Assert.Equal("Steven!", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void InstanceMethod_CalledOnReceiverAndViaImplicitThis()
    {
        const string source = Shapes + """
            public partial class Greeter(string greeting)
            {
                [DuckTyped]
                public string Greet(INamed named) => greeting + ", " + named.Name;

                public string GreetPet() => Greet(new Pet());
            }

            public static class Entry
            {
                public static string Run()
                {
                    var greeter = new Greeter("Hello");
                    return greeter.Greet(new Person()) + "|" + greeter.GreetPet();
                }
            }
            """;

        Assert.Equal("Hello, Steven|Hello, Duck", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void InstanceMethodOnRecord_AndStaticMethodOnRecordStruct()
    {
        const string source = Shapes + """
            public partial record Greeter(string Greeting)
            {
                [DuckTyped]
                public string Greet(INamed named) => Greeting + ", " + named.Name;
            }

            public readonly partial record struct Formatter
            {
                [DuckTyped]
                public static string Format(INamed named) => "<" + named.Name + ">";
            }

            public static class Entry
            {
                public static string Run() => new Greeter("Hi").Greet(new Person()) + Formatter.Format(new Pet());
            }
            """;

        Assert.Equal("Hi, Steven<Duck>", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void InstanceMethodOnMutableStruct_MutatesCallersValue()
    {
        const string source = Shapes + """
            public partial struct Counter
            {
                public int Count;

                [DuckTyped]
                public string Add(INamed named) => named.Name + ++Count;
            }

            public readonly partial record struct Prefix(string Value)
            {
                [DuckTyped]
                public string Apply(INamed named) => Value + named.Name;
            }

            public static class Entry
            {
                private static readonly Prefix Hi = new("Hi ");

                public static string Run()
                {
                    var counter = new Counter();
                    var first = counter.Add(new Person());
                    var second = counter.Add(new Pet());
                    return first + "|" + second + "|" + counter.Count + "|" + Hi.Apply(new Person());
                }
            }
            """;

        Assert.Equal("Steven1|Duck2|2|Hi Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void InstanceMethodOnStruct_OnReceiversThatAreNotWritableVariables_CopiesDefensively()
    {
        const string source = Shapes + """
            public partial struct Counter
            {
                public int Count;

                [DuckTyped]
                public string Add(INamed named) => named.Name + ++Count;
            }

            public static class Entry
            {
                private static readonly Counter Field = new();

                private static Counter Property => new();

                public static string Run() =>
                    string.Join("|", Field.Add(new Person()), new Counter().Add(new Pet()), Property.Add(new Person()),
                        FromIn(Field), Field.Count);

                private static string FromIn(in Counter counter) => counter.Add(new Pet());
            }
            """;

        Assert.Equal("Steven1|Duck1|Steven1|Duck1|0", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void PrivateAndProtectedMethods_AreIntercepted()
    {
        const string source = Shapes + """
            public partial class Base
            {
                [DuckTyped]
                protected string Describe(INamed named) => "base:" + named.Name;

                [DuckTyped]
                private protected static string Tag(INamed named) => "#" + named.Name;
            }

            public partial class Derived : Base
            {
                public string Run() => Describe(new Person()) + Tag(new Pet());
            }

            public static partial class Ops
            {
                [DuckTyped]
                private static string Shout(INamed named) => named.Name.ToUpperInvariant();

                [DuckTyped]
                private static T First<T>(IContainer<T> container) => container.Get();

                public static string Run() => Shout(new Pet()) + First(new IntBox(7));
            }

            public static class Entry
            {
                public static string Run() => new Derived().Run() + "|" + Ops.Run();
            }
            """;

        Assert.Equal("base:Steven#Duck|DUCK7", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void GenericInstanceMethod_InfersTypeArgumentUsedByRegularParameter()
    {
        const string source = Shapes + """
            public partial class Collector
            {
                public System.Collections.Generic.List<string> Items { get; } = new();

                [DuckTyped]
                public T Add<T>(IContainer<T> container, T fallback)
                {
                    var value = container.Get();
                    Items.Add(value + "/" + fallback);
                    return value;
                }
            }

            public static class Entry
            {
                public static string Run()
                {
                    var collector = new Collector();
                    int value = collector.Add(new IntBox(42), 7);
                    return value + "|" + string.Join(",", collector.Items);
                }
            }
            """;

        Assert.Equal("42|42/7", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void GenericMethod_InfersTypeArgumentFromSeveralShapeParameters()
    {
        const string source = Shapes + """
            public static partial class Ops
            {
                [DuckTyped]
                public static T Pick<T>(IContainer<T> first, IContainer<T> second, bool takeFirst) => takeFirst ? first.Get() : second.Get();
            }

            public static class Entry
            {
                public static int Run() => Ops.Pick(new IntBox(1), new IntBox(2), takeFirst: false);
            }
            """;

        Assert.Equal(2, GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void MismatchInSecondShapeParameter_ReportsIfItQuacks001OnThatArgument()
    {
        const string source = Shapes + """
            public static partial class Ops
            {
                [DuckTyped]
                public static string Meet(INamed first, INamed second) => first.Name + second.Name;
            }

            public static class Entry
            {
                public static string Run() => Ops.Meet(new Person(), new Rock());
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        var diagnostic = Assert.Single(diagnostics, d => d.Id == "IFITQUACKS001");
        Assert.Equal("new Rock()", diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void MismatchInEveryShapeParameter_ReportsEachOne()
    {
        const string source = Shapes + """
            public static partial class Ops
            {
                [DuckTyped]
                public static string Meet(INamed first, INamed second) => first.Name + second.Name;
            }

            public static class Entry
            {
                public static string Run() => Ops.Meet(new Rock(), new Rock());
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Equal(2, diagnostics.Count(d => d.Id == "IFITQUACKS001"));
    }

    [Fact]
    public void MethodsOnRefStruct_AreIntercepted()
    {
        const string source = Shapes + """
            public ref partial struct Cursor
            {
                public int Count;

                [DuckTyped]
                public string Add(INamed named) => named.Name + ++Count;

                [DuckTyped]
                public static string Format(INamed named) => "<" + named.Name + ">";
            }

            public static class Entry
            {
                public static string Run()
                {
                    var cursor = new Cursor();
                    return cursor.Add(new Person()) + "|" + Cursor.Format(new Pet()) + "|" + cursor.Count;
                }
            }
            """;

        Assert.Equal("Steven1|<Duck>|1", GeneratorTestHelper.CompileAndRun(source));
    }

    [Theory]
    [InlineData("public partial class Holder<T> { [DuckTyped] public static string Get(INamed n) => n.Name; }")]
    [InlineData("public partial interface IHolder { [DuckTyped] public static string Get(INamed n) => n.Name; }")]
    [InlineData("file partial class Holder { [DuckTyped] public static string Get(INamed n) => n.Name; }")]
    [InlineData("public static partial class Holder { [DuckTyped] public static T Get<T>(INamed n, T value) => value; }")]
    public void UnsupportedSignature_ReportsIfItQuacks004(string declaration)
    {
        var source = Shapes + declaration;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "IFITQUACKS004");
    }

    [Fact]
    public void MethodWithoutInterfaceParameter_ReportsIfItQuacks003()
    {
        const string source = Shapes + """
            public static partial class Ops
            {
                [DuckTyped]
                public static int Twice(int value) => value * 2;
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "IFITQUACKS003");
    }
}
