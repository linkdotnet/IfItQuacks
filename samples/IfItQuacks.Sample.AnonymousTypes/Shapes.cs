namespace IfItQuacks.Sample.AnonymousTypes;

[DuckShape]
public interface IPerson
{
    string Name { get; }
    int Age { get; }
}

public static partial class Printer
{
    [DuckTyped]
    public static void Print(IPerson person) => Console.WriteLine($"{person.Name} ({person.Age})");
}
