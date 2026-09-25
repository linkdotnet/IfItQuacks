using System.Globalization;
using Xunit;

namespace IfItQuacks.Tests;

public class InaccessibleTypeTests
{
    private const string Shapes = """
        using IfItQuacks;

        public interface INamed { string Name { get; } }

        public static partial class Ops
        {
            [DuckTyped]
            public static string Greet(INamed n) => $"duck:{n.Name}";
        }

        """;

    [Theory]
    [InlineData("private")]
    [InlineData("protected")]
    [InlineData("private protected")]
    public void NestedTypeInPartialHost_IsAdapted(string modifier)
    {
        var source = Shapes + $$"""
            public partial class Host
            {
                {{modifier}} class Hidden { public string Name => "hidden"; }
                public static string Run() => Ops.Greet(new Hidden());
            }
            """;

        Assert.Equal("duck:hidden", GeneratorTestHelper.CompileAndRun(source, "Host"));
    }

    [Fact]
    public void DuckAs_NestedTypeInPartialHost_IsAdapted()
    {
        const string source = Shapes + """
            public partial class Host
            {
                private class Hidden { public string Name => "hidden"; }
                public static string Run() => Duck.As<INamed>(new Hidden()).Name;
            }
            """;

        Assert.Equal("hidden", GeneratorTestHelper.CompileAndRun(source, "Host"));
    }

    [Fact]
    public void NestedTypeInNonPartialHost_ReportsIfItQuacks001()
    {
        const string source = Shapes + """
            public static class Host
            {
                private class Hidden { public string Name => "hidden"; }
                public static string Run() => Ops.Greet(new Hidden());
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("IFITQUACKS001", diagnostic.Id);
        Assert.Contains("partial", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
