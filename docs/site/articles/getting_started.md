---
uid: getting_started
---

# Getting started

## Installation

```bash
dotnet add package IfItQuacks
```

IfItQuacks is a source generator built on top of [interceptors](https://github.com/dotnet/roslyn/blob/main/docs/features/interceptors.md). Interceptors are opt-in per namespace, so add the generated namespace to your project file:

```xml
<PropertyGroup>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);IfItQuacks.Generated</InterceptorsNamespaces>
</PropertyGroup>
```

## Building blocks

The generator adds the following `internal` types to the `IfItQuacks` namespace of your project. Because they are internal, several projects in the same solution can use IfItQuacks without type conflicts:

| Type | Purpose |
|---|---|
| `DuckShapeAttribute` | Marks an interface as a structural "shape" that other types may satisfy without implementing it. |
| `DuckTypedAttribute` | Marks a method whose `[DuckShape]` parameters accept any type that structurally matches the interface. |
| `Duck` | `Duck.As<TShape>(value)` converts a value to a shape it structurally satisfies. |
| `DuckShapeMismatchException` | Thrown by the generated fallback if a call could not be verified at compile time. |

## Defining a shape

A shape is a regular interface decorated with `[DuckShape]`. The following members (including those of base interfaces) are part of the shape:

| Member | Matched against |
|---|---|
| Methods | A public method with the same name, return type, parameter types and ref-kinds. |
| Properties | A public property with the same name and type, and a public getter/setter where the shape declares one. |
| Indexers | A public indexer with the same type, parameter types and accessors. |
| Events | A public event with the same name and delegate type. |
| Default interface members | Optional. If the type has a matching member, it is used; otherwise the default implementation runs. |

Generic methods (`U Map<U>()`), `ref` returns and `static abstract` members can't be adapted and are reported with [`IFITQUACKS005`](diagnostics.md#ifitquacks005).

```csharp
using IfItQuacks;

[DuckShape]
public interface INameable
{
    string Name { get; set; }
}
```

## Declaring a duck-typed method

A `[DuckTyped]` method lives in a `partial` type and has at least one parameter of a `[DuckShape]` interface type. It can be static or an instance method and take any number of other parameters:

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

Every `[DuckShape]` parameter is duck-typed; all other parameters behave as usual, including `ref`/`out`, `params`, default values and named arguments. See [Known limitations](known_limitations.md) for the few signatures that aren't supported.

## Calling it

Any type exposing the shape's members publicly can be passed in:

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

## Generic shapes

Shapes can be generic, and `[DuckTyped]` methods can be generic too. The type arguments are inferred from the argument's members:

```csharp
[DuckShape]
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

Every type parameter of the method has to appear in a `[DuckShape]` parameter type, otherwise it can't be inferred ([`IFITQUACKS004`](diagnostics.md#ifitquacks004)). Other parameters may use the inferred type parameters, e.g. `T Add<T>(IContainer<T> container, T fallback)`. Closed generic shapes like `IContainer<int>` work on non-generic methods as well.

## Converting explicitly

Use `Duck.As<TShape>(value)` when the duck-typed value has to outlive a single call - for example to store it in a field, add it to a collection or return it:

```csharp
List<INameable> nameables = [Duck.As<INameable>(new Person()), Duck.As<INameable>(new Pet())];
```

The same compile-time checks apply as for `[DuckTyped]` methods. The type argument must be a `[DuckShape]` interface ([`IFITQUACKS007`](diagnostics.md#ifitquacks007)).

### A view, not a mapper

`Duck.As` doesn't copy anything. The returned object forwards to the original instance:

```csharp
var customer = new Customer { Name = "Steven", Email = "steven@example.com" };
var view = Duck.As<ICustomerView>(customer); // ICustomerView { string Name { get; } string Email { get; } }

customer.Email = "quack@example.com";
Console.WriteLine(view.Email); // quack@example.com
```

That makes it a cheap way to expose a narrower view of a type, e.g. hiding members of an entity behind a get-only shape. It is not a replacement for a mapper: members must match by name and type, nested objects and collections are not converted, and the result is not a standalone DTO (serializers see the adapter type, and changes to the source are still visible).

Runnable examples live in [`samples`](https://github.com/linkdotnet/IfItQuacks/tree/main/samples), one project per showcase:

| Project | Shows |
|---|---|
| `IfItQuacks.Sample.Methods` | Static and instance `[DuckTyped]` methods with several parameters |
| `IfItQuacks.Sample.Properties` | Read-write property shapes |
| `IfItQuacks.Sample.Generics` | Generic shapes and generic `[DuckTyped]` methods |
| `IfItQuacks.Sample.Conversion` | `Duck.As` for collections and read-only views |
