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

Generic shapes (`IContainer<T>`), generic methods, events, indexers and default interface members are supported as well.

## Documentation

- [Getting started](https://linkdotnet.github.io/IfItQuacks/articles/getting_started.html)
- [How does it work?](https://linkdotnet.github.io/IfItQuacks/articles/concepts.html) - including what gets allocated
- [Diagnostics](https://linkdotnet.github.io/IfItQuacks/articles/diagnostics.html)
- [Known limitations](https://linkdotnet.github.io/IfItQuacks/articles/known_limitations.html)

Runnable examples live in [`samples`](samples).

## Support & Contributing

Thanks to all [contributors](https://github.com/linkdotnet/IfItQuacks/graphs/contributors) and people that are creating bug-reports and valuable input:

<a href="https://github.com/linkdotnet/IfItQuacks/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=linkdotnet/IfItQuacks" alt="Supporters" />
</a>
