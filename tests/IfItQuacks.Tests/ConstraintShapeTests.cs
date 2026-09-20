using Xunit;

namespace IfItQuacks.Tests;

public class ConstraintShapeTests
{
    [Fact]
    public void InstanceOnlyShape_ClassArgument_UsesConstraintOverload()
    {
        const string source = """
            using IfItQuacks;
            public interface IPerson { string Name { get; } int Age { get; } }
            public class Person { public string Name => "Steven"; public int Age => 40; }
            public static partial class Ops
            {
                [DuckTyped]
                public static int Describe<T>(T person) where T : IPerson => person.Name.Length + person.Age;
            }
            public static class Entry { public static int Run() => Ops.Describe(new Person()); }
            """;
        Assert.Equal(46, GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ReadonlyStructArgument_IsAdapted()
    {
        const string source = """
            using IfItQuacks;
            public interface IPerson { string Name { get; } }
            public readonly struct Person { public string Name => "Steven"; }
            public static partial class Ops
            {
                [DuckTyped]
                public static int Describe<T>(T person) where T : IPerson => person.Name.Length;
            }
            public static class Entry { public static int Run() => Ops.Describe(new Person()); }
            """;
        Assert.Equal(6, GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ArgumentImplementingShape_BindsDirectly()
    {
        const string source = """
            using IfItQuacks;
            public interface IPerson { string Name { get; } }
            public class Person : IPerson { public string Name => "Steven"; }
            public static partial class Ops
            {
                [DuckTyped]
                public static int Describe<T>(T person) where T : IPerson => person.Name.Length;
            }
            public static class Entry { public static int Run() => Ops.Describe(new Person()); }
            """;
        Assert.Equal(6, GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void TwoIndependentConstrainedParameters_AreAdapted()
    {
        const string source = """
            using IfItQuacks;
            public interface INamed { string Name { get; } }
            public class Person { public string Name => "Steven"; }
            public class Mallard { public string Name => "Donald"; }
            public static partial class Ops
            {
                [DuckTyped]
                public static string Greet<T1, T2>(T1 a, T2 b) where T1 : INamed where T2 : INamed => a.Name + " & " + b.Name;
            }
            public static class Entry { public static string Run() => Ops.Greet(new Person(), new Mallard()); }
            """;
        Assert.Equal("Steven & Donald", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void MethodIndexerEventAndDefaultMember_AreAdapted()
    {
        const string source = """
            using IfItQuacks;
            using System;
            public interface IThing
            {
                int Compute(int x);
                string this[int i] { get; }
                event Action? Changed;
                string Label => "default";
            }
            public class Thing
            {
                public int Compute(int x) => x * 2;
                public string this[int i] => i.ToString();
                public event Action? Changed;
                public void Fire() => Changed?.Invoke();
            }
            public static partial class Ops
            {
                [DuckTyped]
                public static string Use<T>(T t) where T : IThing
                {
                    t.Changed += () => { };
                    return t.Compute(3) + t[7] + t.Label;
                }
            }
            public static class Entry { public static string Run() => Ops.Use(new Thing()); }
            """;
        Assert.Equal("67default", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void InstanceMethod_IsAdapted()
    {
        const string source = """
            using IfItQuacks;
            public interface INamed { string Name { get; } }
            public class Person { public string Name => "Steven"; }
            public partial class Greeter
            {
                [DuckTyped]
                public string Greet<T>(T named) where T : INamed => "Hi " + named.Name;
            }
            public static class Entry { public static string Run() => new Greeter().Greet(new Person()); }
            """;
        Assert.Equal("Hi Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void GenericShape_IsAdapted()
    {
        const string source = """
            using IfItQuacks;
            public interface IContainer<TItem> { TItem Get(); }
            public class IntBox { public int Get() => 42; }
            public static partial class Ops
            {
                [DuckTyped]
                public static int Unwrap<T>(T box) where T : IContainer<int> => box.Get();
            }
            public static class Entry { public static int Run() => Ops.Unwrap(new IntBox()); }
            """;
        Assert.Equal(42, GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ExtensionMethod_IsAdapted()
    {
        const string source = """
            using IfItQuacks;
            public interface INamed { string Name { get; } }
            public class Person { public string Name => "Steven"; }
            public static partial class Ops
            {
                [DuckTyped]
                public static string Greet<T>(this T named) where T : INamed => "Hi " + named.Name;
            }
            public static class Entry { public static string Run() => new Person().Greet(); }
            """;
        Assert.Equal("Hi Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void AnonymousTypeArgument_IsReported()
    {
        const string source = """
            using IfItQuacks;
            public interface INamed { string Name { get; } }
            public static partial class Ops
            {
                [DuckTyped]
                public static string Greet<T>(T named) where T : INamed => "Hi " + named.Name;
            }
            public static class Entry { public static string Run() => Ops.Greet(new { Name = "Steven" }); }
            """;
        Assert.Contains("IFITQUACKS001", GeneratorTestHelper.GetDiagnosticIds(source));
    }

    [Fact]
    public void ConstraintNextToInterfaceParameter_AdaptsBoth()
    {
        const string source = """
            using IfItQuacks;
            public interface INamed { string Name { get; } }
            public interface ILog { void Write(string message); }
            public class Person { public string Name => "Steven"; }
            public class Recorder { public string Last = ""; public void Write(string message) => Last = message; }
            public static partial class Ops
            {
                [DuckTyped]
                public static string Greet<T>(T named, ILog log) where T : INamed
                {
                    log.Write(named.Name);
                    return named.Name;
                }
            }
            public static class Entry
            {
                public static string Run()
                {
                    var recorder = new Recorder();
                    return Ops.Greet(new Person(), recorder) + "/" + recorder.Last;
                }
            }
            """;
        Assert.Equal("Steven/Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ConstraintNextToInterfaceParameter_AcceptsImplementation()
    {
        const string source = """
            using IfItQuacks;
            public interface INamed { string Name { get; } }
            public interface ILog { void Write(string message); }
            public class Person { public string Name => "Steven"; }
            public class Recorder : ILog { public string Last = ""; public void Write(string message) => Last = message; }
            public static partial class Ops
            {
                [DuckTyped]
                public static string Greet<T>(T named, ILog log) where T : INamed
                {
                    log.Write(named.Name);
                    return named.Name;
                }
            }
            public static class Entry
            {
                public static string Run()
                {
                    var recorder = new Recorder();
                    return Ops.Greet(new Person(), recorder) + "/" + recorder.Last;
                }
            }
            """;
        Assert.Equal("Steven/Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ConstraintNextToInterfaceParameter_AcceptsNull()
    {
        const string source = """
            using IfItQuacks;
            public interface INamed { string Name { get; } }
            public interface ILog { void Write(string message); }
            public class Person { public string Name => "Steven"; }
            public static partial class Ops
            {
                [DuckTyped]
                public static string Greet<T>(T named, ILog? log = null) where T : INamed
                {
                    log?.Write(named.Name);
                    return named.Name;
                }
            }
            public static class Entry { public static string Run() => Ops.Greet(new Person()); }
            """;
        Assert.Equal("Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void MemberTypeSharingTheArgumentsNamePrefix_IsNotRewritten()
    {
        const string source = """
            using IfItQuacks;
            public readonly struct PersonId { public int Value => 7; }
            public interface IHasId { PersonId Id { get; } }
            public class Person { public PersonId Id => new(); }
            public static partial class Ops
            {
                [DuckTyped]
                public static int Read<T>(T holder) where T : IHasId => holder.Id.Value;
            }
            public static class Entry { public static int Run() => Ops.Read(new Person()); }
            """;
        Assert.Equal(7, GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ShapeNameContainingTheArgumentsName_IsNotRewritten()
    {
        const string source = """
            using IfItQuacks;
            namespace N
            {
                public interface PersonShape { string Name { get; } }
                public class Person { public string Name => "Steven"; }
                public static partial class Ops
                {
                    [DuckTyped]
                    public static string Greet<T>(T named) where T : PersonShape => "Hi " + named.Name;
                }
            }
            public static class Entry { public static string Run() => N.Ops.Greet(new N.Person()); }
            """;
        Assert.Equal("Hi Steven", GeneratorTestHelper.CompileAndRun(source));
    }
}

