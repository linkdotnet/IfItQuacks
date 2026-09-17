using Xunit;

namespace IfItQuacks.Tests;

public class AnyInterfaceTests
{
    [Fact]
    public void FrameworkInterfaces_CanBeDuckTyped()
    {
        const string source = """
            using IfItQuacks;
            using System;
            using System.Collections.Generic;
            using System.Linq;

            public class Countdown
            {
                public List<int>.Enumerator GetEnumerator() => new List<int> { 3, 2, 1 }.GetEnumerator();
            }

            public class Connection
            {
                public bool Closed { get; private set; }
                public void Dispose() => Closed = true;
            }

            public static class Entry
            {
                public static string Run()
                {
                    var connection = new Connection();
                    using (Duck.As<IDisposable>(connection)) { }
                    return string.Join(",", Duck.As<IEnumerable<int>>(new Countdown()).Select(i => i * 2)) + "|" + connection.Closed;
                }
            }
            """;

        Assert.Equal("6,4,2|True", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void RegularInterfaceParameters_AcceptImplementationsAndDucks()
    {
        const string source = """
            using IfItQuacks;
            using System.Collections.Generic;

            public class Names { public List<string>.Enumerator GetEnumerator() => new List<string> { "b" }.GetEnumerator(); }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Join(IEnumerable<string> first, IEnumerable<object> second, IReadOnlyCollection<string> third) =>
                    string.Join(",", first) + "|" + string.Join(",", second) + "|" + third.Count;
            }

            public static class Entry
            {
                public static string Run() => Ops.Join(new[] { "a" }, new Names(), new Dictionary<string, int> { ["x"] = 1 }.Keys);
            }
            """;

        Assert.Equal("a|b|1", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void InterfaceParameterThatDoesNotNeedDuckTyping_AcceptsImplementationsDucksNullAndDefaults()
    {
        const string source = """
            using IfItQuacks;
            using System.Text;

            public interface INamed { string Name { get; } }
            public interface ILogger { void Log(string message); }

            public class Person { public string Name => "Steven"; }
            public class Tag : INamed { public string Name => "Tag"; }
            public class ConsoleLogger : ILogger { public StringBuilder Lines = new(); public void Log(string message) => Lines.Append(message + ";"); }
            public class FakeLogger { public StringBuilder Lines = new(); public void Log(string message) => Lines.Append("fake " + message + ";"); }

            public partial class Greeter
            {
                [DuckTyped]
                public string Greet(INamed named, ILogger? logger = null)
                {
                    logger?.Log(named.Name);
                    return named.Name;
                }
            }

            public static class Entry
            {
                public static string Run()
                {
                    var greeter = new Greeter();
                    var real = new ConsoleLogger();
                    var fake = new FakeLogger();
                    ILogger typed = real;

                    greeter.Greet(new Person(), real);
                    greeter.Greet(new Person(), fake);
                    greeter.Greet(new Person(), typed);
                    greeter.Greet(new Tag(), fake);
                    greeter.Greet(new Person(), null);
                    greeter.Greet(new Person(), default);
                    greeter.Greet(new Person());
                    greeter.Greet(named: new Tag(), logger: real);
                    return real.Lines + "|" + fake.Lines;
                }
            }
            """;

        Assert.Equal("Steven;Steven;Tag;|fake Steven;fake Tag;", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void NullForRequiredInterfaceParameter_StillDuckTypesTheOthers()
    {
        const string source = """
            using IfItQuacks;

            public interface INamed { string Name { get; } }

            public class Person { public string Name => "Steven"; }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Meet(INamed? first, INamed? second) => (first?.Name ?? "-") + (second?.Name ?? "-");
            }

            public static class Entry
            {
                public static string Run() => Ops.Meet(null, new Person()) + Ops.Meet(new Person(), null) + Ops.Meet(null, null);
            }
            """;

        Assert.Equal("-StevenSteven---", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ExplicitImplementationsAndVariance_ArePassedThroughWithoutAdapter()
    {
        const string source = """
            using IfItQuacks;
            using System.Collections.Generic;

            public static class Entry
            {
                public static string Run()
                {
                    var dictionary = new Dictionary<string, int> { ["a"] = 1 };
                    var collection = Duck.As<ICollection<KeyValuePair<string, int>>>(dictionary);
                    var strings = new List<string> { "x" };
                    var objects = Duck.As<IEnumerable<object>>(strings);
                    return $"{ReferenceEquals(collection, dictionary)}|{ReferenceEquals(objects, strings)}";
                }
            }
            """;

        Assert.Equal("True|True", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void InterfaceWithStaticAbstractMembers_WorksForImplementingTypes_AndReportsIfItQuacks005ForDucks()
    {
        const string implementing = """
            using IfItQuacks;

            public interface IParsable<TSelf> where TSelf : IParsable<TSelf> { static abstract TSelf Parse(string s); string Name { get; } }

            public class Real : IParsable<Real> { public static Real Parse(string s) => new(); public string Name => "real"; }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Describe(IParsable<Real> value) => value.Name;
            }

            public static class Entry
            {
                public static string Run() => Ops.Describe(new Real());
            }
            """;

        Assert.Equal("real", GeneratorTestHelper.CompileAndRun(implementing));
        Assert.Contains("IFITQUACKS005", GeneratorTestHelper.GetDiagnosticIds(implementing.Replace("Ops.Describe(new Real())", "Ops.Describe(new Duck())", StringComparison.Ordinal) +
            "\npublic class Duck { public string Name => \"duck\"; }"));
    }

    [Fact]
    public void InheritedMembersWithSameName_AreImplementedExplicitly()
    {
        const string source = """
            using IfItQuacks;

            public interface IBase { string Name { get; } }
            public interface IDerived : IBase { new string Name { get; } string ToString(); }

            public class Item { public string Name => "x"; public override string ToString() => "item"; }

            public static class Entry
            {
                public static string Run()
                {
                    var derived = Duck.As<IDerived>(new Item());
                    return derived.Name + ((IBase)derived).Name + derived.ToString();
                }
            }
            """;

        Assert.Equal("xxitem", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void UnverifiableCallFromGenericCode_UsesRuntimeImplementation_OrThrows()
    {
        const string source = """
            using IfItQuacks;

            public interface INamed { string Name { get; } }

            public class Real : INamed { public string Name => "real"; }
            public class Fake { public string Name => "fake"; }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Greet(INamed named, string greeting = "Hi") => greeting + " " + named.Name;

                public static string Forward<T>(T value) => Greet(value);
            }

            public static class Entry
            {
                public static string Run()
                {
                    var fromGeneric = Ops.Forward(new Real());
                    try
                    {
                        Ops.Forward(new Fake());
                        return "no exception";
                    }
                    catch (DuckTypeMismatchException)
                    {
                        return fromGeneric;
                    }
                }
            }
            """;

        Assert.Equal("Hi real", GeneratorTestHelper.CompileAndRun(source));
    }
}
