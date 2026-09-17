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

## What does it solve?

Sometimes you want to treat unrelated types uniformly - types from third-party libraries you can't modify, generated code, or simply types that happen to share members - without writing wrapper classes by hand. Languages like Go and TypeScript offer structural typing out of the box; IfItQuacks brings a compile-time checked flavor of it to C#:

- **Type safe**: a type that doesn't fit the shape is a build error (`DUCK001`), not a runtime surprise.
- **Zero reflection**: adapters and interceptors are plain generated C#.
- **Works with existing types**: if a type already implements the interface it is passed through as-is.

## What doesn't it solve?

IfItQuacks is intentionally narrow. Current limitations:

- `[DuckTyped]` methods must be `static`, have exactly one parameter and live in a `partial` type.
- Overloads of a `[DuckTyped]` method are not supported.
- Members are matched by exact name, return type, parameter types and ref-kinds - no variance or implicit conversions.
- Only calls within the compilation that references the generator are intercepted.
- The argument's type must be known at compile time. Calls with an open generic type parameter hit the generated fallback, which throws `DuckShapeMismatchException`.

See [Known limitations](https://linkdotnet.github.io/IfItQuacks/articles/known_limitations.html) for details.

## Diagnostics

| Id | Description |
|---|---|
| `DUCK001` | Argument does not structurally satisfy the duck shape |
| `DUCK002` | The containing type of a `[DuckTyped]` method must be `partial` |
| `DUCK003` | The parameter of a `[DuckTyped]` method must be a `[DuckShape]` interface |
| `DUCK004` | Unsupported `[DuckTyped]` method signature |
| `DUCK005` | Unsupported shape member |

## Documentation

More detailed documentation can be found [here](https://linkdotnet.github.io/IfItQuacks).

## Support & Contributing

Thanks to all [contributors](https://github.com/linkdotnet/IfItQuacks/graphs/contributors) and people that are creating bug-reports and valuable input:

<a href="https://github.com/linkdotnet/IfItQuacks/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=linkdotnet/IfItQuacks" alt="Supporters" />
</a>
