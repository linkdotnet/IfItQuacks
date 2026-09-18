using Xunit;

namespace IfItQuacks.Tests;

public class CallShapeTests
{
    private const string Shapes = """
        using IfItQuacks;

        public interface INamed { string Name { get; } }

        public class Person { public string Name => "Steven"; }

        """;

    [Fact]
    public void ConditionalAccess_IsIntercepted()
    {
        const string source = Shapes + """
            public partial class Greeter
            {
                [DuckTyped]
                public string Greet(INamed named) => named.Name;
            }

            public static class Entry
            {
                public static string Run()
                {
                    Greeter? greeter = new Greeter();
                    Greeter? missing = null;
                    return greeter?.Greet(new Person()) + "|" + (missing?.Greet(new Person()) ?? "<null>");
                }
            }
            """;

        Assert.Equal("Steven|<null>", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ConditionalAccess_OnAnExtensionMethod_IsIntercepted()
    {
        const string source = Shapes + """
            public static partial class Ops
            {
                [DuckTyped]
                public static string Describe(this INamed named) => named.Name;
            }

            public static class Entry
            {
                public static string Run()
                {
                    Person? person = new Person();
                    return person?.Describe() ?? "<null>";
                }
            }
            """;

        Assert.Equal("Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void BaseCall_ReportsIfItQuacks008()
    {
        const string source = Shapes + """
            public partial class Base
            {
                [DuckTyped]
                public virtual string Greet(INamed named) => "base:" + named.Name;
            }

            public partial class Derived : Base
            {
                public override string Greet(INamed named) => "derived:" + named.Name;
                public string ViaBase() => base.Greet(new Person());
            }

            public static class Entry
            {
                public static string Run() => new Derived().ViaBase();
            }
            """;

        Assert.Contains("IFITQUACKS008", GeneratorTestHelper.GetDiagnosticIds(source));
    }

    [Fact]
    public void BaseCall_WithAnArgumentTypedAsTheInterface_StaysUntouched()
    {
        const string source = Shapes + """
            public class Named : INamed { public string Name => "named"; }

            public partial class Base
            {
                [DuckTyped]
                public virtual string Greet(INamed named) => "base:" + named.Name;
            }

            public partial class Derived : Base
            {
                public override string Greet(INamed named) => "derived:" + named.Name;

                public string ViaBase()
                {
                    INamed named = new Named();
                    return base.Greet(named);
                }
            }

            public static class Entry
            {
                public static string Run() => new Derived().ViaBase();
            }
            """;

        Assert.Equal("base:named", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void CallInsideAGenericType_IsIntercepted()
    {
        const string source = Shapes + """
            public static partial class Ops
            {
                [DuckTyped]
                public static string Greet(INamed named) => named.Name;
            }

            public class Holder<T>
            {
                public string Call() => Ops.Greet(new Person());
            }

            public static class Entry
            {
                public static string Run() => new Holder<int>().Call();
            }
            """;

        Assert.Equal("Steven", GeneratorTestHelper.CompileAndRun(source));
    }
}
