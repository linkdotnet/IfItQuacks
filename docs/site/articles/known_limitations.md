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
Func<Person, string> f = Ops.Describe;   // a generated overload, see Delegates and method groups

// A call from another assembly referencing yours: the runtime fallback.
```

A method group only converts to a delegate whose parameter types are known at that point: `Func<Person, string>` works, `Func<INamed, string>` passes the interface through as before, but a duck-typed conversion inferred from a later step can't be generated.

The fallback is the generated overload that accepts anything and casts at runtime. It works if the value implements the interface and throws `DuckTypeMismatchException` otherwise.

One call shape can't be redirected at all: `base.Greet(duck)` invokes the method non-virtually, which neither an interceptor nor the fallback can reproduce, so it is reported with [`IFITQUACKS008`](diagnostics.md#ifitquacks008).

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
    [DuckTyped] public static string C(ref INamed n) => n.Name;       // IFITQUACKS003: no interface parameter by value
    [DuckTyped] public static T D<T>(INamed n, T value) => value;     // IFITQUACKS004: T unused by an interface parameter
}

public class Ops2 { [DuckTyped] public static string E(INamed n) => n.Name; }             // IFITQUACKS002: not partial
public partial class Ops3<T> { [DuckTyped] public static string F(INamed n) => n.Name; }  // IFITQUACKS004: generic type
public partial interface IOps { [DuckTyped] static string G(INamed n) => n.Name; }        // IFITQUACKS004: interface
file partial class Ops4 { [DuckTyped] public static string H(INamed n) => n.Name; }       // IFITQUACKS004: file-local
```

A `[DuckTyped]` method can share its name with regular overloads, e.g. `Greet(object)`. A call binding to such an overload is only redirected to the `[DuckTyped]` method if C# would pick it had the argument implemented the interface. That redirect is skipped, so the call stays on your overload, for anonymous types, `params` parameters, generic overloads, extension-method calls on a receiver and overloads with a different return type. This needs C# 13, also on .NET 8 ([`IFITQUACKS004`](diagnostics.md#ifitquacks004) otherwise).

`null`, `default` and omitted interface arguments work for methods with **up to four** interface parameters. Beyond that, every interface parameter needs an argument with a type.

## Interface members that can't be adapted

```csharp
public interface IMapper
{
    U Map<U>();                       // IFITQUACKS005: generic interface method
    static abstract IMapper Create();  // IFITQUACKS005: static abstract member on an interface parameter
}
```

Both are only reported for arguments that need an adapter. A type implementing the interface itself is passed through untouched.

`static abstract` members *are* supported through a duck-typed constraint (`where T : IAddable<T>`), because there the adapter is its own type argument. [Constrained duck typing](constrained_duck_typing.md) has its own limits:

```csharp
[DuckTyped] static T Sum<T>(T a, T b) where T : IAddable<T> => a + b;          // works
[DuckTyped] static string Mix<T>(T a, INamed n) where T : INamed => a.Name;    // works: constraints and interface parameters mix
Ops.Sum(new Money(1m), 2);                                                     // no: arguments must have the same type
[DuckTyped] static T Half<T, U>(T a, U b) where T : IAddable<T> => a;          // IFITQUACKS004: U has no interface constraint
Ops.Greet(new { Name = "Steven" });                                            // IFITQUACKS001: the overload can't name an anonymous type

public interface ICombinable<T> where T : ICombinable<T>
{
    T Combine(T other);   // IFITQUACKS005: an instance member using the self type
}
```

Conversion operators (`static abstract implicit operator`), `checked` operators and static abstract events aren't supported either.

## Generic `[DuckTyped]` methods

Type arguments are inferred by exact matching against the argument's members - no variance, no implicit conversions.

```csharp
[DuckTyped] public static T First<T>(IContainer<T> c) => c.Get();

First(new IntBox());                       // T = int
First(new { Get = 1 });                    // IFITQUACKS001: no anonymous types here
static T Nested<T>(Box<T> b) => First(b);  // CS0411: Box<T> is still open
```

## Delegates

A delegate only satisfies an interface with exactly one required member, and that member has to be a method.

```csharp
Comparison<int> comparison = (l, r) => l - r;
Duck.As<IComparer<int>>(comparison);        // works
Duck.As<IComparerShape>((int a, int b) => a - b); // works: the lambda has a natural type
Duck.As<ITwoMembers>(comparison);           // IFITQUACKS001: a delegate can only satisfy a single-method interface
Ops.Render((a, b) => a - b, 1);             // CS8917: an untyped lambda has no natural type - write (int a, int b)
```

## Sequences

Only interfaces using their element type in output position are adapted element by element, and only one level deep:

```csharp
Ops.Join(new List<Person>());              // works: IEnumerable<INamed>
Duck.As<IReadOnlyList<INamed>>(people);    // works: Count and the indexer included
Duck.As<IList<INamed>>(people);            // IFITQUACKS001: Add(INamed) would have to run backwards
Duck.As<IEnumerable<IEnumerable<INamed>>>(groups); // IFITQUACKS001: not nested
```

Every enumeration allocates one wrapper plus one adapter per element, so a sequence you walk repeatedly is cheaper materialised once.

## Mapped shapes

`[DuckShape<T>]` derives properties, and with `IncludeMethods` also methods, events and indexers. It stops there:

