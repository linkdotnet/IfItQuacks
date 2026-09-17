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
[DuckShape]
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

Head over to [Getting started](articles/getting_started.md) for the setup.
