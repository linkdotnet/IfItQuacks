namespace IfItQuacks.Sample.Methods;

[DuckShape]
public interface IDoable
{
    void Do();
}

[DuckShape]
public interface INamed
{
    string Name { get; }
}

public class A
{
    public void Do() => Console.WriteLine("A.Do");
}

public class B
{
    public void Do() => Console.WriteLine("B.Do");
}

public class Person
{
    public string Name => "Steven";
}

public class Mallard
{
    public string Name => "Donald";
}

public static partial class Ops
{
    [DuckTyped]
    public static void Foo(IDoable a) => a.Do();
}

public partial class Greeter
{
    [DuckTyped]
    public string Greet(INamed first, INamed second, string greeting = "Hello") =>
        $"{greeting}, {first.Name} and {second.Name}!";
}