```csharp
[DuckShape<Customer>(Pick = [nameof(Customer.Id)])] public partial interface IKey;   // works
[DuckShape<Customer>] public interface INotPartial;                                  // IFITQUACKS010: not partial
[DuckShape<Customer>(Pick = ["Id"], Omit = ["Name"])] public partial interface IBoth; // IFITQUACKS011: mutually exclusive
[DuckShape<Customer>(Omit = ["Nope"])] public partial interface ITypo;                // IFITQUACKS011: unknown member
```

Public **fields** of the source are not derived, and neither are static members, generic methods or `init`-only setters. Nothing is shaped recursively: a derived `Address` member keeps its own type. Because `[DuckShape<T>]` is a generic attribute, the consuming project needs C# 11 or later. See [Mapped shapes](mapped_shapes.md).

## Copying with `Duck.To`

`Duck.To` matches members by name and assignable type, like everything else, and stops there:

```csharp
Duck.To<CustomerDto>(customer);               // works
Duck.To<INamed>(customer);                    // IFITQUACKS009: interfaces are Duck.As' job
Duck.To<CustomerDto>(new { FullName = "x" }); // IFITQUACKS009: no renaming
Duck.To<OrderDto>(order);                     // IFITQUACKS009 if a nested object would have to be converted too
```

Constructor parameters are matched to source members by name, ignoring case. A target with an inaccessible constructor, an abstract target and a `ref struct` target are all reported.

## Anonymous types

Their properties are read-only, so only interfaces with get-only properties can be satisfied. A member holding a delegate satisfies an interface *method*, but a lambda can't be assigned to an anonymous type property (`CS0828`), so its delegate type has to be written out:

```csharp
Duck.As<IRepository>(new { Find = (Func<int, Order?>)(id => ...) }); // works
Duck.As<IRepository>(new { Find = (int id) => ... });                // CS0828
```
 A property whose type *contains* an anonymous type can't be named by the generated adapter.

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

If you need a mutable struct, make it implement the interface or wrap it in a class - both make the copy explicit. Passing it by `ref` or `in` wouldn't help: the interceptor has to keep the signature of the call it replaces, which takes the argument by value. The same restriction applies to a [duck-typed constraint](constrained_duck_typing.md), where the adapter also holds a copy.

A `[DuckTyped]` method *on* a struct is a different thing and works: the receiver is passed by reference, so mutations reach the caller's value.

## Identity

Adapters of the same instance are equal and hash alike, and `ToString` is forwarded, but every conversion creates its own adapter.

```csharp
var view = Duck.As<INamed>(person);
var other = Duck.As<INamed>(person);

view.Equals(other);            // true
ReferenceEquals(view, other);  // false - two boxed adapters
Duck.Unwrap(view) == person;   // true
```

An adapter only equals adapters of its own type, never the original - compare with `Duck.Unwrap` instead:

```csharp
view.Equals(person);                  // false
person.Equals(view);                  // false
Duck.Unwrap(view).Equals(person);     // true
view.Equals(Duck.As<IOther>(person)); // false - a different adapter type
```

Inside a `[DuckTyped]` method the parameter is the adapter, not your instance - with a [duck-typed constraint](constrained_duck_typing.md) `T` is the adapter type itself. Casting back to the original type fails at runtime and is reported as [`IFITQUACKS012`](diagnostics.md#ifitquacks012):

```csharp
[DuckTyped] static string Name<T>(T named) where T : INamed
{
    var a = (Person)(object)named;        // InvalidCastException
    var b = (Person)Duck.Unwrap(named)!;  // works (boxes the adapter)
    return named.Name;
}
```

A returned `T` is converted back by the generated overload, so `Echo(person)` still returns a `Person`. If the argument already implements the interface, there is no adapter and the cast succeeds.

## Extension methods

The generated fallback for a `[DuckTyped]` extension method has an unconstrained type parameter, so the method shows up on every type in scope and a receiver that doesn't fit reports [`IFITQUACKS001`](diagnostics.md#ifitquacks001) rather than `CS1061`. Keep such methods in a namespace you import deliberately.

## Generated code is part of your type

- `[DuckTyped]` methods get generated overloads on the containing type. They are hidden from IntelliSense, but still visible through reflection (see [How does it work?](concepts.md)).
- A `private`, `protected` or `private protected` `[DuckTyped]` method is called through a generated `internal` forwarder (`__IfItQuacks_<Name>`), which makes it reachable inside the assembly.
- An argument whose type is a `private`, `protected` or `private protected` nested type gets its adapter nested in the type declaring it, so that type and every type around it must be `partial`; otherwise the call reports [`IFITQUACKS001`](diagnostics.md#ifitquacks001). Only `[DuckTyped]` calls to non-generic methods and `Duck.As` support this.
- `[DuckTyped]` on an override calls the method through the base type that declares it, so a `protected` or `private protected` override reports [`IFITQUACKS004`](diagnostics.md#ifitquacks004); put `[DuckTyped]` on the base method instead.
- The generated `IfItQuacks` types are `internal`. If a project using IfItQuacks grants `InternalsVisibleTo` to another project that uses it too, the compiler warns about the duplicate types (`CS0436`) and uses the local ones.

## Allocations

Passing an adapter as an interface boxes it: 24 bytes for a class argument, more for a struct, and `Duck.As` with a `readonly struct` allocates twice. The JIT often removes that box when nothing escapes your method, but don't rely on it. See [Benchmarks](benchmarks.md) for measured numbers and [Allocations](concepts.md#allocations) for the mechanics.

A `[DuckTyped]` method whose duck type is a constrained type parameter (`where T : IShape`) allocates nothing at all, because the adapter is passed as a type argument instead of an interface - see [Constrained duck typing](constrained_duck_typing.md).
