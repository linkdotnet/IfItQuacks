using System.Globalization;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IfItQuacks.Tests;

public class InaccessibleTypeTests
{
    private const string Shapes = """
        using System.Collections.Generic;
        using System.Linq;
        using IfItQuacks;

        public interface INamed { string Name { get; } }
        public interface INamedOther { string Name { get; } int Other { get; } }
        public class NameDto { public string Name { get; set; } = ""; }

        public static partial class Ops
        {
            [DuckTyped] public static string Greet(INamed n) => n.Name;
            [DuckTyped] public static string Shout(this INamed n) => n.Name.ToUpperInvariant();
            [DuckTyped] public static string Join(IEnumerable<INamed> people) => string.Join(",", people.Select(p => p.Name));
            [DuckTyped] public static int Describe<T>(T person) where T : INamed => person.Name.Length;
            [DuckTyped] public static string Pair(INamed first, INamed second) => first.Name + second.Name;
        }

        public static partial class Over
        {
            [DuckTyped] public static string Greet(INamed n) => n.Name;
            public static string Greet(object o) => "object";
        }

        public class Visible { public string Name => "visible"; }
        """;

    public static TheoryData<string> EntryPoints =>
    [
        "Ops.Greet(new Hidden())",
        "new Hidden().Shout()",
        "Duck.As<INamed>(new Hidden()).Name",
        "Duck.Stub<INamed>(new Hidden()).Name",
        "Duck.Merge<INamedOther>(new Hidden(), new { Other = 1 }).Name",
        "Duck.To<NameDto>(new Hidden()).Name",
        "new System.Func<Hidden, string>(Ops.Greet)(new Hidden())",
        "Ops.Join(new List<Hidden> { new Hidden() })",
        "Ops.Join(new[] { new Hidden() })",
        "Ops.Describe(new Hidden())",
        "Over.Greet(new Hidden())",
    ];

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public void PrivateNestedArgumentType_ReportsIfItQuacks013(string call)
    {
        var errors = Errors(Source("private class Hidden { public string Name => \"hidden\"; }", call));

        // Without the generated overload a duck-typed constraint call also fails the constraint, as it does for any argument that can't be adapted.
        var error = Assert.Single(errors, e => e.Id != "CS0311");
        Assert.Equal("IFITQUACKS013", error.Id);
        Assert.Contains("'Host.Hidden' is private", error.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("protected", "protected")]
    [InlineData("private protected", "private protected")]
    public void ProtectedNestedArgumentType_ReportsIfItQuacks013(string modifier, string reported)
    {
        var errors = Errors(Source($"{modifier} class Hidden {{ public string Name => \"hidden\"; }}", "Ops.Greet(new Hidden())"));

        var error = Assert.Single(errors);
        Assert.Equal("IFITQUACKS013", error.Id);
        Assert.Contains($"'Host.Hidden' is {reported}", error.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void PublicTypeNestedInPrivateType_ReportsTheInaccessibleEnclosingType()
    {
        var errors = Errors(Source("private class Outer { public class Hidden { public string Name => \"hidden\"; } }", "Ops.Greet(new Outer.Hidden())"));

        var error = Assert.Single(errors);
        Assert.Equal("IFITQUACKS013", error.Id);
        Assert.Contains("'Host.Outer' is private", error.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void FileLocalArgumentType_ReportsIfItQuacks013()
    {
        var source = Shapes + """

            file class Hidden { public string Name => "hidden"; }

            public class Host
            {
                public static object Run() => Ops.Greet(new Hidden());
            }
            """;

        var error = Assert.Single(Errors(source));
        Assert.Equal("IFITQUACKS013", error.Id);
        Assert.Contains("'Hidden' is file-local", error.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    // The interceptor names every argument type in its signature, including one that implements the interface itself.
    [Fact]
    public void PrivateArgumentImplementingInterface_NextToAdaptedArgument_ReportsIfItQuacks013()
    {
        var errors = Errors(Source(
            "private class Hidden : INamed { public string Name => \"hidden\"; } public class Named { public string Name => \"named\"; }",
            "Ops.Pair(new Hidden(), new Named())"));

        var error = Assert.Single(errors);
        Assert.Equal("IFITQUACKS013", error.Id);
    }

    [Fact]
    public void PrivateInterfaceAsConversionTarget_ReportsIfItQuacks013()
    {
        var error = Assert.Single(Errors(Source("private interface IHidden { string Name { get; } }", "Duck.As<IHidden>(new Visible()).Name")));

        Assert.Equal("IFITQUACKS013", error.Id);
        Assert.Contains("'Host.IHidden' is private", error.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void DuckTypedMethodWithPrivateParameterType_ReportsIfItQuacks004()
    {
        var source = Shapes + """

            public partial class Host
            {
                private interface IHidden { string Name { get; } }
                [DuckTyped] private static string Greet(IHidden n) => n.Name;
            }
            """;

        var error = Assert.Single(Errors(source));
        Assert.Equal("IFITQUACKS004", error.Id);
        Assert.Contains("'Host.IHidden', which is private", error.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void DuckTypedMethodInPrivateNestedType_ReportsIfItQuacks004()
    {
        var source = Shapes + """

            public partial class Host
            {
                private static partial class Inner { [DuckTyped] public static string Greet(INamed n) => n.Name; }
            }
            """;

        var error = Assert.Single(Errors(source));
        Assert.Equal("IFITQUACKS004", error.Id);
        Assert.Contains("'Host.Inner', which is private", error.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    // Generic methods are called through overloads generated inside the containing type, which can name it.
    [Fact]
    public void GenericDuckTypedMethodInPrivateNestedType_IsAdapted()
    {
        var source = Shapes + """

            public interface IBox<T> { T Get(); }
            public class IntBox { public int Get() => 42; }

            public partial class Host
            {
                private static partial class Inner { [DuckTyped] public static T First<T>(IBox<T> box) => box.Get(); }
                public static object Run() => Inner.First(new IntBox());
            }
            """;

        Assert.Equal(42, GeneratorTestHelper.CompileAndRun(source, "Host"));
    }

    [Theory]
    [InlineData("internal")]
    [InlineData("protected internal")]
    [InlineData("public")]
    public void AccessibleNestedArgumentType_IsAdapted(string modifier)
    {
        var source = Source($"{modifier} class Hidden {{ public string Name => \"hidden\"; }}", "Ops.Greet(new Hidden())");

        Assert.Equal("hidden", GeneratorTestHelper.CompileAndRun(source, "Host"));
    }

    // Nothing is generated for these, so the call compiles as it did before.
    [Theory]
    [InlineData("Duck.As<INamed>(new Hidden()).Name", "hidden")]
    [InlineData("Ops.Greet(new Hidden())", "hidden")]
    public void PrivateArgumentImplementingInterface_IsPassedThrough(string call, string expected)
    {
        var source = Source("private class Hidden : INamed { public string Name => \"hidden\"; }", call);

        Assert.Equal(expected, GeneratorTestHelper.CompileAndRun(source, "Host"));
    }

    // A method group that doesn't match falls back to the runtime check instead of an adapter, which names nothing.
    [Fact]
    public void PrivateMismatchedArgument_ThroughMethodGroup_KeepsRuntimeFallback()
    {
        var source = Source("private class Hidden { public int Age => 1; }", "new System.Func<Hidden, string>(Ops.Greet)(new Hidden())");

        Assert.Empty(Errors(source));
    }

    private static string Source(string declaration, string call) => Shapes + $$"""

        public class Host
        {
            {{declaration}}
            public static object Run() => {{call}};
        }
        """;

    private static List<Diagnostic> Errors(string source) =>
        GeneratorTestHelper.RunGenerator(source).Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
}
