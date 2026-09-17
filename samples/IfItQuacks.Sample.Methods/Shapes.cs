namespace IfItQuacks.Sample.Methods;

[DuckShape]
public interface IDoable
{
    void Do();
}

public class A
{
    public void Do() => Console.WriteLine("A.Do");
}

public class B
{
    public void Do() => Console.WriteLine("B.Do");
}

public static partial class Ops
{
    [DuckTyped]
    public static void Foo(IDoable a) => a.Do();
}
