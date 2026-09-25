using Xunit;

namespace IfItQuacks.Tests;

public class DelegateTests
{
    private const string Shapes = """
        using IfItQuacks;
        using System;
        using System.Collections.Generic;

        public interface IComparerShape { int Compare(int left, int right); }

        public interface INamed { string Name { get; } }

        public static partial class Ops
        {
            [DuckTyped]
            public static int Best(IComparerShape comparer) => comparer.Compare(2, 1);
        }
        """;

    [Fact]
    public void DelegateVariable_SatisfiesSingleMethodInterface()
    {
        const string source = Shapes + """
            public static class Entry
            {
                public static int Run()
                {
                    Comparison<int> comparison = (left, right) => left - right;
                    return Ops.Best(comparison);
                }
            }
            """;

        Assert.Equal(1, GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void LambdaWithExplicitParameterTypes_SatisfiesSingleMethodInterface()
    {
        const string source = Shapes + """
            public static class Entry
            {
                public static int Run() => Ops.Best((int left, int right) => left - right);
            }
            """;

        Assert.Equal(1, GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void MethodGroupArgument_SatisfiesSingleMethodInterface()
    {
        const string source = Shapes + """
            public static class Entry
            {
                public static int Run() => Ops.Best(Subtract);

                private static int Subtract(int left, int right) => left - right;
            }
            """;

        Assert.Equal(1, GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void DuckAs_ConvertsDelegateToSingleMethodInterface()
    {
        const string source = Shapes + """
            public static class Entry
            {
                public static int Run()
                {
                    Comparison<int> comparison = (left, right) => right - left;
                    var comparer = Duck.As<IComparerShape>(comparison);
                    return comparer.Compare(2, 1);
                }
            }
            """;

        Assert.Equal(-1, GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void FrameworkInterface_IsSatisfiedByMatchingDelegate()
    {
        const string source = """
            using IfItQuacks;
            using System;
            using System.Collections.Generic;

            public static partial class Ops
            {
                [DuckTyped]
                public static string Sort(IComparer<int> comparer)
                {
                    var values = new List<int> { 3, 1, 2 };
                    values.Sort(comparer);
                    return string.Join(",", values);
                }
            }

            public static class Entry
            {
                public static string Run()
                {
                    Comparison<int> descending = (left, right) => right - left;
                    return Ops.Sort(descending);
                }
            }
            """;

        Assert.Equal("3,2,1", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void DelegateWithWrongSignature_ReportsIfItQuacks001()
    {
        const string source = Shapes + """
            public static class Entry
            {
                public static int Run()
                {
                    Func<string, int> wrong = s => s.Length;
                    return Ops.Best(wrong);
                }
            }
            """;

        Assert.Contains("IFITQUACKS001", GeneratorTestHelper.GetDiagnosticIds(source));
    }

    [Fact]
    public void DelegateForInterfaceWithSeveralMembers_ReportsIfItQuacks001()
    {
        const string source = """
            using IfItQuacks;
            using System;

            public interface ITwo { int Compare(int left, int right); string Name { get; } }

            public static partial class Ops
            {
                [DuckTyped]
                public static int Best(ITwo two) => two.Compare(2, 1);
            }

            public static class Entry
            {
                public static int Run()
                {
                    Comparison<int> comparison = (left, right) => left - right;
                    return Ops.Best(comparison);
                }
            }
            """;

        Assert.Contains("IFITQUACKS001", GeneratorTestHelper.GetDiagnosticIds(source));
    }

    [Fact]
    public void MethodGroupOfDuckTypedMethod_ConvertsToDelegate()
    {
        const string source = """
            using IfItQuacks;
            using System;
            using System.Linq;

            public interface INamed { string Name { get; } }

            public class Person { public string Name => "Steven"; }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Describe(INamed named) => "This is " + named.Name;
            }

            public static class Entry
            {
                public static string Run()
                {
                    Func<Person, string> describe = Ops.Describe;
                    var people = new[] { new Person() };
                    return describe(new Person()) + "|" + string.Join(",", people.Select(Ops.Describe));
                }
            }
            """;

        Assert.Equal("This is Steven|This is Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Theory]
    [InlineData("Func<Person, string> describe; describe = Ops.Describe;")]
    [InlineData("Func<Person, string>? describe = null; describe += Ops.Describe;")]
    [InlineData("var describe = (Func<Person, string>)Ops.Describe;")]
    [InlineData("var describe = true ? Ops.Describe : (Func<Person, string>)(p => p.Name);")]
    [InlineData("Func<Person, string>? none = null; var describe = none ?? Ops.Describe;")]
    [InlineData("Func<Person, string>[] all = [Ops.Describe]; var describe = all[0];")]
    [InlineData("var describe = Pick(Ops.Describe);")]
    [InlineData("var describe = Returned();")]
    public void MethodGroupOfDuckTypedMethod_ConvertsToDelegateInAnyPosition(string statements)
    {
        var source = $$"""
            using IfItQuacks;
            using System;

            public interface INamed { string Name { get; } }

            public class Person { public string Name => "Steven"; }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Describe(INamed named) => "This is " + named.Name;
            }

            public static class Entry
            {
                public static string Run()
                {
                    {{statements}}
                    return describe!(new Person());
                }

                private static Func<Person, string> Pick(Func<Person, string> describe) => describe;

                private static Func<Person, string> Returned() => Ops.Describe;
            }
            """;

        Assert.Equal("This is Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void DuckAs_ConvertsMethodGroupAndLambdaToSingleMethodInterface()
    {
        const string source = Shapes + """
            public static class Entry
            {
                public static int Run()
                {
                    var fromGroup = Duck.As<IComparerShape>(Subtract).Compare(5, 1);
                    var fromLambda = Duck.As<IComparerShape>((int left, int right) => left * right).Compare(5, 1);
                    return fromGroup + fromLambda;
                }

                private static int Subtract(int left, int right) => left - right;
            }
            """;

        Assert.Equal(9, GeneratorTestHelper.CompileAndRun(source));
    }
}
