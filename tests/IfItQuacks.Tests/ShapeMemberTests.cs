using Microsoft.CodeAnalysis;
using Xunit;

namespace IfItQuacks.Tests;

public class ShapeMemberTests
{
    [Fact]
    public void EventShape_ForwardsSubscriptions()
    {
        const string source = """
            using IfItQuacks;
            using System;

            public interface INotifier { event EventHandler? Changed; void Raise(); }

            public class Model
            {
                public event EventHandler? Changed;
                public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
            }

            public static class Entry
            {
                public static int Run()
                {
                    var count = 0;
                    EventHandler handler = (_, _) => count++;
                    var notifier = Duck.As<INotifier>(new Model());
                    notifier.Changed += handler;
                    notifier.Raise();
                    notifier.Changed -= handler;
                    notifier.Raise();
                    return count;
                }
            }
            """;

        Assert.Equal(1, Run(source));
    }

    [Fact]
    public void IndexerShape_ForwardsGetAndSet()
    {
        const string source = """
            using IfItQuacks;
            using System.Collections.Generic;

            public interface ILookup { string this[string key] { get; set; } }

            public class Settings
            {
                private readonly Dictionary<string, string> _values = new();
                public string this[string key] { get => _values[key]; set => _values[key] = value; }
            }

            public static class Entry
            {
                public static string Run()
                {
                    var lookup = Duck.As<ILookup>(new Settings());
                    lookup["quack"] = "duck";
                    return lookup["quack"];
                }
            }
            """;

        Assert.Equal("duck", Run(source));
    }

    [Fact]
    public void IndexerShape_WithDifferentParameterType_ReportsIfItQuacks001()
    {
        const string source = """
            using IfItQuacks;

            public interface ILookup { string this[string key] { get; } }

            public class List { public string this[int index] => ""; }

            public static class Entry
            {
                public static ILookup Run() => Duck.As<ILookup>(new List());
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "IFITQUACKS001");
    }

    [Fact]
    public void DefaultInterfaceMember_IsOptional_AndForwardedWhenPresent()
    {
        const string source = """
            using IfItQuacks;

            public interface IGreeter
            {
                string Name { get; }
                string Greet() => "Hello, " + Name;
            }

            public class Plain { public string Name => "Plain"; }
            public class Custom { public string Name => "Custom"; public string Greet() => "Quack, " + Name; }

            public static class Entry
            {
                public static string Run() => Duck.As<IGreeter>(new Plain()).Greet() + "|" + Duck.As<IGreeter>(new Custom()).Greet();
            }
            """;

        Assert.Equal("Hello, Plain|Quack, Custom", Run(source));
    }

    [Fact]
    public void RefReturningMembers_AliasTheOriginalStorage()
    {
        const string source = """
            using IfItQuacks;

            public interface ISlots
            {
                ref int First();
                ref readonly int Last { get; }
                ref int this[int index] { get; }
            }

            public class Slots
            {
                private readonly int[] _values = [1, 2, 3];
                public ref int First() => ref _values[0];
                public ref int Last => ref _values[2];
                public ref int this[int index] => ref _values[index];
                public int Sum => _values[0] + _values[1] + _values[2];
            }

            public static class Entry
            {
                public static string Run()
                {
                    var slots = new Slots();
                    var view = Duck.As<ISlots>(slots);
                    view.First() = 10;
                    view[1] = 20;
                    return view.Last + "|" + slots.Sum;
                }
            }
            """;

        Assert.Equal("3|33", Run(source));
    }

    [Fact]
    public void RefReturningShapeMember_WithByValueCandidate_ReportsIfItQuacks001()
    {
        const string source = """
            using IfItQuacks;

            public interface ISlot { ref int Get(); }

            public class Slot { public int Get() => 1; }

            public static class Entry
            {
                public static ISlot Run() => Duck.As<ISlot>(new Slot());
            }
            """;

        Assert.Contains("IFITQUACKS001", GeneratorTestHelper.GetDiagnosticIds(source));
    }

    [Theory]
    [InlineData("U Map<U>();")]
    [InlineData("static abstract IDoable Create();")]
    public void UnsupportedShapeMember_ReportsIfItQuacks005(string member)
    {
        var source = $$"""
            using IfItQuacks;

            public interface IDoable { void Do(); {{member}} }

            public class A { public void Do() { } }

            public static partial class Ops
            {
                [DuckTyped]
                public static void Foo(IDoable a) => a.Do();
            }

            public static class Entry
            {
                public static void Run() => Ops.Foo(new A());
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "IFITQUACKS005");
    }

    private static object? Run(string source)
    {
        var (compilation, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var assembly = GeneratorTestHelper.EmitAndLoad(compilation);
        return assembly.GetType("Entry")!.GetMethod("Run")!.Invoke(null, null);
    }
}
