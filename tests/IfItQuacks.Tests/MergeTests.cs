using Xunit;

namespace IfItQuacks.Tests;

public class MergeTests
{
    private const string Shapes = """
        using System;
        using IfItQuacks;

        public interface IRepo
        {
            string Load(int id);
            void Save(string row);
            int Count { get; }
        }

        public class RealRepo
        {
            public string Saved = "";
            public string Load(int id) => $"real-{id}";
            public void Save(string row) => Saved = row;
            public int Count => 1;
        }

        """;

    [Fact]
    public void Merge_TakesEachMemberFromTheFirstValueProvidingIt()
    {
        const string source = Shapes + """
            public static class Entry
            {
                public static string Run()
                {
                    var real = new RealRepo();
                    string? spied = null;
                    var repo = Duck.Merge<IRepo>(new { Save = (Action<string>)(row => spied = row) }, real);
                    repo.Save("row-1");
                    return $"{repo.Load(9)}|{repo.Count}|{spied}|{real.Saved}";
                }
            }
            """;

        Assert.Equal("real-9|1|row-1|", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void Merge_CombinesThreeValues()
    {
        const string source = Shapes + """
            public class Loader { public string Load(int id) => $"loaded-{id}"; }
            public class Counter { public int Count => 42; }

            public static class Entry
            {
                public static string Run()
                {
                    var repo = Duck.Merge<IRepo>(new Loader(), new Counter(), new RealRepo());
                    return $"{repo.Load(1)}|{repo.Count}";
                }
            }
            """;

        Assert.Equal("loaded-1|42", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void Merge_UnwrapsToTheFirstValue()
    {
        const string source = Shapes + """
            public class Counter { public int Count => 42; }

            public static class Entry
            {
                public static string Run()
                {
                    var counter = new Counter();
                    var repo = Duck.Merge<IRepo>(counter, new RealRepo());
                    return ReferenceEquals(Duck.Unwrap(repo), counter).ToString();
                }
            }
            """;

        Assert.Equal("True", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void Merge_WithAMemberNoValueProvides_ReportsIfItQuacks001()
    {
        const string source = Shapes + """
            public class Counter { public int Count => 42; }

            public static class Entry
            {
                public static object Run() => Duck.Merge<IRepo>(new Counter(), new Counter());
            }
            """;

        Assert.Contains("IFITQUACKS001", GeneratorTestHelper.GetDiagnosticIds(source));
    }

    [Fact]
    public void Merge_OfANonInterface_ReportsIfItQuacks007()
    {
        const string source = Shapes + """
            public static class Entry
            {
                public static object Run() => Duck.Merge<RealRepo>(new RealRepo(), new RealRepo());
            }
            """;

        Assert.Contains("IFITQUACKS007", GeneratorTestHelper.GetDiagnosticIds(source));
    }
}
