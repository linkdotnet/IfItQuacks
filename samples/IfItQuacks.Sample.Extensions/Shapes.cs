namespace IfItQuacks.Sample.Extensions;

public interface INamed
{
    string Name { get; }
}

public interface IPriced
{
    decimal Price { get; }
}

public class Person
{
    public string Name => "Steven";
}

public class Mallard
{
    public string Name => "Donald";
}

public record Product(string Title, decimal Price)
{
    public string Name => Title;
}
