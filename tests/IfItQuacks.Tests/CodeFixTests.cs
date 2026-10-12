using IfItQuacks.CodeFixes;
using Xunit;

namespace IfItQuacks.Tests;

public class CodeFixTests
{
    [Fact]
    public async Task MakePartial_FixesIfItQuacks002()
    {
        const string source = """
            using IfItQuacks;

            public interface INamed { string Name { get; } }

            public static class Ops
            {
                [DuckTyped]
                public static string Greet(INamed n) => n.Name;
            }
            """;

        var fixedSource = await CodeFixTestHelper.ApplyFix(new MakePartialCodeFix(), source, "IFITQUACKS002");

        Assert.Equal(source.Replace("public static class Ops", "public static partial class Ops", StringComparison.Ordinal), fixedSource);
    }

    [Fact]
    public async Task MakePartial_AlsoFixesEnclosingTypesAndTypesWithoutModifiers()
    {
        const string source = """
            using IfItQuacks;

            public interface INamed { string Name { get; } }

            public class Outer
            {
                class Ops
                {
                    [DuckTyped]
                    public static string Greet(INamed n) => n.Name;
                }
            }
            """;

        var fixedSource = await CodeFixTestHelper.ApplyFix(new MakePartialCodeFix(), source, "IFITQUACKS002");

        Assert.Equal(source
            .Replace("public class Outer", "public partial class Outer", StringComparison.Ordinal)
            .Replace("    class Ops", "    partial class Ops", StringComparison.Ordinal), fixedSource);
    }

    [Fact]
    public async Task MakePartial_FixesIfItQuacks010()
    {
        const string source = """
            using IfItQuacks;

            public class Customer { public string Name { get; set; } = ""; }

            [DuckShape<Customer>]
            public interface ICustomerView;
            """;

        var fixedSource = await CodeFixTestHelper.ApplyFix(new MakePartialCodeFix(), source, "IFITQUACKS010");

        Assert.Equal(source.Replace("public interface ICustomerView", "public partial interface ICustomerView", StringComparison.Ordinal), fixedSource);
    }

    [Fact]
    public async Task MakePartial_FixedSourceCompilesWithoutDiagnostics()
    {
        const string source = """
            using IfItQuacks;

            public interface INamed { string Name { get; } }
            public class Person { public string Name => "Steven"; }

            public static class Ops
            {
                [DuckTyped]
                public static string Greet(INamed n) => n.Name;
            }
            """;

        var fixedSource = await CodeFixTestHelper.ApplyFix(new MakePartialCodeFix(), source, "IFITQUACKS002");

        Assert.Empty(GeneratorTestHelper.RunGenerator(fixedSource).Diagnostics);
    }

    [Fact]
    public async Task RenameMember_AppliesTheDidYouMeanOfIfItQuacks001()
    {
        const string source = """
            using IfItQuacks;

            public interface INamed { string Name { get; } }
            public class Person { public string name => "Steven"; }

            public static class Entry
            {
                public static string Run() => new Person().name + Duck.As<INamed>(new Person()).Name;
            }
            """;

        var fixedSource = await CodeFixTestHelper.ApplyFix(new RenameMemberCodeFix(), source, "IFITQUACKS001", "Rename 'name' to 'Name'");

        Assert.Equal(source.Replace("string name", "string Name", StringComparison.Ordinal).Replace("new Person().name", "new Person().Name", StringComparison.Ordinal), fixedSource);
    }

    [Fact]
    public async Task RenameMember_OffersOneFixPerMisspelledMember()
    {
        const string source = """
            using IfItQuacks;

            public interface IPerson { string Name { get; } int Age { get; } string Greet(); }
            public class Person { public string name => "Steven"; public int AGE => 40; public string greet() => "hi"; }

            public static class Entry
            {
                public static object Run() => Duck.As<IPerson>(new Person());
            }
            """;

        var titles = await CodeFixTestHelper.GetFixTitles(new RenameMemberCodeFix(), source, "IFITQUACKS001");

        Assert.Equal(["Rename 'name' to 'Name'", "Rename 'AGE' to 'Age'", "Rename 'greet' to 'Greet'"], titles);
    }

