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

    private const string Orders = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using IfItQuacks;

        public class Address { public string City { get; set; } = "Zurich"; }
        public class Line { public string Product { get; set; } = ""; public int Quantity { get; set; } }
        public class Order
        {
            public int Id { get; set; } = 7;
            public Address? Address { get; set; } = new();
            public List<Line> Lines { get; set; } = [new() { Product = "Duck", Quantity = 2 }, new() { Product = "Pond", Quantity = 1 }];
        }

        public record AddressDto(string City);
        public record LineDto(string Product, int Quantity);

        """;

    [Fact]
    public void To_BuildsNestedObjectsFromTheirOwnSource()
    {
        const string source = Orders + """
            public record OrderDto(int Id, AddressDto Address);

            public static class Entry
            {
                public static string Run()
                {
                    var dto = Duck.To<OrderDto>(new Order());
                    return $"{dto.Id}/{dto.Address.City}";
                }
            }
            """;

        Assert.Equal("7/Zurich", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void To_BuildsNestedObjectsForSettableProperties()
    {
        const string source = Orders + """
            public class OrderDto { public int Id { get; set; } public AddressDto? Address { get; set; } }

            public static class Entry
            {
                public static string Run()
                {
                    var dto = Duck.To<OrderDto>(new Order());
                    return $"{dto.Id}/{dto.Address!.City}";
                }
            }
            """;

        Assert.Equal("7/Zurich", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void To_KeepsANullNestedObjectNull()
    {
        const string source = Orders + """
            public record OrderDto(int Id, AddressDto? Address);

            public static class Entry
            {
                public static string Run()
                {
                    var dto = Duck.To<OrderDto>(new Order { Address = null });
                    return dto.Address is null ? "null" : "set";
                }
            }
            """;

        Assert.Equal("null", GeneratorTestHelper.CompileAndRun(source));
    }

    [Theory]
    [InlineData("List<LineDto>")]
    [InlineData("LineDto[]")]
    [InlineData("IReadOnlyList<LineDto>")]
    [InlineData("IReadOnlyCollection<LineDto>")]
    [InlineData("IEnumerable<LineDto>")]
    [InlineData("IList<LineDto>")]
    [InlineData("ICollection<LineDto>")]
    public void To_BuildsCollectionsElementByElement(string collectionType)
    {
        var source = Orders + $$"""
            public record OrderDto(int Id, {{collectionType}} Lines);

            public static class Entry
            {
                public static string Run()
                {
                    var dto = Duck.To<OrderDto>(new Order());
                    return string.Join(",", dto.Lines.Select(l => $"{l.Product}x{l.Quantity}"));
                }
            }
            """;

        Assert.Equal("Duckx2,Pondx1", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void To_CopiesTheCollection_SoLaterChangesAreNotVisible()
    {
        const string source = Orders + """
            public record OrderDto(List<LineDto> Lines);

            public static class Entry
            {
                public static string Run()
                {
                    var order = new Order();
                    var dto = Duck.To<OrderDto>(order);
                    order.Lines.Add(new Line());
                    return dto.Lines.Count.ToString();
                }
            }
            """;

        Assert.Equal("2", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void To_ConvertsCollectionsOfAssignableElements()
    {
        const string source = Orders + """
            public class Scores { public int[] Values { get; set; } = [1, 2]; }
            public record ScoresDto(List<long> Values);

            public static class Entry
            {
                public static string Run() => string.Join(",", Duck.To<ScoresDto>(new Scores()).Values);
            }
            """;

        Assert.Equal("1,2", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void To_BuildsNestedCollectionsAndObjectsInCollections()
    {
        const string source = Orders + """
            public class Customer { public List<Order> Orders { get; set; } = [new(), new() { Id = 8 }]; }
            public record OrderDto(int Id, AddressDto Address, LineDto[] Lines);
            public record CustomerDto(IReadOnlyList<OrderDto> Orders);

            public static class Entry
            {
                public static string Run()
                {
                    var dto = Duck.To<CustomerDto>(new Customer());
                    return string.Join("|", dto.Orders.Select(o => $"{o.Id}:{o.Address.City}:{o.Lines.Length}"));
                }
            }
            """;

        Assert.Equal("7:Zurich:2|8:Zurich:2", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void To_FollowsRecursiveTypes()
    {
        const string source = """
            #nullable enable
            using IfItQuacks;

            public class Node { public string Name { get; set; } = ""; public Node? Next { get; set; } }
            public class NodeDto { public string Name { get; set; } = ""; public NodeDto? Next { get; set; } }

            public static class Entry
            {
                public static string Run()
                {
                    var dto = Duck.To<NodeDto>(new Node { Name = "a", Next = new Node { Name = "b" } });
                    return $"{dto.Name}>{dto.Next!.Name}>{(dto.Next.Next is null ? "end" : "more")}";
                }
            }
            """;

        Assert.Equal("a>b>end", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void To_WithANestedMemberTheSourceCannotFill_ReportsIfItQuacks009WithThePath()
    {
        const string source = Orders + """
            public record AddressWithZipDto(string City, string Zip);
            public record OrderDto(int Id, AddressWithZipDto Address);

            public static class Entry
            {
                public static object Run() => Duck.To<OrderDto>(new Order());
            }
            """;

        var diagnostic = Assert.Single(GeneratorTestHelper.RunGenerator(source).Diagnostics);
        Assert.Equal("IFITQUACKS009", diagnostic.Id);
        Assert.Contains("'Address'", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("'Zip'", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void To_DoesNotBuildFrameworkTypesMemberByMember()
    {
        const string source = """
            using IfItQuacks;

            public class VersionInfo { public int Major { get; set; } = 1; public int Minor { get; set; } = 2; }
            public class Release { public VersionInfo Version { get; set; } = new(); }
            public record ReleaseDto(System.Version Version);

            public static class Entry
            {
                public static object Run() => Duck.To<ReleaseDto>(new Release());
            }
            """;

        Assert.Contains("IFITQUACKS009", GeneratorTestHelper.GetDiagnosticIds(source));
    }

    [Theory]
    [InlineData("Dictionary<string, AddressDto>")]
    [InlineData("IDictionary<string, AddressDto>")]
    [InlineData("IReadOnlyDictionary<string, AddressDto>")]
    public void To_BuildsDictionaryValues(string dictionaryType)
    {
        var source = Orders + $$"""
            public class Book { public Dictionary<string, Address> Addresses { get; set; } = new() { ["home"] = new() }; }
            public record BookDto({{dictionaryType}} Addresses);

            public static class Entry
            {
                public static string Run() => Duck.To<BookDto>(new Book()).Addresses["home"].City;
            }
            """;

        Assert.Equal("Zurich", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void To_KeepsTheComparerOfADictionaryWithTheSameKeys()
    {
        const string source = Orders + """
            public class Book { public Dictionary<string, Address> Addresses { get; set; } = new(StringComparer.OrdinalIgnoreCase) { ["Home"] = new() }; }
            public record BookDto(Dictionary<string, AddressDto> Addresses);

            public static class Entry
            {
                public static string Run() => Duck.To<BookDto>(new Book()).Addresses["HOME"].City;
            }
            """;

        Assert.Equal("Zurich", GeneratorTestHelper.CompileAndRun(source));
    }

    [Theory]
    [InlineData("HashSet<LineDto>")]
    [InlineData("ISet<LineDto>")]
    [InlineData("IReadOnlySet<LineDto>")]
    public void To_BuildsSets(string setType)
    {
        var source = Orders + $$"""
            public record OrderDto({{setType}} Lines);

            public static class Entry
            {
                public static string Run() => string.Join(",", Duck.To<OrderDto>(new Order()).Lines.Select(l => l.Product).OrderBy(p => p));
            }
            """;

        Assert.Equal("Duck,Pond", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void To_BuildsASetFromAnyCollection()
    {
        const string source = Orders + """
            public class Labels { public List<int> Values { get; set; } = [1, 1, 2]; }
            public record LabelsDto(HashSet<long> Values);

            public static class Entry
            {
                public static string Run() => string.Join(",", Duck.To<LabelsDto>(new Labels()).Values);
            }
            """;

        Assert.Equal("1,2", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void To_ConvertsEnumsByName()
    {
        const string source = Orders + """
            public enum Status { Open = 1, Closed = 2 }
            public enum StatusDto { Closed, Open, Archived }
            public class Ticket { public Status Status { get; set; } = Status.Closed; public Status? Previous { get; set; } = Status.Open; public Status? Next { get; set; } }
            public record TicketDto(StatusDto Status, StatusDto? Previous, StatusDto? Next);

            public static class Entry
            {
                public static string Run()
                {
                    var dto = Duck.To<TicketDto>(new Ticket());
                    return $"{dto.Status}|{dto.Previous}|{dto.Next?.ToString() ?? "null"}";
                }
            }
            """;

        Assert.Equal("Closed|Open|null", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void To_ConvertsFlagsEnumsWithTheSameValues()
    {
        const string source = Orders + """
            [Flags] public enum Access { Read = 1, Write = 2 }
            [Flags] public enum AccessDto { Read = 1, Write = 2 }
            public class User { public Access Access { get; set; } = Access.Read | Access.Write; }
            public record UserDto(AccessDto Access);

            public static class Entry
            {
                public static string Run() => Duck.To<UserDto>(new User()).Access.ToString();
            }
            """;

        Assert.Equal("Read, Write", GeneratorTestHelper.CompileAndRun(source));
    }

    [Theory]
    [InlineData("public enum Status { Open, Closed }", "public enum StatusDto { Open }", "'Closed'")]
    [InlineData("[Flags] public enum Status { Open = 1, Closed = 2 }", "[Flags] public enum StatusDto { Open = 2, Closed = 1 }", "'Open'")]
    public void To_WithAnEnumMemberTheTargetLacks_ReportsIfItQuacks009(string sourceEnum, string targetEnum, string member)
    {
        var source = Orders + $$"""
            {{sourceEnum}}
            {{targetEnum}}
            public class Ticket { public Status Status { get; set; } }
            public record TicketDto(StatusDto Status);

            public static class Entry
            {
                public static object Run() => Duck.To<TicketDto>(new Ticket());
            }
            """;

        var diagnostic = Assert.Single(GeneratorTestHelper.RunGenerator(source).Diagnostics);
        Assert.Equal("IFITQUACKS009", diagnostic.Id);
        Assert.Contains(member, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void To_AnUnnamedEnumValue_Throws()
    {
        const string source = Orders + """
            public enum Status { Open }
            public enum StatusDto { Open }
            public class Ticket { public Status Status { get; set; } = (Status)5; }
            public record TicketDto(StatusDto Status);

            public static class Entry
            {
                public static object Run() => Duck.To<TicketDto>(new Ticket());
            }
            """;

        var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() => GeneratorTestHelper.CompileAndRun(source));
        Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
    }
}
