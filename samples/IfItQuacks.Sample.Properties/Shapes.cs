namespace IfItQuacks.Sample.Properties;

[DuckShape]
public interface INameable
{
    string Name { get; set; }
}

public class Person
{
    public string Name { get; set; } = "";
}

public static partial class Ops
{
    [DuckTyped]
    public static void Greet(INameable n) => Console.WriteLine($"Hello, {n.Name}!");
}
