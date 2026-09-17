using Xunit;

namespace IfItQuacks.Tests;

public class AssignabilityTests
{
    [Fact]
    public void CovariantReturnTypes_AreAccepted()
    {
        const string source = """
            using IfItQuacks;
            using System.Collections.Generic;

            public interface ISource
            {
                object Name();
                IEnumerable<int> Numbers();
                long Count();
                int? Maybe();
                object Boxed();
            }

            public class Source
            {
                public string Name() => "src";
                public List<int> Numbers() => [1, 2];
                public int Count() => 3;
                public int Maybe() => 4;
                public int Boxed() => 5;
            }

            public static class Entry
            {
                public static string Run()
                {
                    var s = Duck.As<ISource>(new Source());
                    return $"{s.Name()}|{string.Join(",", s.Numbers())}|{s.Count()}|{s.Maybe()}|{s.Boxed()}";
                }
            }
            """;

        Assert.Equal("src|1,2|3|4|5", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ContravariantParameterTypes_AreAccepted()
    {
        const string source = """
            using IfItQuacks;

            public interface IPrinter { string Print(string text, int number); }

            public class Printer { public string Print(object text, long number) => text + ":" + number; }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Run(IPrinter printer) => printer.Print("x", 1);
            }

            public static class Entry
            {
                public static string Run() => Ops.Run(new Printer());
            }
            """;

        Assert.Equal("x:1", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void VoidShapeMethod_DiscardsReturnValue()
    {
        const string source = """
            using IfItQuacks;
            using System.Collections.Generic;

            public interface IAdder { void Add(int value); }

            public static class Entry
            {
                public static int Run()
                {
                    var set = new HashSet<int>();
                    var adder = Duck.As<IAdder>(set);
                    adder.Add(1);
                    adder.Add(1);
                    return set.Count;
                }
            }
            """;

        Assert.Equal(1, GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void GetOnlyPropertyIsCovariant_SetOnlyPropertyIsContravariant()
    {
        const string source = """
            using IfItQuacks;

            public interface IBox
            {
                object Value { get; }
                string Label { set; }
            }

            public class Box
            {
                public string Value => "v";
                public object Label { get; set; } = "";
            }

            public static class Entry
            {
                public static string Run()
                {
                    var box = new Box();
                    var shape = Duck.As<IBox>(box);
                    shape.Label = "l";
                    return shape.Value + "|" + box.Label;
                }
            }
            """;

        Assert.Equal("v|l", GeneratorTestHelper.CompileAndRun(source));
    }

    [Theory]
    [InlineData("public interface IBox { object Value { get; set; } }", "public class Box { public string Value { get; set; } = \"\"; }")]
    [InlineData("public interface IBox { string Value { set; } }", "public class Box { public int Value { set { } } }")]
    [InlineData("public interface IBox { object Get(); }", "public class Box { public void Get() { } }")]
    [InlineData("public interface IBox { void Set(ref object value); }", "public class Box { public void Set(ref string value) { } }")]
    [InlineData("public interface IBox { void Set(object value); }", "public class Box { public void Set(string value) { } }")]
    [InlineData("public interface IBox { Wrapper Get(); }", "public class Box { public int Get() => 1; }")]
    public void IncompatibleTypes_ReportIfItQuacks001(string shape, string type)
    {
        var source = $$"""
            using IfItQuacks;

            public class Wrapper { public static implicit operator Wrapper(int value) => new(); }

            {{shape}}

            {{type}}

            public static class Entry
            {
                public static IBox Run() => Duck.As<IBox>(new Box());
            }
            """;

        Assert.Contains("IFITQUACKS001", GeneratorTestHelper.GetDiagnosticIds(source));
    }

    [Fact]
    public void ExactOverload_IsPreferredOverAssignableOne()
    {
        const string source = """
            using IfItQuacks;

            public interface IWriter { string Write(string value); }

            public class Writer
            {
                public string Write(object value) => "object";
                public string Write(string value) => "string";
            }

            public static class Entry
            {
                public static string Run() => Duck.As<IWriter>(new Writer()).Write("x");
            }
            """;

        Assert.Equal("string", GeneratorTestHelper.CompileAndRun(source));
    }
}
