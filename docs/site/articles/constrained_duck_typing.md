---
uid: constrained_duck_typing
---

# Constrained duck typing

Duck typing normally costs one small object per call: the adapter is passed as an interface, so it is
boxed. Write the duck type as a **constrained type parameter** instead and that cost disappears.

```csharp
public interface IPerson
{
    string Name { get; }
    int Age { get; }
}

public static partial class Ops
{
    [DuckTyped]
    public static int Describe<T>(T person) where T : IPerson => person.Name.Length + person.Age;
}

// Person does not implement IPerson.
Ops.Describe(new Person());
```

The two forms are interchangeable from the caller's side - `Ops.Describe(new Person())` looks the same
either way. What changes is the code the generator emits.

## What the generator does

For an interface parameter the call is *intercepted* and the adapter is boxed as the interface. For a
constrained type parameter there is nothing to intercept: the generator adds a concrete overload that
passes the adapter **as the type argument**.

```csharp
// You write
[DuckTyped] public static int Describe<T>(T person) where T : IPerson => person.Name.Length + person.Age;

// The generator emits
[EditorBrowsable(EditorBrowsableState.Never)]
public static int Describe(global::Person person) =>
    Describe<ShapeAdapter_IPerson_Person>(new ShapeAdapter_IPerson_Person(person));
```

`ShapeAdapter_IPerson_Person` is the same `readonly struct` as always - it is only used differently. A
struct type argument makes the runtime compile a separate copy of `Describe` for that adapter, so the
calls to `person.Name` and `person.Age` inside your method are `constrained callvirt`s the JIT
devirtualizes, and the adapter's one-line forwarders inline into the body. Nothing is boxed, because
the adapter never becomes an interface.

## What it costs

Measured with BenchmarkDotNet on an Apple M2 Pro, .NET 10, per 1000 calls
(see [Benchmarks](benchmarks.md) for the full table):

| Scenario | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Concrete parameter (no interface) | 571 ns | 1.00 | - |
| `[DuckTyped]`, interface parameter, class argument | 3,694 ns | 6.47 | 24,000 B |
| **`[DuckTyped]`, constrained, class argument** | **562 ns** | **0.98** | **-** |
| `[DuckTyped]`, interface parameter, `readonly struct` | 4,940 ns | 8.65 | 40,000 B |
| **`[DuckTyped]`, constrained, `readonly struct`** | **966 ns** | **1.69** | **-** |

A constrained class argument is as fast as having written the concrete type into the signature, and
allocates nothing. A `readonly struct` still pays for the copy into the adapter, but not for a box.

### Where it beats an interface outright

An interface parameter is not automatically slow: if exactly one shape ever reaches it, the JIT guesses
the target and the call is as fast as a direct one. That guess is what breaks when several shapes share
a call site - and it is where a constrained type parameter wins, because each shape gets its own
specialized copy and there is nothing left to guess:

| Three shapes, one method, per 1000 iterations | Mean | Allocated |
|---|---:|---:|
| Interface parameter | 10,459 ns | 72,000 B |
| Constrained type parameter | **3,005 ns** | **-** |

## When to use which

Use the **interface parameter** by default. It is the simpler signature, it accepts `null`, `default`
and omitted arguments freely, and it works with anonymous types.

Reach for the **constrained** form when the call sits on a hot path, when several different shapes flow
through the same method, or when you want a duck-typed API that allocates nothing at all.

## What it supports

Everything structural matching supports - properties, fields, methods, indexers, events and default
interface members - plus a few things the interface form cannot do.

```csharp
// Several independent constrained parameters, each with its own adapter.
[DuckTyped]
public static string Introduce<TFirst, TSecond>(TFirst first, TSecond second)
    where TFirst : INamed where TSecond : INamed => $"{first.Name} and {second.Name}";

// A constrained parameter next to an ordinary interface parameter.
[DuckTyped]
public static string Label<T>(T priced, ILog log) where T : IPriced { ... }

// Extension methods.
[DuckTyped]
public static string Shout<T>(this T named) where T : INamed => named.Name.ToUpperInvariant();

// Generic and self-referencing shapes, including static abstract members and generic math.
[DuckTyped]
public static int Unwrap<T>(T box) where T : IContainer<int> => box.Get();

[DuckTyped]
public static T Sum<T>(T a, T b) where T : IAddable<T> => a + b;
```

An argument that already implements the interface binds to your method directly - no overload, no
adapter, nothing generated.

## Limits

- **Anonymous types don't work.** The generated overload has to name the argument's type in its
  signature, and an anonymous type has no name. Reported as
  [`IFITQUACKS001`](diagnostics.md#ifitquacks001); use an interface parameter instead.
- **Every type parameter needs exactly one interface constraint used by a by-value parameter**, and all
  arguments bound to the same type parameter must have the same type
  ([`IFITQUACKS004`](diagnostics.md#ifitquacks004)).
- **Mutable structs and `ref struct`s** are still rejected
  ([`IFITQUACKS006`](diagnostics.md#ifitquacks006)), because the adapter holds a copy.
- Calls the generator can't see still fall through to the generic method itself, which requires the
  argument to implement the interface nominally.
- One adapter and one overload are generated per shape and argument type, so a method called with many
  different shapes produces more code than the interface form.

Runnable examples live in [`samples/IfItQuacks.Sample.Constraints`](https://github.com/linkdotnet/IfItQuacks/tree/main/samples/IfItQuacks.Sample.Constraints).
