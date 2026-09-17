namespace IfItQuacks.Sample.Delegates;

public interface IFormatter
{
    string Format(int value);
}

public interface INamed
{
    string Name { get; }
}

public class Person
{
    public string Name => "Steven";
}

public static partial class Ops
{
    // A single-method interface can be satisfied by any matching delegate, lambda or method group.
    [DuckTyped]
    public static string Render(IFormatter formatter, int value) => formatter.Format(value);

    [DuckTyped]
    public static string Describe(INamed named) => $"This is {named.Name}";
}
