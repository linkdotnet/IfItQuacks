using Xunit;

namespace IfItQuacks.Tests;

public class DuckToTests
{
    private const string Types = """
        using System;
        using IfItQuacks;

        public class Entity
        {
            public string Name { get; set; } = "Steven";
            public int Age { get; set; } = 40;
        }

        public record PersonDto(string Name, int Age);

        public class SettableDto
        {
            public string Name { get; set; } = "";
            public int Age { get; set; }
        }

        """;

    [Fact]
    public void To_FillsARecordThroughItsConstructor()
    {
        const string source = Types + """
            public static class Entry
            {
                public static string Run()
                {
                    var dto = Duck.To<PersonDto>(new Entity());
                    return $"{dto.Name}/{dto.Age}";
                }
            }
            """;

        Assert.Equal("Steven/40", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void To_FillsSettablePropertiesThroughAnObjectInitializer()
    {
        const string source = Types + """
            public static class Entry
            {
                public static string Run()
                {
                    var dto = Duck.To<SettableDto>(new Entity());
                    return $"{dto.Name}/{dto.Age}";
                }
            }
            """;

        Assert.Equal("Steven/40", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void To_CopiesInsteadOfForwarding()
    {
        const string source = Types + """
            public static class Entry
            {
                public static string Run()
                {
                    var entity = new Entity();
                    var dto = Duck.To<SettableDto>(entity);
                    entity.Name = "changed";
                    return dto.Name;
                }
            }
            """;

        Assert.Equal("Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void To_ReadsAnonymousTypesAndFields()
    {
        const string source = Types + """
            public class Legacy { public string Name = "Field"; public int Age = 1; }

            public static class Entry
            {
                public static string Run()
                {
                    var fromAnonymous = Duck.To<PersonDto>(new { Name = "Donald", Age = 90 });
                    var fromFields = Duck.To<PersonDto>(new Legacy());
                    return $"{fromAnonymous.Name}/{fromAnonymous.Age}|{fromFields.Name}/{fromFields.Age}";
                }
            }
            """;

        Assert.Equal("Donald/90|Field/1", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void To_WidensMemberTypes()
    {
        const string source = Types + """
            public class Narrow { public string Name { get; set; } = "N"; public short Age { get; set; } = 2; }

            public static class Entry
            {
                public static string Run()
                {
                    var dto = Duck.To<PersonDto>(new Narrow());
                    return $"{dto.Name}/{dto.Age}";
                }
            }
            """;

        Assert.Equal("N/2", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void To_WithAMemberTheSourceCannotFill_ReportsIfItQuacks009()
    {
        const string source = Types + """
            public class OnlyName { public string Name { get; set; } = ""; }

            public static class Entry
            {
                public static object Run() => Duck.To<PersonDto>(new OnlyName());
            }
            """;

        Assert.Contains("IFITQUACKS009", GeneratorTestHelper.GetDiagnosticIds(source));
    }

    [Fact]
    public void To_WithAnInterfaceTarget_ReportsIfItQuacks009()
    {
        const string source = Types + """
            public interface INamed { string Name { get; } }

            public static class Entry
            {
                public static object Run() => Duck.To<INamed>(new Entity());
            }
            """;

        Assert.Contains("IFITQUACKS009", GeneratorTestHelper.GetDiagnosticIds(source));
    }
}
