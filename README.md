<p align="center">
  <img src="assets/logo.png" alt="IfItQuacks logo" width="160" height="160" />
</p>

# IfItQuacks

[![Nuget](https://img.shields.io/nuget/dt/IfItQuacks?style=flat-square)](https://www.nuget.org/packages/IfItQuacks/)
[![GitHub tag](https://img.shields.io/github/v/tag/linkdotnet/IfItQuacks?include_prereleases&logo=github&style=flat-square)](https://github.com/linkdotnet/IfItQuacks/releases)

Compile-time checked structural (duck) typing for C#: "If it walks like a duck and quacks like a duck, it's a duck."

## Getting Started

> PM> Install-Package IfItQuacks

IfItQuacks relies on [interceptors](https://github.com/dotnet/roslyn/blob/main/docs/features/interceptors.md), so the generated namespace has to be enabled in your project file:

```xml
<PropertyGroup>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);IfItQuacks.Generated</InterceptorsNamespaces>
</PropertyGroup>
```

Declare a shape, mark a method as duck-typed and pass in anything that fits:

```csharp
using IfItQuacks;

[DuckShape]
public interface IDoable
{
    void Do();
}

public class A
{
    public void Do() => Console.WriteLine("A.Do");
}

public class B
{
    public void Do() => Console.WriteLine("B.Do");
}

public static partial class Ops
{
    [DuckTyped]
    public static void Foo(IDoable doable) => doable.Do();
}

Ops.Foo(new A()); // A.Do
Ops.Foo(new B()); // B.Do
```

Neither `A` nor `B` implements `IDoable`. The generator verifies at compile time that both have a matching `Do` method and redirects each call through a small generated adapter. There is no reflection and no `dynamic`.

Generic shapes and generic methods work too - the type arguments are inferred per call:

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

int number = Ops.Unwrap(new IntBox());             // 42
string text = Ops.Unwrap(new Box<string>("quack")); // quack
```

### Converting explicitly with `Duck.As`

`[DuckTyped]` methods only cover passing a value into a method. To store a duck-typed value in a field, a collection or return it, convert it explicitly:

```csharp
List<IDoable> doables = [Duck.As<IDoable>(new A()), Duck.As<IDoable>(new B())];

foreach (var doable in doables)
    doable.Do();
```

The call is verified at compile time just like a `[DuckTyped]` call and replaced by `new A_As_IDoable(a)`. If the value already implements the shape, it becomes a plain cast.

`Duck.As` is **not a mapper**: nothing is copied. The result is a live view that forwards every member access to the original object, so later changes to the object are visible through the shape (and setters write back to it). This makes it handy for exposing a narrower, read-only view of a type (`ICustomerView` over an entity), but it won't create a DTO, rename members or convert nested types for you.

## How does it work?

At compile time the generator writes a small wrapper that implements the shape and forwards to your type, then replaces your call so it passes that wrapper instead:

```csharp
// You write
Ops.Foo(new A());

// The generator emits (simplified)
struct A_As_IDoable(A value) : IDoable
{
    public void Do() => value.Do();
}

// and the compiler actually calls
Ops.Foo(new A_As_IDoable(new A()));
```

If `A` has no matching `Do()`, the build fails.

### Does it allocate?

Yes, exactly like a handwritten wrapper would. The adapter is a struct, but it is handed out as an interface, so it gets boxed:

| Call | Allocation (x64/arm64) |
|---|---|
| Class argument | one adapter object, 24 bytes |
| Argument already implementing the shape | none - passed through or cast |
| `readonly struct` via `[DuckTyped]` | one adapter object (its size depends on the struct) |
| `readonly struct` via `Duck.As` | two: the struct is boxed into the `object` parameter, then the adapter is boxed |

There is no hidden cost beyond that: no reflection, no caching, no runtime code generation. Details: [How does it work?](https://linkdotnet.github.io/IfItQuacks/articles/concepts.html)

## What does it solve?

Sometimes you want to treat unrelated types uniformly - types from third-party libraries you can't modify, generated code, or simply types that happen to share members - without writing wrapper classes by hand. Languages like Go and TypeScript offer structural typing out of the box; IfItQuacks brings a compile-time checked flavor of it to C#:

- **Type safe**: a type that doesn't fit the shape is a build error (`IFITQUACKS001`), not a runtime surprise.
- **Zero reflection**: adapters and interceptors are plain generated C#.
- **Works with existing types**: if a type already implements the interface it is passed through as-is.

## What doesn't it solve?

IfItQuacks is intentionally narrow. Current limitations:

- `Duck.As` and `[DuckTyped]` calls are verified against the argument's static type. A value typed as `object` or an open generic type parameter can't be verified (`IFITQUACKS001` or a runtime `DuckShapeMismatchException`, respectively).
- `[DuckTyped]` methods must be `static`, have exactly one parameter (not `ref`, `in`, `out` or `ref readonly`) and live in a `partial` type.
- Arguments must be classes or `readonly struct`s. Mutable structs would be silently copied into the adapter and ref structs can't be converted to an interface (`IFITQUACKS006`).
- Overloads of a `[DuckTyped]` method are not supported.
- Members are matched by exact name, return type, parameter types and ref-kinds - no variance or implicit conversions.
- Only calls within the compilation that references the generator are intercepted.
- Every type parameter of a generic `[DuckTyped]` method must appear in its parameter type. Shape members that are generic methods themselves are not supported.
- The argument's type must be known at compile time. Calls with an open generic type parameter hit the generated fallback, which throws `DuckShapeMismatchException`.

See [Known limitations](https://linkdotnet.github.io/IfItQuacks/articles/known_limitations.html) for details.

## Diagnostics

| Id | Description |
|---|---|
| `IFITQUACKS001` | Argument does not structurally satisfy the duck shape |
| `IFITQUACKS002` | The containing type of a `[DuckTyped]` method must be `partial` |
| `IFITQUACKS003` | The parameter of a `[DuckTyped]` method must be a `[DuckShape]` interface |
| `IFITQUACKS004` | Unsupported `[DuckTyped]` method signature |
| `IFITQUACKS005` | Unsupported shape member |
| `IFITQUACKS006` | Unsupported struct argument (mutable struct or ref struct) |
| `IFITQUACKS007` | The type argument of `Duck.As` must be a `[DuckShape]` interface |

## Documentation

More detailed documentation can be found [here](https://linkdotnet.github.io/IfItQuacks).

## Support & Contributing

Thanks to all [contributors](https://github.com/linkdotnet/IfItQuacks/graphs/contributors) and people that are creating bug-reports and valuable input:

<a href="https://github.com/linkdotnet/IfItQuacks/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=linkdotnet/IfItQuacks" alt="Supporters" />
</a>
