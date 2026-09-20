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
    private Employee[] _employees = [];
    private Mallard[] _mallards = [];

    [GlobalSetup]
    public void Setup()
    {
        _ducks = [.. Enumerable.Range(0, Count).Select(i => new Person { Name = "Steven" + i, Age = i })];
        _implementations = [.. Enumerable.Range(0, Count).Select(i => new RealPerson { Name = "Steven" + i, Age = i })];
        _products = [.. Enumerable.Range(0, Count).Select(i => new Product("Rubber duck" + i, i))];
        _employees = [.. Enumerable.Range(0, Count).Select(i => new Employee { Name = "Steven" + i, Age = i })];
        _mallards = [.. Enumerable.Range(0, Count).Select(i => new Mallard { Name = "Donald" + i, Age = i })];
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

    [Benchmark(Description = "[DuckTyped] constrained, class argument")]
    public int DuckTypedConstrainedClass()
    {
        var sum = 0;
        foreach (var person in _ducks)
            sum += Ops.DescribeConstrained(person);
        return sum;
    }

    [Benchmark(Description = "[DuckTyped] constrained, readonly struct argument")]
    public int DuckTypedConstrainedReadonlyStruct()
    {
        var sum = 0;
        foreach (var product in _products)
            sum += Ops.DescribeProductConstrained(product);
        return sum;
    }

    /// <summary>Three shapes reach one interface parameter, so the JIT cannot devirtualize the calls inside it.</summary>
    [Benchmark(Description = "[DuckTyped] polymorphic, interface parameter")]
    public int DuckTypedPolymorphicInterface()
    {
        var sum = 0;
        for (var i = 0; i < Count; i++)
        {
            sum += Ops.Describe(_ducks[i]);
            sum += Ops.Describe(_employees[i]);
            sum += Ops.Describe(_mallards[i]);
        }

        return sum;
    }

    /// <summary>The same three shapes, each specialized into its own instantiation.</summary>
    [Benchmark(Description = "[DuckTyped] polymorphic, constrained")]
    public int DuckTypedPolymorphicConstrained()
    {
        var sum = 0;
        for (var i = 0; i < Count; i++)
        {
            sum += Ops.DescribeConstrained(_ducks[i]);
            sum += Ops.DescribeConstrained(_employees[i]);
            sum += Ops.DescribeConstrained(_mallards[i]);
        }

        return sum;
    }
}
