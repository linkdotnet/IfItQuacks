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

The generator adds the following types to the `IfItQuacks` namespace of your project:

| Type | Purpose |
|---|---|
| `DuckShapeAttribute` | Marks an interface as a structural "shape" that other types may satisfy without implementing it. |
| `DuckTypedAttribute` | Marks a method whose parameter accepts any type that structurally matches its `[DuckShape]` interface. |
| `DuckShapeMismatchException` | Thrown by the generated fallback if a call could not be verified at compile time. |

## Defining a shape

A shape is a regular interface decorated with `[DuckShape]`. Instance methods and properties (including those of base interfaces) are part of the shape.

```csharp
using IfItQuacks;

[DuckShape]
public interface INameable
{
    string Name { get; set; }
}
```

## Declaring a duck-typed method

A `[DuckTyped]` method has to be `static`, take exactly one parameter of a `[DuckShape]` interface type and be declared in a `partial` type:

```csharp
public static partial class Greeter
{
    [DuckTyped]
    public static void Greet(INameable nameable) => Console.WriteLine($"Hello, {nameable.Name}!");
}
```

## Calling it

Any type exposing the shape's members publicly can be passed in:

```csharp
public class Person
{
    public string Name { get; set; } = "";
}

Greeter.Greet(new Person { Name = "Steven" }); // Hello, Steven!
```

`Person` does not implement `INameable`. If it didn't have a public `Name` property with a getter and setter, the build fails with [`DUCK001`](diagnostics.md#duck001).

Types that already implement the interface are passed through unchanged.

A runnable example lives in [`samples/IfItQuacks.Sample`](https://github.com/linkdotnet/IfItQuacks/tree/main/samples/IfItQuacks.Sample).
