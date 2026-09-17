namespace IfItQuacks.Sample.Generics;

public interface IContainer<out T>
{
    T Get();
}

public class IntBox(int value)
{
    public int Get() => value;
}

public class Box<T>(T value)
{
    public T Get() => value;
}

public static partial class Ops
{
    [DuckTyped]
    public static T Unwrap<T>(IContainer<T> c) => c.Get();
}
