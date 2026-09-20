using Xunit;

namespace IfItQuacks.Tests;

public class MappedShapeTests
{
    private const string Customer = """
        using IfItQuacks;
        using System;

        public class Customer
        {
            public int Id { get; set; } = 1;
            public string Name { get; set; } = "Steven";
            public string Email { get; set; } = "steven@example.com";
            public string PasswordHash { get; set; } = "secret";
            public string Describe() => Name;
        }
        """;

    [Fact]
    public void Omit_DerivesEveryOtherMember()
    {
        const string source = Customer + """
            [DuckShape<Customer>(Omit = [nameof(Customer.PasswordHash)])]
            public partial interface ICustomerView;

            public static partial class Ops
            {
                [DuckTyped]
                public static string Render(ICustomerView view) => $"{view.Id}/{view.Name}/{view.Email}";
            }

            public static class Entry { public static string Run() => Ops.Render(new Customer()); }
            """;

        Assert.Equal("1/Steven/steven@example.com", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void Pick_DerivesOnlyTheNamedMembers()
    {
        const string source = Customer + """
            [DuckShape<Customer>(Pick = [nameof(Customer.Id), nameof(Customer.Name)])]
            public partial interface ICustomerKey;

            public static partial class Ops
            {
                [DuckTyped]
                public static string Render(ICustomerKey key) => $"{key.Id}/{key.Name}";
            }

            public static class Entry { public static string Run() => Ops.Render(new Customer()); }
            """;

        Assert.Equal("1/Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void DerivedShape_IsSatisfiedByAnAnonymousTypeAndADto()
    {
        const string source = Customer + """
            public record CustomerDto(int Id, string Name);

            [DuckShape<Customer>(Pick = [nameof(Customer.Id), nameof(Customer.Name)], Readonly = true)]
            public partial interface ICustomerKey;

            public static partial class Ops
            {
                [DuckTyped]
                public static string Render(ICustomerKey key) => $"{key.Id}/{key.Name}";
            }

            public static class Entry
            {
                public static string Run() =>
                    Ops.Render(new Customer()) + "|" +
                    Ops.Render(new CustomerDto(2, "Donald")) + "|" +
                    Ops.Render(new { Id = 3, Name = "Daisy" });
            }
            """;

        Assert.Equal("1/Steven|2/Donald|3/Daisy", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void Readonly_DropsSetters()
    {
        const string source = Customer + """
            [DuckShape<Customer>(Pick = [nameof(Customer.Name)], Readonly = true)]
            public partial interface ICustomerName;

            public class NameOnly { public string Name => "Donald"; }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Render(ICustomerName view) => view.Name;
            }

            public static class Entry { public static string Run() => Ops.Render(new NameOnly()); }
            """;

        Assert.Equal("Donald", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void Optional_MakesMembersNullableAndStillMatchesTheSource()
    {
        const string source = Customer + """
            [DuckShape<Customer>(Pick = [nameof(Customer.Id), nameof(Customer.Name)], Optional = true, Readonly = true)]
            public partial interface ICustomerPatch;

            public static partial class Ops
            {
                [DuckTyped]
                public static string Render(ICustomerPatch patch) => $"{patch.Id?.ToString() ?? "-"}/{patch.Name ?? "-"}";
            }

            public static class Entry
            {
                public static string Run() =>
                    Ops.Render(new Customer()) + "|" + Ops.Render(new { Id = (int?)null, Name = (string?)null });
            }
            """;

        Assert.Equal("1/Steven|-/-", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void SeveralAttributes_IntersectTheSources()
    {
        const string source = """
            using IfItQuacks;

            public interface IReadable { string Read(); }
            public interface IWritable { void Write(string value); }

            [DuckShape<IReadable>(IncludeMethods = true)]
            [DuckShape<IWritable>(IncludeMethods = true)]
            public partial interface IStream;

            public class Buffer
            {
                public string Value = "";
                public string Read() => Value;
                public void Write(string value) => Value = value;
            }

            public static partial class Ops
            {
                [DuckTyped]
                public static string RoundTrip(IStream stream)
                {
                    stream.Write("duck");
                    return stream.Read();
                }
            }

            public static class Entry { public static string Run() => Ops.RoundTrip(new Buffer()); }
            """;

        Assert.Equal("duck", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void IncludeMethods_DerivesMethods()
    {
        const string source = Customer + """
            [DuckShape<Customer>(Pick = [nameof(Customer.Describe)], IncludeMethods = true)]
            public partial interface IDescribable;

            public static partial class Ops
            {
                [DuckTyped]
                public static string Render(IDescribable value) => value.Describe();
            }

            public static class Entry { public static string Run() => Ops.Render(new Customer()); }
            """;

        Assert.Equal("Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void DerivedShape_WorksWithDuckAsAndDuckTo()
    {
        const string source = Customer + """
            public record CustomerDto(int Id, string Name);

            [DuckShape<Customer>(Pick = [nameof(Customer.Id), nameof(Customer.Name)], Readonly = true)]
            public partial interface ICustomerKey;

            public static class Entry
            {
                public static string Run()
                {
                    ICustomerKey view = Duck.As<ICustomerKey>(new Customer());
                    var dto = Duck.To<CustomerDto>(new Customer());
                    return $"{view.Id}/{view.Name}|{dto.Id}/{dto.Name}";
                }
            }
            """;

        Assert.Equal("1/Steven|1/Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ManuallyDeclaredMember_IsNotDerivedTwice()
    {
        const string source = Customer + """
            [DuckShape<Customer>(Pick = [nameof(Customer.Name)])]
            public partial interface ICustomerName
            {
                string Name { get; }
            }

            public static partial class Ops
            {
                [DuckTyped]
                public static string Render(ICustomerName view) => view.Name;
            }

            public static class Entry { public static string Run() => Ops.Render(new Customer()); }
            """;

        Assert.Equal("Steven", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void NonPartialInterface_IsReported()
    {
        const string source = Customer + """
            [DuckShape<Customer>]
            public interface ICustomerView;
            """;

        Assert.Contains("IFITQUACKS010", GeneratorTestHelper.GetDiagnosticIds(source));
    }

    [Fact]
    public void PickAndOmitTogether_AreReported()
    {
        const string source = Customer + """
            [DuckShape<Customer>(Pick = [nameof(Customer.Id)], Omit = [nameof(Customer.Name)])]
            public partial interface ICustomerView;
            """;

        Assert.Contains("IFITQUACKS011", GeneratorTestHelper.GetDiagnosticIds(source));
    }

    [Fact]
    public void UnknownMemberName_IsReported()
    {
        const string source = Customer + """
            [DuckShape<Customer>(Omit = ["Nope"])]
            public partial interface ICustomerView;
            """;

        Assert.Contains("IFITQUACKS011", GeneratorTestHelper.GetDiagnosticIds(source));
    }

    [Fact]
    public void ShapeDerivingNoMember_IsReported()
    {
        const string source = Customer + """
            [DuckShape<Customer>(Omit = [nameof(Customer.Id), nameof(Customer.Name), nameof(Customer.Email), nameof(Customer.PasswordHash)])]
            public partial interface ICustomerView;
            """;

        Assert.Contains("IFITQUACKS011", GeneratorTestHelper.GetDiagnosticIds(source));
    }
}