    [Fact]
    public async Task RenameMember_WithoutANearMiss_OffersNothing()
    {
        const string source = """
            using IfItQuacks;

            public interface INamed { string Name { get; } }
            public class Rock { }

            public static class Entry
            {
                public static object Run() => Duck.As<INamed>(new Rock());
            }
            """;

        Assert.Empty(await CodeFixTestHelper.GetFixTitles(new RenameMemberCodeFix(), source, "IFITQUACKS001"));
    }

    [Theory]
    [InlineData("named is Person person ? person.Name : \"?\"", "Duck.Unwrap(named) is Person person ? person.Name : \"?\"")]
    [InlineData("((Person)named).Name", "((Person)Duck.Unwrap(named)!).Name")]
    [InlineData("(named as Person)?.Name ?? \"?\"", "(Duck.Unwrap(named) as Person)?.Name ?? \"?\"")]
    [InlineData("named switch { Person p => p.Name, _ => \"?\" }", "Duck.Unwrap(named) switch { Person p => p.Name, _ => \"?\" }")]
    public async Task UnwrapAdapter_FixesIfItQuacks012(string body, string expected)
    {
        var source = $$"""
            using IfItQuacks;

            public interface INamed { string Name { get; } }
            public class Person { public string Name => "Steven"; }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Describe(INamed named) => {{body}};
            }
            """;

        var fixedSource = await CodeFixTestHelper.ApplyFix(new UnwrapAdapterCodeFix(), source, "IFITQUACKS012");

        Assert.Equal(source.Replace(body, expected, StringComparison.Ordinal), fixedSource);
    }

    [Fact]
    public async Task UnwrapAdapter_QualifiesDuckWithoutAUsing()
    {
        const string source = """
            public interface INamed { string Name { get; } }
            public class Person { public string Name => "Steven"; }

            public static partial class Ops
            {
                [IfItQuacks.DuckTyped]
                public static bool IsPerson(INamed named) => named is Person;
            }
            """;

        var fixedSource = await CodeFixTestHelper.ApplyFix(new UnwrapAdapterCodeFix(), source, "IFITQUACKS012");

        Assert.Equal(source.Replace("named is Person", "IfItQuacks.Duck.Unwrap(named) is Person", StringComparison.Ordinal), fixedSource);
    }

    [Fact]
    public async Task UnwrapAdapter_FixedSourceRunsAndReportsNothing()
    {
        const string source = """
            using IfItQuacks;

            public interface INamed { string Name { get; } }
            public class Person { public string Name => "Steven"; }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Describe(INamed named) => named is Person person ? "person:" + person.Name : "?";
            }

            public static class Entry { public static string Run() => Ops.Describe(new Person()); }
            """;

        var fixedSource = await CodeFixTestHelper.ApplyFix(new UnwrapAdapterCodeFix(), source, "IFITQUACKS012");

        Assert.Empty(GeneratorTestHelper.RunGenerator(fixedSource).Diagnostics);
        Assert.Equal("person:Steven", GeneratorTestHelper.CompileAndRun(fixedSource));
    }

    [Fact]
    public async Task RenameMember_IsOfferedForADuckTypedConstraint()
    {
        const string source = """
            using IfItQuacks;

            public interface INamed { string Name { get; } }
            public class Person { public string name => "Steven"; }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Greet<T>(T named) where T : INamed => named.Name;
            }

            public static class Entry
            {
                public static string Run() => Ops.Greet(new Person());
            }
            """;

        Assert.Equal(["Rename 'name' to 'Name'"], await CodeFixTestHelper.GetFixTitles(new RenameMemberCodeFix(), source, "IFITQUACKS001"));
    }
}
