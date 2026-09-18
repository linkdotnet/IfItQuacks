using Xunit;

namespace IfItQuacks.Tests;

public class StubTests
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

        """;

    [Fact]
    public void DelegateMember_SatisfiesAnInterfaceMethod()
    {
        const string source = Shapes + """
            public class Fake
            {
                public Func<int, string> Load = id => $"row-{id}";
                public Action<string> Save { get; } = _ => { };
                public int Count => 7;
            }

            public static class Entry
            {
                public static string Run()
                {
                    var repo = Duck.As<IRepo>(new Fake());
                    repo.Save("x");
                    return repo.Load(3) + "|" + repo.Count;
                }
            }
            """;

        Assert.Equal("row-3|7", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void DelegateMember_OfAnAnonymousType_SatisfiesAnInterfaceMethod()
    {
        const string source = Shapes + """
            public static class Entry
            {
                public static string Run()
                {
                    var repo = Duck.As<IRepo>(new
                    {
                        Load = (Func<int, string>)(id => $"anon-{id}"),
                        Save = (Action<string>)(_ => { }),
                        Count = 1,
                    });
                    return repo.Load(2);
                }
            }
            """;

        Assert.Equal("anon-2", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void Stub_ForwardsWhatIsProvidedAndThrowsForTheRest()
    {
        const string source = Shapes + """
            public static class Entry
            {
                public static string Run()
                {
                    var repo = Duck.Stub<IRepo>(new { Load = (Func<int, string>)(id => $"stub-{id}") });
                    try
                    {
                        repo.Save("x");
                        return "no throw";
                    }
                    catch (DuckStubException)
                    {
                        return repo.Load(1);
                    }
                }
            }
            """;

        Assert.Equal("stub-1", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void Stub_WithoutAValue_ThrowsForEveryMember()
    {
        const string source = Shapes + """
            public static class Entry
            {
                public static string Run()
                {
                    var repo = Duck.Stub<IRepo>();
                    try
                    {
                        return repo.Load(1);
                    }
                    catch (DuckStubException e)
                    {
                        return e.Message.Contains("Load") ? "threw" : e.Message;
                    }
                }
            }
            """;

        Assert.Equal("threw", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void Stub_KeepsIdentityOfTheProvidedValue()
    {
        const string source = Shapes + """
            public class Partial { public int Count => 3; }

            public static class Entry
            {
                public static string Run()
                {
                    var value = new Partial();
                    var repo = Duck.Stub<IRepo>(value);
                    return $"{repo.Count}|{ReferenceEquals(Duck.Unwrap(repo), value)}";
                }
            }
            """;

        Assert.Equal("3|True", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void Stub_WithAMemberThatDoesNotFit_ReportsIfItQuacks001()
    {
        const string source = Shapes + """
            public class Wrong { public int Count(int unexpected) => unexpected; }

            public static class Entry
            {
                public static object Run() => Duck.Stub<IRepo>(new Wrong());
            }
            """;

        Assert.Contains("IFITQUACKS001", GeneratorTestHelper.GetDiagnosticIds(source));
    }

    [Fact]
    public void Stub_OfANonInterface_ReportsIfItQuacks007()
    {
        const string source = Shapes + """
            public class NotAnInterface { }

            public static class Entry
            {
                public static object Run() => Duck.Stub<NotAnInterface>();
            }
            """;

        Assert.Contains("IFITQUACKS007", GeneratorTestHelper.GetDiagnosticIds(source));
    }

    [Fact]
    public void Stub_ImplementsGenericInterfaceMethodsByThrowing()
    {
        const string source = """
            using IfItQuacks;

            public interface IMapper { U Map<U>(); }

            public static class Entry
            {
                public static string Run()
                {
                    var mapper = Duck.Stub<IMapper>();
                    try
                    {
                        mapper.Map<string>();
                        return "no throw";
                    }
                    catch (DuckStubException)
                    {
                        return "threw";
                    }
                }
            }
            """;

        Assert.Equal("threw", GeneratorTestHelper.CompileAndRun(source));
    }
}
