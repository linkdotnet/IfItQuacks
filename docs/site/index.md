[![Nuget](https://img.shields.io/nuget/dt/IfItQuacks?style=flat-square)](https://www.nuget.org/packages/IfItQuacks/)
[![GitHub tag](https://img.shields.io/github/v/tag/linkdotnet/IfItQuacks?include_prereleases&logo=github&style=flat-square)](https://github.com/linkdotnet/IfItQuacks/releases)

<img src="images/logo.png" alt="IfItQuacks logo" width="128" height="128" />

# IfItQuacks: compile-time checked structural typing for C#

IfItQuacks lets you pass objects of unrelated types to a method, as long as they structurally satisfy an interface. The check happens at compile time and the call is redirected through a generated adapter - no reflection, no `dynamic`.

## Download

IfItQuacks is available on [NuGet](https://www.nuget.org/packages/IfItQuacks/).

```bash
dotnet add package IfItQuacks
```

## Example usage

```csharp
public interface IDoable
{
    void Do();
}

public class A
{
    public void Do() => Console.WriteLine("A.Do");
}

public static partial class Ops
{
    [DuckTyped]
    public static void Foo(IDoable doable) => doable.Do();
}

Ops.Foo(new A()); // A.Do
```

Under the hood the generator writes a small wrapper that implements the interface and replaces your call so it passes that wrapper instead:

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

If `A` has no matching `Do()`, the build fails. See [How does it work?](articles/concepts.md) for details.

Head over to [Getting started](articles/getting_started.md) for the setup.
