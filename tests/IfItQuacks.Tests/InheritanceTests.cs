using Xunit;

namespace IfItQuacks.Tests;

public class InheritanceTests
{
    private const string Shapes = """
        using IfItQuacks;

        public interface INamed { string Name { get; } }

        public class Person { public string Name => "Steven"; }

        """;

    [Theory]
    [InlineData("new { Name = \"Steven\" }")]
    [InlineData("new Person()")]
    public void ProtectedDuckTypedBase_CalledFromDerived_RunsOverride(string argument)
    {
        var source = Shapes + $$"""
            public abstract partial class MyBaseClass
            {
                [DuckTyped]
                protected virtual string Method(INamed n) => "base:" + n.Name;
            }

            public class Derived : MyBaseClass
            {
                protected override string Method(INamed n) => "derived:" + n.Name;
                public string Call() => Method({{argument}});
            }

            public static class Entry
            {
                public static string Run() => new Derived().Call();
            }
            """;

        Assert.Equal("derived:Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Theory]
    [InlineData("new Derived().Method(new { Name = \"Steven\" })")]
    [InlineData("new Derived().Method(new Person())")]
    [InlineData("new Derived().Call()")]
    public void DuckTypedOverride_RunsOverride(string call)
    {
        var source = Shapes + $$"""
            public abstract class MyBaseClass
            {
                public virtual string Method(INamed n) => "base:" + n.Name;
            }

            public partial class Derived : MyBaseClass
            {
                [DuckTyped]
                public override string Method(INamed n) => "derived:" + n.Name;
                public string Call() => Method(new Person());
            }

            public static class Entry
            {
                public static string Run() => {{call}};
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Empty(diagnostics);
        Assert.Equal("derived:Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void DuckTypedProtectedOverride_ReportsIfItQuacks004()
    {
        const string source = Shapes + """
            public abstract class MyBaseClass
            {
                protected virtual string Method(INamed n) => "base:" + n.Name;
            }

            public partial class Derived : MyBaseClass
            {
                [DuckTyped]
                protected override string Method(INamed n) => "derived:" + n.Name;
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Equal("IFITQUACKS004", Assert.Single(diagnostics).Id);
        Assert.DoesNotContain(compilation.SyntaxTrees, t => t.FilePath.Contains("Fallback", StringComparison.Ordinal));
    }

    [Fact]
    public void DuckTypedOverride_WithAnotherInheritedOverload_ReportsIfItQuacks004()
    {
        const string source = Shapes + """
            public abstract class MyBaseClass
            {
                public virtual string Method(INamed n) => "base:" + n.Name;
                public string Method(object o) => "object";
            }

            public partial class Derived : MyBaseClass
            {
                [DuckTyped]
                public override string Method(INamed n) => "derived:" + n.Name;
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("IFITQUACKS004", diagnostic.Id);
        Assert.Contains("MyBaseClass.Method(object", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
