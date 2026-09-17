---
uid: known_limitations
---

# Known limitations

Everything IfItQuacks does happens while your project compiles: the generator looks at a call, matches the argument's **compile-time type** against the interface, and rewrites the call. Nearly every limitation below follows from that one sentence - the generator has to see the call, and it has to be able to name the type it adapts.

## The call has to be visible

Only direct calls inside the project that references the generator are intercepted.

```csharp
Ops.Describe(new Person());              // intercepted
Describe(new Person());                  // intercepted (implicit this)

Func<INamed, string> f = Ops.Describe;   // not intercepted: a method group, not a call
f(duck);                                 // binds to the fallback

// A call from another assembly referencing yours: also the fallback.
```

The fallback is the generated overload that accepts anything and casts at runtime. It works if the value implements the interface and throws `DuckTypeMismatchException` otherwise.

## Matching uses the static type

The generator only sees the type the compiler wrote down at the call site.

```csharp
var duck = new Person();
Duck.As<INamed>(duck);           // fine
Duck.As<INamed>((object)duck);   // IFITQUACKS001: object has no Name
Duck.As<TShape>(duck);           // IFITQUACKS007: the target must be an interface, not a type parameter

static string Wrap<T>(T value) => Ops.Describe(value); // T is open: runtime fallback
```

## Members match by name and assignable type

Member types only have to be *assignable* (identity, reference, boxing, numeric or nullable conversions). Nothing else is bridged: no renaming, no user-defined conversions, and `ref`/`out`/`in` parameters and events have to match exactly.

```csharp
public interface IPerson { string Name { get; } }

public class Employee { public string FullName => "Steven"; }                    // IFITQUACKS001: no Name
public class Salary { public static implicit operator string(Salary s) => ""; }  // conversions are ignored
```

`Duck.As` is not a mapper either: it returns a view that forwards to the original instance. It never copies values, renames members or converts nested objects.

## Method and containing type

```csharp
public static partial class Ops
{
    [DuckTyped] public static string A(INamed n) => n.Name;           // works
    [DuckTyped] public static string A(INamed n, int i) => n.Name;    // IFITQUACKS004: one [DuckTyped] method per name
    [DuckTyped] public static string B(this INamed n) => n.Name;      // IFITQUACKS004: extension method
    [DuckTyped] public static string C(ref INamed n) => n.Name;       // IFITQUACKS003: no interface parameter by value
    [DuckTyped] public static T D<T>(INamed n, T value) => value;     // IFITQUACKS004: T unused by an interface parameter
}

public class Ops2 { [DuckTyped] public static string E(INamed n) => n.Name; }             // IFITQUACKS002: not partial
public partial class Ops3<T> { [DuckTyped] public static string F(INamed n) => n.Name; }  // IFITQUACKS004: generic type
public partial interface IOps { [DuckTyped] static string G(INamed n) => n.Name; }        // IFITQUACKS004: interface
file partial class Ops4 { [DuckTyped] public static string H(INamed n) => n.Name; }       // IFITQUACKS004: file-local
```

`null`, `default` and omitted interface arguments work for methods with **up to four** interface parameters. Beyond that, every interface parameter needs an argument with a type.

## Interface members that can't be adapted

```csharp
public interface IMapper
{
    U Map<U>();                       // IFITQUACKS005: generic interface method
    static abstract IMapper Create();  // IFITQUACKS005: static abstract member
}
```

Both are only reported for arguments that need an adapter. A type implementing the interface itself is passed through untouched.

## Generic `[DuckTyped]` methods

Type arguments are inferred by exact matching against the argument's members - no variance, no implicit conversions.

```csharp
[DuckTyped] public static T First<T>(IContainer<T> c) => c.Get();

First(new IntBox());                       // T = int
First(new { Get = 1 });                    // IFITQUACKS001: no anonymous types here
static T Nested<T>(Box<T> b) => First(b);  // CS0411: Box<T> is still open
```

## Anonymous types

Their properties are read-only, so only interfaces with get-only properties can be satisfied. A property whose type *contains* an anonymous type can't be named by the generated adapter.

```csharp
Duck.As<INamed>(new { Name = "Steven" });                 // works
Duck.As<IWritableName>(new { Name = "Steven" });          // IFITQUACKS001: needs a setter
Duck.As<IRows>(new { Rows = new[] { new { A = 1 } } });   // IFITQUACKS001: property type can't be named
```

## Structs

The adapter holds a **copy** of the argument. For classes that copy is a reference, for structs it is the whole value, so struct arguments are restricted and reported with [`IFITQUACKS006`](diagnostics.md#ifitquacks006):

| Argument type | Supported | Why |
|---|---|---|
| `class` | Yes | The adapter holds a reference to the original object. |
| `readonly struct` | Yes | The value can't be mutated, so the copy is invisible. |
| `struct` implementing the interface | Yes | No adapter is involved - the compiler boxes it, exactly like passing it to an interface parameter without IfItQuacks. |
| mutable `struct` | No | Mutations made through the interface would only affect the hidden copy inside the adapter, never the caller's value. |
| `ref struct` | No | A ref struct can't be boxed or stored in an adapter, so it can never be passed as an interface. |

```csharp
public struct Counter
{
    public int Count { get; private set; }
    public void Increment() => Count++;
}

Ops.Bump(new Counter()); // IFITQUACKS006: mutable structs are copied into an adapter, ...
```

If you need a mutable struct, make it implement the interface or wrap it in a class - both make the copy explicit. Passing it by `ref` or `in` wouldn't help: the interceptor has to keep the signature of the call it replaces, which takes the argument by value.

A `[DuckTyped]` method *on* a struct is a different thing and works: the receiver is passed by reference, so mutations reach the caller's value.

## Identity

Adapters forward `Equals`, `GetHashCode` and `ToString`, but every conversion creates its own adapter.

```csharp
var view = Duck.As<INamed>(person);
var other = Duck.As<INamed>(person);

view.Equals(other);            // true
ReferenceEquals(view, other);  // false - two boxed adapters
Duck.Unwrap(view) == person;   // true
```

## Generated code is part of your type

- `[DuckTyped]` methods get generated overloads on the containing type. They are hidden from IntelliSense, but still visible through reflection (see [How does it work?](concepts.md)).
- A `private`, `protected` or `private protected` `[DuckTyped]` method is called through a generated `internal` forwarder (`__IfItQuacks_<Name>`), which makes it reachable inside the assembly.
- The generated `IfItQuacks` types are `internal`. If a project using IfItQuacks grants `InternalsVisibleTo` to another project that uses it too, the compiler warns about the duplicate types (`CS0436`) and uses the local ones.

## Allocations

Passing an adapter as an interface boxes it: 24 bytes for a class argument, more for a struct, and `Duck.As` with a `readonly struct` allocates twice. The JIT often removes that box when nothing escapes your method, but don't rely on it. See [Benchmarks](benchmarks.md) for measured numbers and [Allocations](concepts.md#allocations) for the mechanics.
