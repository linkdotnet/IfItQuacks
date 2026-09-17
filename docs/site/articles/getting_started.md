---
uid: getting_started
---

# Getting started

## Installation

```bash
dotnet add package IfItQuacks
```

IfItQuacks is a source generator built on top of [interceptors](https://github.com/dotnet/roslyn/blob/main/docs/features/interceptors.md). Interceptors are opt-in per namespace; the package enables its generated namespace automatically, so no project file changes are needed.

Only if you reference the generator as a project (`OutputItemType="Analyzer"`) instead of the NuGet package, add the namespace yourself:

```xml
<PropertyGroup>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);IfItQuacks.Generated</InterceptorsNamespaces>
</PropertyGroup>
```

## Building blocks

The generator adds the following `internal` types to the `IfItQuacks` namespace of your project. Because they are internal, several projects in the same solution can use IfItQuacks without type conflicts:

| Type | Purpose |
|---|---|
| `DuckTypedAttribute` | Marks a method whose interface parameters accept any type that structurally matches the interface. |
| `Duck` | `Duck.As<TShape>(value)` converts a value to an interface it structurally satisfies; `Duck.Unwrap(value)` returns the original instance behind an adapter. |
| `DuckTypeMismatchException` | Thrown at runtime if a call couldn't be verified at compile time and the value doesn't implement the interface. |

## Matching an interface

Any interface works - your own, the framework's (`IDisposable`, `IEnumerable<T>`, ...) or one from another library. No attribute is needed. A type matches if it provides the following members of the interface (including those of base interfaces):

| Member | Matched against |
|---|---|
| Methods | A public instance method with the same name and number of parameters. Its return type has to be assignable to the interface's (a `void` interface method accepts any return type), and the interface's parameter types have to be assignable to its parameters. `ref`/`out`/`in` parameters must match exactly. |
| Properties | A public instance property or field with the same name, and a public getter/setter where the interface declares one (a field needs to be non-`readonly` for a setter). A getter's type has to be assignable to the interface's type, a setter's the other way round - with both, the types have to convert in both directions. |
| Indexers | A public indexer with the same accessors, assignable parameter types and a type following the property rules. |
| Events | A public event with the same name and delegate type. |
| Default interface members | Optional. If the type has a matching member, it is used; otherwise the default implementation runs. |

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

Types that already implement the interface - directly, explicitly or through variance - are passed through without an adapter. For all others, generic methods (`U Map<U>()`), `ref` returns and `static abstract` members can't be adapted and are reported with [`IFITQUACKS005`](diagnostics.md#ifitquacks005).

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

That makes it a cheap way to expose a narrower view of a type, e.g. hiding members of an entity behind a get-only interface. It is not a replacement for a mapper: members must match by name and have compatible types, nested objects and collections are not converted, and the result is not a standalone DTO (serializers see the adapter type, and changes to the source are still visible).

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
