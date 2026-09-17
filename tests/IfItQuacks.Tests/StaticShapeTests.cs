using Xunit;

namespace IfItQuacks.Tests;

public class StaticShapeTests
{
    private const string Money = """
        using IfItQuacks;
        using System;

        public readonly struct Money(decimal amount)
        {
            public decimal Amount { get; } = amount;

            public static Money Zero => new(0m);

            public static Money operator +(Money left, Money right) => new(left.Amount + right.Amount);

            public static Money operator -(Money value) => new(-value.Amount);

            public static Money Parse(string text) => new(decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
        }
        """;

    [Fact]
    public void StaticAbstractOperatorAndProperty_AreAdapted()
    {
        const string source = Money + """
            public interface IAddable<T> where T : IAddable<T>
            {
                static abstract T Zero { get; }
                static abstract T operator +(T left, T right);
            }

            public static partial class Ops
            {
                [DuckTyped]
                public static T Sum<T>(T first, T second) where T : IAddable<T> => first + second + T.Zero;
            }

            public static class Entry
            {
                public static string Run() => Ops.Sum(new Money(1m), new Money(2m)).Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            """;

        Assert.Equal("3", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void StaticAbstractMethodAndUnaryOperator_AreAdapted()
    {
        const string source = Money + """
            public interface IParsable2<T> where T : IParsable2<T>
            {
                static abstract T Parse(string text);
                static abstract T operator -(T value);
            }

            public static partial class Ops
            {
                [DuckTyped]
                public static T Negated<T>(T _, string text) where T : IParsable2<T> => -T.Parse(text);
            }

            public static class Entry
            {
                public static string Run() => Ops.Negated(new Money(0m), "5").Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            """;

        Assert.Equal("-5", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void InstanceMembersOnTheSameConstraint_AreForwarded()
    {
        const string source = Money + """
            public interface IAmount<T> where T : IAmount<T>
            {
                decimal Amount { get; }
                static abstract T operator +(T left, T right);
            }

            public static partial class Ops
            {
                [DuckTyped]
                public static decimal Total<T>(T first, T second) where T : IAmount<T> => (first + second).Amount;
            }

            public static class Entry
            {
                public static string Run() => Ops.Total(new Money(1.5m), new Money(2m)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            """;

        Assert.Equal("3.5", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void GenericMathInterfaceFromTheBcl_IsSatisfiedStructurally()
    {
        const string source = Money + """
            public static partial class Ops
            {
                [DuckTyped]
                public static T Add<T>(T first, T second) where T : System.Numerics.IAdditionOperators<T, T, T> => first + second;
            }

            public static class Entry
            {
                public static string Run() => Ops.Add(new Money(2m), new Money(3m)).Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            """;

        Assert.Equal("5", GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void ClassArgument_SatisfiesStaticConstraint()
    {
        const string source = """
            using IfItQuacks;

            public interface ICreatable<T> where T : ICreatable<T>
            {
                static abstract T Create(int value);
                int Value { get; }
            }

            public sealed class Counter(int value)
            {
                public int Value => value;

                public static Counter Create(int value) => new(value);
            }

            public static partial class Ops
            {
                [DuckTyped]
                public static int Roundtrip<T>(T source) where T : ICreatable<T> => T.Create(source.Value + 1).Value;
            }

            public static class Entry
            {
                public static int Run() => Ops.Roundtrip(new Counter(41));
            }
            """;

        Assert.Equal(42, GeneratorTestHelper.CompileAndRun(source));
    }

    [Fact]
    public void MissingStaticMember_ReportsIfItQuacks001()
    {
        const string source = """
            using IfItQuacks;

            public interface IAddable<T> where T : IAddable<T>
            {
                static abstract T operator +(T left, T right);
            }

            public sealed class Rock;

            public static partial class Ops
            {
                [DuckTyped]
                public static T Sum<T>(T first, T second) where T : IAddable<T> => first + second;
            }

            public static class Entry
            {
                public static void Run() => Ops.Sum(new Rock(), new Rock());
            }
            """;

        Assert.Contains("IFITQUACKS001", GeneratorTestHelper.GetDiagnosticIds(source));
    }

    [Fact]
    public void TypeThatImplementsTheConstraintItself_IsPassedThrough()
    {
        const string source = """
            using IfItQuacks;
            using System.Numerics;

            public static partial class Ops
            {
                [DuckTyped]
                public static T Add<T>(T first, T second) where T : IAdditionOperators<T, T, T> => first + second;
            }

            public static class Entry
            {
                public static int Run() => Ops.Add(40, 2);
            }
            """;

        Assert.Equal(42, GeneratorTestHelper.CompileAndRun(source));
    }
}
