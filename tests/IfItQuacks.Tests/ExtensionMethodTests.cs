using Xunit;

namespace IfItQuacks.Tests;

public class ExtensionMethodTests
{
    private const string Shapes = """
        using System;
        using IfItQuacks;

        public interface INamed { string Name { get; } }

        public class Person { public string Name => "Steven"; }
        public class Mallard { public string Name => "Donald"; }

        """;

    [Fact]
    public void ExtensionMethod_IsCalledOnAnyMatchingReceiver()
    {
        const string source = Shapes + """
            public static partial class Ops
            {
                [DuckTyped]
                public static string Describe(this INamed named) => named.Name;
            }

            public static class Entry
            {
                public static string Run() => new Person().Describe() + "|" + new Mallard().Describe();
            }
            """;

        Assert.Equal("Steven|Donald", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ExtensionMethod_KeepsOtherParametersAndDefaults()
    {
        const string source = Shapes + """
            public static partial class Ops
            {
                [DuckTyped]
                public static string Greet(this INamed named, string greeting = "Hello") => $"{greeting}, {named.Name}!";
            }

            public static class Entry
            {
                public static string Run() => new Person().Greet() + "|" + new Mallard().Greet("Quack");
            }
            """;

        Assert.Equal("Hello, Steven!|Quack, Donald!", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ExtensionMethod_CalledAsAStaticMethod_WorksAsWell()
    {
        const string source = Shapes + """
            public static partial class Ops
            {
                [DuckTyped]
                public static string Describe(this INamed named) => named.Name;
            }

            public static class Entry
            {
                public static string Run() => Ops.Describe(new Person());
            }
            """;

        Assert.Equal("Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ExtensionMethod_AcceptsAnonymousTypes()
    {
        const string source = Shapes + """
            public static partial class Ops
            {
                [DuckTyped]
                public static string Describe(this INamed named) => named.Name;
            }

            public static class Entry
            {
                public static string Run() => new { Name = "Anon" }.Describe();
            }
            """;

        Assert.Equal("Anon", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ExtensionMethod_WithANonInterfaceReceiver_DuckTypesTheOtherParameter()
    {
        const string source = Shapes + """
            public static partial class Ops
            {
                [DuckTyped]
                public static string Introduce(this Person person, INamed other) => $"{person.Name} and {other.Name}";
            }

            public static class Entry
            {
                public static string Run() => new Person().Introduce(new Mallard());
            }
            """;

        Assert.Equal("Steven and Donald", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ExtensionMethod_WithAMismatchedReceiver_ReportsIfItQuacks001()
    {
        const string source = Shapes + """
            public class Rock { }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Describe(this INamed named) => named.Name;
            }

            public static class Entry
            {
                public static string Run() => new Rock().Describe();
            }
            """;

        Assert.Contains("IFITQUACKS001", GeneratorTestHelper.GetDiagnosticIds(source));
    }
}
