namespace IfItQuacks.Benchmarks;

public interface IPerson
{
    string Name { get; }
    int Age { get; }
}

public interface IProduct
{
    string Name { get; }
    decimal Price { get; }
}

public interface ICustomerView
{
    int Id { get; }
    string Name { get; }
    string Email { get; }
}

/// <summary>A type that fits <see cref="IPerson"/> without implementing it.</summary>
public sealed class Person
{
    public string Name { get; init; } = "Steven";
    public int Age { get; init; } = 40;
}

/// <summary>Three distinct types fitting <see cref="IPerson"/>, so one interface call site sees three
/// implementations and the JIT can no longer devirtualize it.</summary>
public sealed class Employee
{
    public string Name { get; init; } = "Steven";
    public int Age { get; init; } = 40;
}

public sealed class Mallard
{
    public string Name { get; init; } = "Donald";
    public int Age { get; init; } = 3;
}

public sealed class RealPerson : IPerson
{
    public string Name { get; init; } = "Steven";
    public int Age { get; init; } = 40;
}

public readonly record struct Product(string Name, decimal Price);

/// <summary>A domain entity with a member no caller outside the domain should see.</summary>
public sealed class Customer
{
    public int Id { get; init; } = 1;
    public string Name { get; init; } = "Steven";
    public string Email { get; init; } = "steven@example.com";
    public string InternalNotes { get; init; } = "pays late";
}

/// <summary>The same three members, copied into a DTO by hand.</summary>
public sealed record CustomerDto(int Id, string Name, string Email) : ICustomerView;
