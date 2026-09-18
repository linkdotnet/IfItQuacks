namespace IfItQuacks.Sample.Sequences;

public interface INamed
{
    string Name { get; }
}

public class Person(string name)
{
    public string Name => name;
}

public class Mallard
{
    public string Name => "Donald";
}

public static partial class Ops
{
    [DuckTyped]
    public static string Join(IEnumerable<INamed> people) => string.Join(", ", people.Select(p => p.Name));

    [DuckTyped]
    public static string Second(IReadOnlyList<INamed> people) => $"{people[1].Name} of {people.Count}";
}
