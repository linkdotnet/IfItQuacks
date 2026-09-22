<p align="center">
  <img src="https://raw.githubusercontent.com/linkdotnet/IfItQuacks/main/assets/showcase.webp" alt="IfItQuacks: any class and anonymous types satisfy an interface, mismatches fail the build" />
</p>

# IfItQuacks

[![Nuget](https://img.shields.io/nuget/dt/IfItQuacks?style=flat-square)](https://www.nuget.org/packages/IfItQuacks/)
[![GitHub tag](https://img.shields.io/github/v/tag/linkdotnet/IfItQuacks?include_prereleases&logo=github&style=flat-square)](https://github.com/linkdotnet/IfItQuacks/releases)

Compile-time checked structural (duck) typing for C#: "If it walks like a duck and quacks like a duck, it's a duck."

## Getting Started

> dotnet add package IfItQuacks

Requires Visual Studio 2022 17.13 or the .NET SDK 9.0.200 or later.

Declare an interface, mark a method as duck-typed and pass in anything that fits - no attributes on the interface or the types:

```csharp
using IfItQuacks;

public interface INamed
{
    string Name { get; }
}

public class Person { public string Name => "Steven"; }
public class Mallard { public string Name => "Donald"; }

public partial class Greeter
{
    [DuckTyped]
    public string Greet(INamed first, INamed second, string greeting = "Hello") =>
        $"{greeting}, {first.Name} and {second.Name}!";
}

new Greeter().Greet(new Person(), new Mallard()); // Hello, Steven and Donald!
```

Neither `Person` nor `Mallard` implements `INamed`. The generator verifies at compile time that both have a matching `Name` and redirects the call through small generated adapters - no reflection, no `dynamic`. A type that doesn't fit is a build error.

To keep a duck-typed value around, convert it explicitly:

```csharp
List<INamed> names = [Duck.As<INamed>(new Person()), Duck.As<INamed>(new Mallard())];
```

## Highlights

### Anonymous types

Like object literals in TypeScript, anonymous types are ducks too - perfect for tests and quick stubs:

```csharp
new Greeter().Greet(new { Name = "Steven" }, new Mallard());

IPerson stub = Duck.As<IPerson>(new { Name = "Donald", Age = 90 });
```

### Compatible, not identical

Members only have to fit, just like an assignment would:

```csharp
public interface IInventory
{
    IEnumerable<string> Items { get; }
    long Count();
    void Add(string item);
}

public class Warehouse
{
    public List<string> Items { get; } = [];                  // List<string> -> IEnumerable<string>
    public int Count() => Items.Count;                        // int -> long
    public bool Add(object item) { Items.Add($"{item}"); return true; } // string -> object, result discarded
}
```

### Test doubles without a mocking library

A member holding a delegate satisfies an interface method, `Duck.Stub` throws for everything that is missing, and `Duck.Merge` replaces a single member of a real object:

```csharp
var repository = Duck.Stub<IRepository>(new { Find = (Func<int, Order?>)(id => new Order(id, "Rubber duck")) });

Order? saved = null;
var spy = Duck.Merge<IRepository>(new { Save = (Action<Order>)(order => saved = order) }, realRepository);
```

### Copies, not only views

`Duck.As` returns a view that forwards; `Duck.To` builds a value of its own, verified the same way:

```csharp
public record CustomerDto(string Name, string Email);

var dto = Duck.To<CustomerDto>(customer); // new CustomerDto(customer.Name, customer.Email)
```

### Types derived from types

`Pick`, `Omit`, `Partial`, `Readonly` and intersections, like TypeScript's mapped types. A derived
interface would be dead weight in C# - under structural typing every fitting type satisfies it:

```csharp
[DuckShape<Customer>(Omit = [nameof(Customer.PasswordHash)], Readonly = true)]
public partial interface ICustomerView;   // int Id { get; } string Name { get; } string Email { get; }

Render(new Customer());                                // the entity
Render(new CustomerRow(2, "Donald", "d@example.com")); // a DTO
Render(new { Id = 3, Name = "Daisy", Email = "" });    // an object literal
```

See [Mapped shapes](https://linkdotnet.github.io/IfItQuacks/articles/mapped_shapes.html).

### Zero-allocation duck typing

Write the duck type as a constrained type parameter and the adapter is passed as a *type argument*
instead of an interface - nothing is boxed, and the runtime specializes the method per shape:

```csharp
[DuckTyped]
public static int Describe<T>(T person) where T : IPerson => person.Name.Length + person.Age;

Ops.Describe(new Person());   // same call, 0 bytes allocated
```

Measured per 1000 calls: `3,694 ns` and 24,000 B as an interface parameter, `562 ns` and nothing as a
constraint - the same as putting the concrete type in the signature. With three shapes sharing one
method it is `10,459 ns` against `3,005 ns`, because a constrained call has no virtual dispatch left to
guess. See [Constrained duck typing](https://linkdotnet.github.io/IfItQuacks/articles/constrained_duck_typing.html).

### Fields count as properties

```csharp
public class LegacyPerson { public string Name = ""; }

new Greeter().Greet(new LegacyPerson { Name = "Steven" }, new Mallard());
```

### Identity is preserved

Adapters forward `Equals`, `GetHashCode` and `ToString` to the original instance, so they work as dictionary keys and in sets. `Duck.Unwrap` gives you the original back:

```csharp
var person = new Person();
Duck.As<INamed>(person).Equals(Duck.As<INamed>(person)); // true
Duck.Unwrap(Duck.As<INamed>(person)) is Person;          // true
```

### And more

- Any interface works, including framework ones: `Duck.As<IDisposable>(x)`, `IEnumerable<T>` parameters, ...
- Extension methods: `[DuckTyped] static string Greet(this INamed n)` makes `person.Greet()` work
- Sequences are adapted element by element, so a `List<Person>` is an `IEnumerable<INamed>`
- Interface parameters that don't need duck typing (an `ILogger`, say) keep accepting implementations, `null` and default values
- Generic interfaces (`IContainer<T>`) and generic `[DuckTyped]` methods with inferred type arguments
- Events, indexers and default interface members
- Delegates, lambdas and method groups for single-method interfaces - and `[DuckTyped]` methods as delegates
- `static abstract` members and operators through duck-typed constraints (`where T : IAddable<T>`), generic math included
- Instance and static methods, `ref`/`out`, `params`, default values and named arguments
- Zero setup: install the package, no project file changes
- Cheap at build time: ~0.4 ms per duck-typed call site, nearly nothing when unused ([Benchmarks](https://linkdotnet.github.io/IfItQuacks/articles/benchmarks.html#compile-time-cost))

## Documentation

- [Getting started](https://linkdotnet.github.io/IfItQuacks/articles/getting_started.html)
- [How does it work?](https://linkdotnet.github.io/IfItQuacks/articles/concepts.html) - including what gets allocated
- [Diagnostics](https://linkdotnet.github.io/IfItQuacks/articles/diagnostics.html)
- [Benchmarks](https://linkdotnet.github.io/IfItQuacks/articles/benchmarks.html)
- [Mapped shapes](https://linkdotnet.github.io/IfItQuacks/articles/mapped_shapes.html) - Pick, Omit, Partial, Readonly
- [Constrained duck typing](https://linkdotnet.github.io/IfItQuacks/articles/constrained_duck_typing.html) - the allocation-free form
- [Known limitations](https://linkdotnet.github.io/IfItQuacks/articles/known_limitations.html)

Runnable examples live in [`samples`](https://github.com/linkdotnet/IfItQuacks/tree/main/samples).

## Support & Contributing

Thanks to all [contributors](https://github.com/linkdotnet/IfItQuacks/graphs/contributors) and people that are creating bug-reports and valuable input:

<a href="https://github.com/linkdotnet/IfItQuacks/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=linkdotnet/IfItQuacks" alt="Supporters" />
</a>
