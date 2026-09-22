---
uid: getting_started
---

# Getting started

## Installation

```bash
dotnet add package IfItQuacks
```

IfItQuacks is a source generator built on top of [interceptors](https://github.com/dotnet/roslyn/blob/main/docs/features/interceptors.md). Interceptors are opt-in per namespace; the package enables its generated namespace automatically, so no project file changes are needed.

### Requirements

Visual Studio 2022 17.13 or the .NET SDK 9.0.200 or later (Roslyn 4.13), the first versions with stable interceptors. An older compiler skips the generator with warning CS9057, so neither `[DuckTyped]` nor `Duck` exists and the project fails to compile.

## Building blocks

The generator adds the following `internal` types to the `IfItQuacks` namespace of your project. Because they are internal, several projects in the same solution can use IfItQuacks without type conflicts:

| Type | Purpose |
|---|---|
| `DuckTypedAttribute` | Marks a method whose interface parameters accept any type that structurally matches the interface. |
| `Duck` | `Duck.As<TShape>(value)` converts a value to an interface it structurally satisfies; `Duck.Stub<TShape>(value)` fills the rest with members that throw; `Duck.Merge<TShape>(first, second)` takes each member from the value implementing its interface, else the first value providing it; `Duck.To<TTarget>(value)` copies into a new value; `Duck.Unwrap(value)` returns the original instance behind an adapter. |
| `DuckTypeMismatchException` | Thrown at runtime if a call couldn't be verified at compile time and the value doesn't implement the interface. |
| `DuckStubException` | Thrown when a member of a `Duck.Stub` that nothing implements is used. |

## Matching an interface

Any interface works - your own, the framework's (`IDisposable`, `IEnumerable<T>`, ...) or one from another library. No attribute is needed. A type matches if it provides the following members of the interface (including those of base interfaces):

| Member | Matched against |
|---|---|
| Methods | A public instance method with the same name and number of parameters, or a public property or field of a delegate type with a matching signature (see [Members holding a delegate](#members-holding-a-delegate)). Its return type has to be assignable to the interface's (a `void` interface method accepts any return type), and the interface's parameter types have to be assignable to its parameters. `ref`/`out`/`in` parameters must match exactly. |
| Properties | A public instance property or field with the same name, and a public getter/setter where the interface declares one (a field needs to be non-`readonly` for a setter). A getter's type has to be assignable to the interface's type, a setter's the other way round - with both, the types have to convert in both directions. |
| Indexers | A public indexer with the same accessors, assignable parameter types and a type following the property rules. |
| Events | A public event with the same name and delegate type. |
| Default interface members | Optional. If the type has a matching member, it is used; otherwise the default implementation runs. |
| `static abstract` members and operators | Only for [duck-typed constraints](#static-members-and-operators): a public static member or operator with a matching signature. |

"Assignable" means an implicit identity, reference, boxing, numeric or nullable conversion - just like in an assignment - but no user-defined conversion operators. If several members match, one with exactly the same types wins:

```csharp
public interface IInventory
{
    IEnumerable<string> Items { get; }
    long Count();
    void Add(string item);
}

public class Warehouse
{
    public List<string> Items { get; } = [];     // List<string> -> IEnumerable<string>
    public int Count() => Items.Count;           // int -> long
    public bool Add(object item) { /* ... */ }   // string -> object, result is discarded
}
```

Types that already implement the interface - directly, explicitly or through variance - are passed through without an adapter. For all others, generic methods (`U Map<U>()`) and `static abstract` members can't be adapted and are reported with [`IFITQUACKS005`](diagnostics.md#ifitquacks005).

The rest of this page uses one interface throughout:

```csharp
public interface INameable
{
    string Name { get; set; }
}
```

## Declaring a duck-typed method

A `[DuckTyped]` method lives in a `partial` type and has at least one interface parameter. It can be static or an instance method and take any number of other parameters:

```csharp
public static partial class Greeter
{
    [DuckTyped]
    public static void Greet(INameable nameable) => Console.WriteLine($"Hello, {nameable.Name}!");
}

public partial class Party
{
    [DuckTyped]
    public void Introduce(INameable host, INameable guest, string greeting = "Hello") =>
        Console.WriteLine($"{greeting}, {guest.Name}! I'm {host.Name}.");
}
```

Every interface parameter passed by value is duck-typed; all other parameters behave as usual, including `ref`/`out`, `params`, default values and named arguments. See [Known limitations](known_limitations.md) for the few signatures that aren't supported.

Interface parameters that don't need duck typing keep working as before. An implementation, a variable typed as the interface, `null`, `default` or an omitted default value are all accepted:

```csharp
public static partial class Greeter
{
    [DuckTyped]
    public static void Greet(INameable nameable, ILogger? logger = null) { /* ... */ }
}

Greeter.Greet(new Person(), consoleLogger);    // real ILogger implementation
Greeter.Greet(new Person(), new FakeLogger()); // a duck
Greeter.Greet(new Person(), null);
Greeter.Greet(new Person());
```

## Calling it

Any type exposing the interface's members publicly can be passed in:

```csharp
public class Person
{
    public string Name { get; set; } = "";
}

Greeter.Greet(new Person { Name = "Steven" }); // Hello, Steven!
```

`Person` does not implement `INameable`. If it didn't have a public `Name` property with a getter and setter, the build fails with [`IFITQUACKS001`](diagnostics.md#ifitquacks001).

Types that already implement the interface are passed through unchanged. Instance methods work the same way, on an explicit receiver or through the implicit `this`:

```csharp
var party = new Party();
party.Introduce(new Person { Name = "Steven" }, new Pet { Name = "Donald" }, greeting: "Quack");
```

## Extension methods

A `[DuckTyped]` method can be an extension method, which lets the receiver be duck-typed as well:

```csharp
public static partial class Ops
{
    [DuckTyped]
    public static string Greet(this INameable nameable, string greeting = "Hello") => $"{greeting}, {nameable.Name}!";
}

new Person { Name = "Steven" }.Greet();          // Hello, Steven!
new { Name = "Donald" }.Greet("Quack");          // Quack, Donald!
Ops.Greet(new Person { Name = "Steven" }, "Hi"); // calling it as a static method works too
```

This is the one mode that changes what the rest of your code sees: to make `person.Greet()` compile, the generated fallback is an extension method with an unconstrained type parameter, so `Greet` appears on **every** type in scope. A receiver that doesn't fit reports [`IFITQUACKS001`](diagnostics.md#ifitquacks001) instead of `CS1061`.

## Generic interfaces

Interfaces can be generic, and `[DuckTyped]` methods can be generic too. The type arguments are inferred from the argument's members:

```csharp
public interface IContainer<T>
{
    T Get();
}

public class IntBox { public int Get() => 42; }
public class Box<T>(T value) { public T Get() => value; }

public static partial class Ops
{
    [DuckTyped]
    public static T Unwrap<T>(IContainer<T> container) => container.Get();
}

int number = Ops.Unwrap(new IntBox());             // T = int
string text = Ops.Unwrap(new Box<string>("quack")); // T = string
```

Every type parameter of the method has to appear in an interface parameter type, otherwise it can't be inferred ([`IFITQUACKS004`](diagnostics.md#ifitquacks004)). Other parameters may use the inferred type parameters, e.g. `T Add<T>(IContainer<T> container, T fallback)`. Closed generic interfaces like `IContainer<int>` work on non-generic methods as well.

## Delegates and method groups

An interface with a single method is satisfied by any matching delegate, lambda or method group - a functional interface, as in TypeScript or Java:

```csharp
public interface IFormatter { string Format(int value); }

public static partial class Ops
{
    [DuckTyped]
    public static string Render(IFormatter formatter, int value) => formatter.Format(value);
}

Func<int, string> hex = value => $"0x{value:X}";
Ops.Render(hex, 255);                          // a delegate variable
Ops.Render((int value) => $"{value} EUR", 42);  // a lambda: parameter types have to be explicit
Ops.Render(Spell, 3);                          // a method group
IFormatter formatter = Duck.As<IFormatter>(hex);
```

The delegate's signature has to match the interface's single method by the usual rules (assignable return and parameter types). Interfaces with more than one required member can't be satisfied by a delegate ([`IFITQUACKS001`](diagnostics.md#ifitquacks001)).

It also works the other way round: a `[DuckTyped]` method converts to a delegate over the duck type, so it can be passed around as a function:

```csharp
Func<Person, string> describe = Ops.Describe;   // Describe takes an INamed
people.Select(Ops.Describe);
```

### Members holding a delegate

A member whose type is a delegate stands in for the interface method of the same name. That turns an object literal into a test double:

```csharp
public interface IRepository
{
    Order? Find(int id);
    void Save(Order order);
}

var repository = Duck.As<IRepository>(new
{
    Find = (Func<int, Order?>)(id => new Order(id, "Rubber duck")),
    Save = (Action<Order>)(_ => { }),
});
```

A lambda can't be assigned to an anonymous type property directly (`CS0828`), so its delegate type has to be written out.

## Stubbing and merging

`Duck.Stub<TShape>(value)` is `Duck.As` for tests: members the value provides are forwarded, everything else throws `DuckStubException` when it is used. `Duck.Stub<TShape>()` stubs the whole interface.

```csharp
var repository = Duck.Stub<IRepository>(new { Find = (Func<int, Order?>)(id => new Order(id, "Stubbed")) });

repository.Find(7);                   // the lambda
repository.Save(new Order(2, "..."));  // throws DuckStubException
```

A member that *is* there but doesn't fit is still reported with [`IFITQUACKS001`](diagnostics.md#ifitquacks001) - a stub fills in what is missing, not what is wrong.

`Duck.Merge<TShape>(first, second)` (and a three-value overload) takes every member from the first value that provides it, which replaces a single member of a real object. A value that implements the interface declaring a member takes precedence, so interfaces sharing a member name each get their own implementer:

```csharp
Order? saved = null;
var spy = Duck.Merge<IRepository>(new { Save = (Action<Order>)(order => saved = order) }, realRepository);

spy.Save(order);   // the lambda
spy.Find(1);       // the real repository
```

`Duck.Unwrap` returns the first value. Equality covers every value, so merges of anonymous objects behave like records:

```csharp
var a = Duck.Merge<IMyObj>(new { Value = 1 }, new { Test = 2 });

a.Equals(Duck.Merge<IMyObj>(new { Value = 1 }, new { Test = 2 }));   // true
a.Equals(Duck.Merge<IMyObj>(new { Value = 1 }, new { Test = 3 }));   // false
```

A member none of the values provides is reported with [`IFITQUACKS001`](diagnostics.md#ifitquacks001).

## Sequences

An `IEnumerable<Person>` is not an `IEnumerable<INameable>`, even when every `Person` fits. Passing one adapts the sequence element by element, lazily:

```csharp
public static partial class Ops
{
    [DuckTyped]
    public static string Join(IEnumerable<INameable> people) => string.Join(", ", people.Select(p => p.Name));
}

Ops.Join(new List<Person> { new() { Name = "Steven" } });
Ops.Join(new[] { new Pet() });

IReadOnlyList<INameable> view = Duck.As<IReadOnlyList<INameable>>(people); // Count and the indexer included
```

Only `IEnumerable<T>`, `IReadOnlyCollection<T>` and `IReadOnlyList<T>` qualify: they use their element type in output position only. `ICollection<T>` and `IList<T>` would need the adaptation to run backwards, so they are not supported.

## Static members and operators

Generic math is built on `static abstract` interface members, which no instance can provide. They are duck-typed through the **constraint** instead of a parameter: mark a generic method whose type parameter is constrained to the interface and use it as a normal parameter type.

```csharp
public interface IAddable<T> where T : IAddable<T>
{
    static abstract T Zero { get; }
    static abstract T operator +(T left, T right);
}

public static partial class Ops
{
    [DuckTyped]
    public static T Sum<T>(T first, T second) where T : IAddable<T> => T.Zero + first + second;
}

// Money has + and Zero, but doesn't implement IAddable<Money>.
Money total = Ops.Sum(new Money(19.99m), new Money(5.01m));
```

The generated adapter is its own type argument (`Adapter : IAddable<Adapter>`) and forwards the interface's statics and operators to the argument's type, so BCL interfaces work as well:

```csharp
[DuckTyped]
public static T Add<T>(T first, T second) where T : System.Numerics.IAdditionOperators<T, T, T> => first + second;
```

Instance members of the constraint are forwarded too, as long as their signature doesn't use the self type. Every type parameter needs exactly one interface constraint used by a by-value parameter, and all arguments bound to the same type parameter must have the same type ([`IFITQUACKS004`](diagnostics.md#ifitquacks004)).

## Duck typing without allocations

A constraint isn't only for `static abstract` members. Any duck type can be written that way, and doing so is how you get a duck-typed call that allocates nothing:

```csharp
public static partial class Ops
{
    [DuckTyped]
    public static string Greet<T>(T named) where T : INameable => $"Hello, {named.Name}!";
}

Ops.Greet(new Person());   // same call, no adapter is boxed
```

With an interface parameter the adapter is boxed as the interface; with a constraint it is passed as the *type argument*, so the runtime specializes the method per shape and the forwarders inline. Measured, that is the difference between `3,694 ns` plus 24 bytes per call and `562 ns` with nothing allocated.

Constrained parameters can sit next to ordinary interface parameters, work as extension methods, and accept several independent type parameters. They can't take anonymous types, because the generated overload has to name the argument's type. See [Constrained duck typing](constrained_duck_typing.md) for the full picture.

## Converting explicitly

Use `Duck.As<TShape>(value)` when the duck-typed value has to outlive a single call - for example to store it in a field, add it to a collection or return it:

```csharp
List<INameable> nameables = [Duck.As<INameable>(new Person()), Duck.As<INameable>(new Pet())];
```

The same compile-time checks apply as for `[DuckTyped]` methods. The type argument must be an interface ([`IFITQUACKS007`](diagnostics.md#ifitquacks007)). That includes framework interfaces, so anything with a `Dispose()` method works with `using`:

```csharp
using (Duck.As<IDisposable>(new TemporaryFile("quack.tmp")))
{
    // ...
}
```

## Anonymous types

Anonymous types can be passed to `[DuckTyped]` methods and `Duck.As` like any other type - handy for tests and quick stubs, much like object literals in TypeScript:

```csharp
Greeter.Greet(new { Name = "Steven" });

IPerson stub = Duck.As<IPerson>(new { Name = "Donald", Age = 90, Hometown = "Duckburg" });
```

Anonymous type properties are read-only, so interfaces with setters, methods, indexers or events can't be satisfied. Nested anonymous types are supported; a property whose type only contains an anonymous type (e.g. `new[] { new { A = 1 } }`) and generic `[DuckTyped]` methods are not ([`IFITQUACKS001`](diagnostics.md#ifitquacks001)).

## Identity

Adapters forward `Equals`, `GetHashCode` and `ToString` to the original instance, so two adapters of the same object are equal and collapse in a `HashSet` or as dictionary keys. `Duck.Unwrap` gives you the original instance back:

```csharp
var person = new Person();
INameable a = Duck.As<INameable>(person), b = Duck.As<INameable>(person);

a.Equals(b);                        // true
new HashSet<INameable> { a, b }.Count; // 1
Duck.Unwrap(a) is Person;           // true
ReferenceEquals(a, b);              // false - every conversion creates a new adapter
```

### A view, not a mapper

`Duck.As` doesn't copy anything. The returned object forwards to the original instance:

```csharp
var customer = new Customer { Name = "Steven", Email = "steven@example.com" };
var view = Duck.As<ICustomerView>(customer); // ICustomerView { string Name { get; } string Email { get; } }

customer.Email = "quack@example.com";
Console.WriteLine(view.Email); // quack@example.com
```

That makes it a cheap way to expose a narrower view of a type, e.g. hiding members of an entity behind a get-only interface.

### Copying with `Duck.To`

When you need a value of its own instead of a view, `Duck.To<TTarget>(value)` builds one. The target is a concrete type, not an interface, and the call is replaced by an object creation - nothing forwards afterwards:

```csharp
public record CustomerDto(string Name, string Email);

var dto = Duck.To<CustomerDto>(customer);  // new CustomerDto(customer.Name, customer.Email)
customer.Email = "quack@example.com";
Console.WriteLine(dto.Email);              // steven@example.com - the copy is its own value
```

The target is filled through the constructor taking the most parameters the source can fill, and every remaining settable property or field is set from the member of the same name. Anything the source can't fill is reported with [`IFITQUACKS009`](diagnostics.md#ifitquacks009).

`Duck.To` is not a mapping library either: name for name, assignable types, no renaming, no nested projection and no user-defined conversions - the same rules as everywhere else.

Runnable examples live in [`samples`](https://github.com/linkdotnet/IfItQuacks/tree/main/samples), one project per showcase:

| Project | Shows |
|---|---|
| `IfItQuacks.Sample.Methods` | Static and instance `[DuckTyped]` methods with several parameters, including an optional interface parameter |
| `IfItQuacks.Sample.Properties` | Read-write properties, satisfied by properties and fields |
| `IfItQuacks.Sample.Assignability` | Compatible instead of identical member types, fields as properties |
| `IfItQuacks.Sample.AnonymousTypes` | Anonymous types for `[DuckTyped]` methods and `Duck.As` |
| `IfItQuacks.Sample.Generics` | Generic interfaces and generic `[DuckTyped]` methods |
| `IfItQuacks.Sample.FrameworkInterfaces` | `IEnumerable<T>` parameters and `Duck.As<IDisposable>` with `using` |
| `IfItQuacks.Sample.Conversion` | `Duck.As` for collections and read-only views, identity and `Duck.Unwrap` |
| `IfItQuacks.Sample.Members` | Events, indexers, `ref` returns and default interface members |
| `IfItQuacks.Sample.Signatures` | `ref`/`out`/`params` parameters, `private` methods, methods on a struct and the runtime fallback |
| `IfItQuacks.Sample.Delegates` | Delegates, lambdas and method groups as ducks, and `[DuckTyped]` methods as delegates |
| `IfItQuacks.Sample.GenericMath` | `static abstract` members and operators through duck-typed constraints |
| `IfItQuacks.Sample.Extensions` | `[DuckTyped]` extension methods, including a duck-typed receiver |
| `IfItQuacks.Sample.Sequences` | `IEnumerable<T>` and `IReadOnlyList<T>` parameters adapted element by element |
| `IfItQuacks.Sample.Testing` | Delegate members, `Duck.Stub` and `Duck.Merge` as test doubles |
| `IfItQuacks.Sample.Copying` | `Duck.To` for records and settable types |
