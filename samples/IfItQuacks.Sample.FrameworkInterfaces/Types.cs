namespace IfItQuacks.Sample.FrameworkInterfaces;

public class Countdown(int from)
{
    public IEnumerator<int> GetEnumerator()
    {
        for (var i = from; i > 0; i--)
            yield return i;
    }
}

public class TemporaryFile(string name)
{
    public void Dispose() => Console.WriteLine($"Deleted {name}");
}

public static partial class Report
{
    [DuckTyped]
    public static void Print(IEnumerable<int> numbers) => Console.WriteLine(string.Join(", ", numbers.Select(n => n * 10)));
}
