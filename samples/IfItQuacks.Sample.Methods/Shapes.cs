namespace IfItQuacks.Sample.Methods;

public interface IDoable
{
    void Do();
}

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

public interface ILog
{
    void Write(string message);
}

public class ConsoleLog : ILog
{
    public void Write(string message) => Console.WriteLine($"[log] {message}");
}

public class PrefixLog
{
    public void Write(string message) => Console.WriteLine($"[prefix] {message}");
}

public static partial class Ops
{
    [DuckTyped]
    public static void Foo(IDoable a, ILog? log = null)
    {
        log?.Write("Doing it");
        a.Do();
    }
}

public partial class Greeter
{
    [DuckTyped]
    public string Greet(INamed first, INamed second, string greeting = "Hello") =>
        $"{greeting}, {first.Name} and {second.Name}!";
}
