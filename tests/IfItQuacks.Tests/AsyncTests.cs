using Xunit;

namespace IfItQuacks.Tests;

public class AsyncTests
{
    private const string Shapes = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using IfItQuacks;

        public interface INamed { string Name { get; } }
        public class Named : INamed { public string Name => "Steven"; }

        """;

    [Fact]
    public void TaskOfADerivedType_SatisfiesTaskOfTheInterface()
    {
        const string source = Shapes + """
            public interface IRepository { Task<INamed> FindAsync(int id); }
            public class Repository { public Task<Named> FindAsync(int id) => Task.FromResult(new Named()); }

            public static class Entry
            {
                public static string Run() => Duck.As<IRepository>(new Repository()).FindAsync(1).GetAwaiter().GetResult().Name;
            }
            """;

        Assert.Equal("Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ValueTaskOfADerivedType_SatisfiesValueTaskOfTheInterface()
    {
        const string source = Shapes + """
            public interface IRepository { ValueTask<INamed> FindAsync(int id); }
            public class Repository { public ValueTask<Named> FindAsync(int id) => new(new Named()); }

            public static class Entry
            {
                public static string Run() => Duck.As<IRepository>(new Repository()).FindAsync(1).AsTask().GetAwaiter().GetResult().Name;
            }
            """;

        Assert.Equal("Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Theory]
    [InlineData("Task<INamed>", "ValueTask<Named>", "new(new Named())", ".GetAwaiter().GetResult()")]
    [InlineData("ValueTask<INamed>", "Task<Named>", "Task.FromResult(new Named())", ".AsTask().GetAwaiter().GetResult()")]
    [InlineData("ValueTask<int>", "Task<int>", "Task.FromResult(42)", ".AsTask().GetAwaiter().GetResult()")]
    [InlineData("Task<long>", "Task<int>", "Task.FromResult(42)", ".GetAwaiter().GetResult()")]
    public void TaskAndValueTask_SatisfyEachOther(string shapeReturn, string concreteReturn, string value, string awaitResult)
    {
        var source = Shapes + $$"""
            public interface ILoader { {{shapeReturn}} LoadAsync(); }
            public class Loader { public {{concreteReturn}} LoadAsync() => {{value}}; }

            public static class Entry
            {
                public static string Run()
                {
                    var result = Duck.As<ILoader>(new Loader()).LoadAsync(){{awaitResult}};
                    return (object)result is INamed named ? named.Name : result.ToString()!;
                }
            }
            """;

        Assert.Equal(value.Contains("42", StringComparison.Ordinal) ? "42" : "Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void AsyncMember_ForwardsArgumentsAndExceptions()
    {
        const string source = Shapes + """
            public interface IRepository { Task<INamed> FindAsync(int id); }
            public class Repository
            {
                public async Task<Named> FindAsync(int id)
                {
                    await Task.Yield();
                    return id > 0 ? new Named() : throw new InvalidOperationException("id " + id);
                }
            }

            public static partial class Ops
            {
                [DuckTyped]
                public static async Task<string> Describe(IRepository repository, int id)
                {
                    try { return (await repository.FindAsync(id)).Name; }
                    catch (InvalidOperationException e) { return e.Message; }
                }
            }

            public static class Entry
            {
                public static string Run() =>
                    Ops.Describe(new Repository(), 1).GetAwaiter().GetResult() + "|" + Ops.Describe(new Repository(), 0).GetAwaiter().GetResult();
            }
            """;

        Assert.Equal("Steven|id 0", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void TaskOfAnUnrelatedType_ReportsIfItQuacks001()
    {
        const string source = Shapes + """
            public interface IRepository { Task<INamed> FindAsync(int id); }
            public class Repository { public Task<string> FindAsync(int id) => Task.FromResult(""); }

            public static class Entry
            {
                public static object Run() => Duck.As<IRepository>(new Repository());
            }
            """;

        Assert.Contains("IFITQUACKS001", GeneratorTestHelper.GetDiagnosticIds(source));
    }

    [Fact]
    public void AsyncSequence_OfAnUnrelatedType_IsAdaptedElementByElement()
    {
        const string source = Shapes + """
            public class Person(string name) { public string Name => name; }

            public static partial class Ops
            {
                [DuckTyped]
                public static async Task<string> Join(IAsyncEnumerable<INamed> people)
                {
                    var names = new List<string>();
                    await foreach (var person in people)
                        names.Add(person.Name);
                    return string.Join(",", names);
                }
            }

            public static class Entry
            {
                private static async IAsyncEnumerable<Person> People()
                {
                    yield return new Person("Steven");
                    await Task.Yield();
                    yield return new Person("Donald");
                }

                public static string Run() => Ops.Join(People()).GetAwaiter().GetResult();
            }
            """;

        Assert.Equal("Steven,Donald", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void AsyncSequence_PassesTheCancellationTokenThrough()
    {
        const string source = Shapes + """
            public class Person(string name) { public string Name => name; }

            public static class Entry
            {
                private static async IAsyncEnumerable<Person> People([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token = default)
                {
                    yield return new Person(token.CanBeCanceled ? "token" : "none");
                    await Task.Yield();
                }

                public static string Run()
                {
                    using var cts = new CancellationTokenSource();
                    var view = Duck.As<IAsyncEnumerable<INamed>>(People());
                    var enumerator = view.GetAsyncEnumerator(cts.Token);
                    enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult();
                    return enumerator.Current.Name;
                }
            }
            """;

        Assert.Equal("token", GeneratorTestHelper.CompileAndRun(source));
    }

    [Theory]
    [InlineData("Task", "ValueTask", "default")]
    [InlineData("ValueTask", "Task", "Task.CompletedTask")]
    [InlineData("ValueTask", "ValueTask<int>", "new(1)")]
    [InlineData("Task", "Task<int>", "Task.FromResult(1)")]
    public void NonGenericTaskAndValueTask_AreSatisfiedByAnyTaskOrValueTask(string shapeReturn, string concreteReturn, string value)
    {
        var source = Shapes + $$"""
            public interface ISaver { {{shapeReturn}} SaveAsync(); }
            public class Saver { public int Saved; public {{concreteReturn}} SaveAsync() { Saved++; return {{value}}; } }

            public static class Entry
            {
                public static string Run()
                {
                    var saver = new Saver();
                    var view = Duck.As<ISaver>(saver);
                    view.SaveAsync().GetAwaiter().GetResult();
                    return saver.Saved.ToString();
                }
            }
            """;

        Assert.Equal("1", GeneratorTestHelper.CompileAndRun(source));
    }
}
