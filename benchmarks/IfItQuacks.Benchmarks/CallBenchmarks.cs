using BenchmarkDotNet.Attributes;

namespace IfItQuacks.Benchmarks;

/// <summary>
/// What calling a <c>[DuckTyped]</c> method costs compared with the same call without IfItQuacks.
/// Every benchmark walks the same 1000 items, so the numbers are per 1000 calls.
/// </summary>
[MemoryDiagnoser]
public class CallBenchmarks
{
    private const int Count = 1000;

    private Person[] _ducks = [];
    private RealPerson[] _implementations = [];
    private Product[] _products = [];

    [GlobalSetup]
    public void Setup()
    {
        _ducks = [.. Enumerable.Range(0, Count).Select(i => new Person { Name = "Steven" + i, Age = i })];
        _implementations = [.. Enumerable.Range(0, Count).Select(i => new RealPerson { Name = "Steven" + i, Age = i })];
        _products = [.. Enumerable.Range(0, Count).Select(i => new Product("Rubber duck" + i, i))];
    }

    [Benchmark(Baseline = true, Description = "Concrete parameter (no interface)")]
    public int ConcreteParameter()
    {
        var sum = 0;
        foreach (var person in _ducks)
            sum += Ops.DescribeConcrete(person);
        return sum;
    }

    [Benchmark(Description = "Interface parameter, hand-written implementation")]
    public int InterfaceParameter()
    {
        var sum = 0;
        foreach (var person in _implementations)
            sum += Ops.DescribeInterface(person);
        return sum;
    }

    [Benchmark(Description = "[DuckTyped], class argument")]
    public int DuckTypedClass()
    {
        var sum = 0;
        foreach (var person in _ducks)
            sum += Ops.Describe(person);
        return sum;
    }

    [Benchmark(Description = "[DuckTyped], argument implements the interface")]
    public int DuckTypedImplementation()
    {
        var sum = 0;
        foreach (var person in _implementations)
            sum += Ops.Describe(person);
        return sum;
    }

    [Benchmark(Description = "[DuckTyped], readonly struct argument")]
    public int DuckTypedReadonlyStruct()
    {
        var sum = 0;
        foreach (var product in _products)
            sum += Ops.DescribeProduct(product);
        return sum;
    }
}
