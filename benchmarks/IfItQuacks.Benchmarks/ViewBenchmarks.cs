using BenchmarkDotNet.Attributes;

namespace IfItQuacks.Benchmarks;

/// <summary>
/// <c>Duck.As</c> used as a "mapper": a view onto an entity, compared with copying into a DTO.
/// Every benchmark walks the same 1000 entities, so the numbers are per 1000 items.
/// </summary>
[MemoryDiagnoser]
public class ViewBenchmarks
{
    private const int Count = 1000;

    private Customer[] _customers = [];
    private ICustomerView[] _views = [];
    private ICustomerView[] _dtos = [];
    private readonly ICustomerView[] _sink = new ICustomerView[Count];

    [GlobalSetup]
    public void Setup()
    {
        _customers = [.. Enumerable.Range(0, Count).Select(i => new Customer
        {
            Id = i,
            Name = "Steven" + i,
            Email = $"steven{i}@example.com",
            InternalNotes = "pays late",
        })];

        // A lambda, not a method group: method groups aren't intercepted and would hit the runtime fallback.
        _views = [.. _customers.Select(c => Duck.As<ICustomerView>(c))];
        _dtos = [.. _customers.Select(c => (ICustomerView)new CustomerDto(c.Id, c.Name, c.Email))];
    }

    [Benchmark(Baseline = true, Description = "Read the entity directly")]
    public int Entity()
    {
        var sum = 0;
        foreach (var customer in _customers)
            sum += customer.Id + customer.Name.Length + customer.Email.Length;
        return sum;
    }

    [Benchmark(Description = "Duck.As, then read")]
    public int DuckAsThenRead()
    {
        var sum = 0;
        foreach (var customer in _customers)
            sum += Read(Duck.As<ICustomerView>(customer));
        return sum;
    }

    [Benchmark(Description = "Copy into a DTO, then read")]
    public int CopyThenRead()
    {
        var sum = 0;
        foreach (var customer in _customers)
            sum += Read(new CustomerDto(customer.Id, customer.Name, customer.Email));
        return sum;
    }

    [Benchmark(Description = "Read through existing views")]
    public int ExistingViews()
    {
        var sum = 0;
        foreach (var view in _views)
            sum += Read(view);
        return sum;
    }

    [Benchmark(Description = "Read through existing DTOs")]
    public int ExistingDtos()
    {
        var sum = 0;
        foreach (var dto in _dtos)
            sum += Read(dto);
        return sum;
    }

    // Storing the result makes it escape, so the JIT can't keep the adapter on the stack.
    [Benchmark(Description = "Duck.As, kept (escapes)")]
    public ICustomerView[] DuckAsEscaping()
    {
        for (var i = 0; i < _customers.Length; i++)
            _sink[i] = Duck.As<ICustomerView>(_customers[i]);
        return _sink;
    }

    [Benchmark(Description = "Copy into DTOs, kept (escapes)")]
    public ICustomerView[] CopyEscaping()
    {
        for (var i = 0; i < _customers.Length; i++)
        {
            var customer = _customers[i];
            _sink[i] = new CustomerDto(customer.Id, customer.Name, customer.Email);
        }
        return _sink;
    }

    private static int Read(ICustomerView view) => view.Id + view.Name.Length + view.Email.Length;
}
