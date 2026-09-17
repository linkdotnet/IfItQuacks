namespace IfItQuacks.Sample.Signatures;

public interface INamed
{
    string Name { get; }
}

public class Person
{
    public string Name => "Steven";
}

public class Mallard
{
    public string Name => "Donald";
}

public class Rock
{
    public string Kind => "granite";
}

/// <summary>A <c>[DuckTyped]</c> method on a struct: the receiver is passed by reference, so mutations stick.</summary>
public partial record struct Counter
{
    public int Count;

    [DuckTyped]
    public string Add(INamed named) => $"{named.Name} is number {++Count}";
}

public static partial class Ops
{
    // ref, out and params parameters are passed through unchanged.
    [DuckTyped]
    public static bool TryDescribe(INamed named, ref int calls, out string description, params string[] suffixes)
    {
        calls++;
        description = named.Name + string.Concat(suffixes);
        return true;
    }

    // private and protected methods work too; they are called through a generated internal forwarder.
    [DuckTyped]
    private static string Shout(INamed named) => named.Name.ToUpperInvariant();

    public static string ShoutAll(Person person, Mallard mallard) => $"{Shout(person)} & {Shout(mallard)}";

    // Calls from generic code can't be checked at compile time and use the runtime fallback.
    public static string DescribeAtRuntime<T>(T value) => Describe(value!);

    [DuckTyped]
    public static string Describe(INamed named) => $"This is {named.Name}";
}

public partial class Greeter
{
    [DuckTyped]
    public string Greet(INamed named) => $"Hello, {named.Name}!";

    // An instance method can also be called through the implicit this.
    public string GreetMallard() => Greet(new Mallard());
}
