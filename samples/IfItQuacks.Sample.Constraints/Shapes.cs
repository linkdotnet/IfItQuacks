namespace IfItQuacks.Sample.Constraints;

public interface INamed
{
    string Name { get; }
}

public interface IPriced
{
    decimal Price { get; }
}

public interface ILog
{
    void Write(string message);
}

/// <summary>Neither type implements <see cref="INamed"/>.</summary>
public sealed class Person
{
    public string Name => "Steven";
}

public sealed class Mallard
{
    public string Name => "Donald";
}

public readonly record struct Product(string Name, decimal Price);

/// <summary>Fits <see cref="ILog"/> without implementing it.</summary>
public sealed class Recorder
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines => _lines;

    public void Write(string message) => _lines.Add(message);
}

public static partial class Ops
{
    /// <summary>The duck type is a constrained type parameter, so the generated adapter is passed as a
    /// type argument instead of an interface: nothing is boxed and the JIT specializes the body per shape.</summary>
    [DuckTyped]
    public static string Greet<T>(T named) where T : INamed => $"Hello, {named.Name}!";

    /// <summary>Several independent constrained parameters each get their own adapter.</summary>
    [DuckTyped]
    public static string Introduce<TFirst, TSecond>(TFirst first, TSecond second)
        where TFirst : INamed
        where TSecond : INamed
        => $"{first.Name} and {second.Name}";

    /// <summary>A constrained parameter can sit next to an ordinary interface parameter.</summary>
    [DuckTyped]
    public static string Label<T>(T priced, ILog log) where T : IPriced
    {
        var label = priced.Price.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        log.Write(label);
        return label;
    }

    /// <summary>Constrained extension methods work too.</summary>
    [DuckTyped]
    public static string Shout<T>(this T named) where T : INamed => named.Name.ToUpperInvariant();
}
