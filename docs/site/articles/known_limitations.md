---
uid: known_limitations
---

# Known limitations

IfItQuacks is intentionally narrow. The following scenarios are currently not supported:

- **Signature**: only interface parameters passed by value are duck-typed (`ref`, `in` and `out` interface parameters behave as usual), and `[DuckTyped]` methods must not be overloaded by another `[DuckTyped]` method of the same name ([`IFITQUACKS004`](diagnostics.md#ifitquacks004)).
- **Containing type**: the containing type has to be `partial` ([`IFITQUACKS002`](diagnostics.md#ifitquacks002)) and non-generic. Interfaces, `file`-local types and extension methods are not supported ([`IFITQUACKS004`](diagnostics.md#ifitquacks004)).
- **Assignable types only**: member types have to be assignable through implicit identity, reference, boxing, numeric or nullable conversions. User-defined conversion operators aren't considered, `ref`/`out`/`in` parameters and events must match exactly, and members must match by name.
- **Static type only**: matching uses the argument's compile-time type. Passing an open generic type parameter (for example from inside a generic method) can't be verified; the call ends up in the generated fallback, which works if the value implements the interface at runtime and throws `DuckTypeMismatchException` otherwise.
- **`null` arguments**: `null`, `default` and omitted arguments for interface parameters are supported for methods with up to four interface parameters. Beyond that, every interface parameter needs an argument with a type.
- **Same compilation**: only calls inside the project referencing the generator are intercepted. Calls from other assemblies bind to the fallback, which only works for values implementing the interface.
- **Direct invocations**: only direct calls like `Ops.Foo(x)` or `Foo(x)` are intercepted, not method groups or delegates.
- **Generic methods**: every type parameter of a generic `[DuckTyped]` method must appear in an interface parameter type ([`IFITQUACKS004`](diagnostics.md#ifitquacks004)). Type arguments are inferred by exact matching, without variance or implicit conversions. Anonymous types can't be passed to generic `[DuckTyped]` methods ([`IFITQUACKS001`](diagnostics.md#ifitquacks001)). Arguments whose type is still an open generic (e.g. `Box<T>` inside another generic method) are not supported, and the compiler reports `CS0411`.
- **Generated overloads**: `[DuckTyped]` methods get generated overloads on the containing type (see [How does it work?](concepts.md)). They are hidden from IntelliSense, but still part of the type, e.g. for reflection.
- **`private` methods**: interceptors live in their own namespace, so a `private`, `protected` or `private protected` `[DuckTyped]` method is called through a generated `internal` forwarder (`__IfItQuacks_<Name>`). It is hidden from IntelliSense, but makes the method reachable within the assembly.
- **Interface members**: generic methods (`U Map<U>()`) and `static abstract` members can't be adapted for types that don't implement the interface ([`IFITQUACKS005`](diagnostics.md#ifitquacks005)).
- **InternalsVisibleTo**: the generated `IfItQuacks` types are `internal`. If a project using IfItQuacks grants `InternalsVisibleTo` to another project that uses it too, the compiler warns about the duplicate types (`CS0436`) and uses the local ones.
- **Boxing**: the adapter is passed as an interface, so every intercepted call allocates a small object (24 bytes on x64 for class arguments) unless the JIT inlines your method and can stack-allocate it. `Duck.As` with a `readonly struct` allocates twice. See [Allocations](concepts.md#allocations).
- **`Duck.As` static type**: `Duck.As` takes `object`, so the argument's static type matters. `Duck.As<IDoable>((object)a)` is reported as a mismatch ([`IFITQUACKS001`](diagnostics.md#ifitquacks001)) because `object` has no `Do()`. Inside generic code (`Duck.As<IDoable>(value)` with `value` of type `T`) the call can't be verified and only works if the value implements the interface at runtime, otherwise it throws `DuckTypeMismatchException`; a type parameter as target (`Duck.As<TShape>`) is rejected with [`IFITQUACKS007`](diagnostics.md#ifitquacks007).
- **Anonymous types**: their properties are read-only, so only interfaces with get-only properties can be satisfied. A property whose type contains an anonymous type as a type argument or array element (e.g. `new[] { new { A = 1 } }`) can't be named by the generated adapter ([`IFITQUACKS001`](diagnostics.md#ifitquacks001)).
- **Identity**: adapters forward `Equals`, `GetHashCode` and `ToString`, but `==` and `ReferenceEquals` on the interface compare the boxed adapters, which differ for every conversion. Use `Equals` or `Duck.Unwrap`.
- **Not a mapper**: `Duck.As` returns a view that forwards to the original instance. It doesn't copy values, rename members or convert nested objects.

## Structs

The argument is wrapped in an adapter, which holds a **copy** of it. For classes that copy is just a reference, but for structs it is the whole value. To avoid surprises, struct arguments are restricted and reported with [`IFITQUACKS006`](diagnostics.md#ifitquacks006):

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

var counter = new Counter();
Ops.Bump(counter); // error IFITQUACKS006: mutable structs are copied into an adapter, ...
```

If you need a mutable struct, either make it implement the interface or wrap it in a class yourself - both make the copy semantics explicit.

Passing the argument by `ref` or `in` wouldn't help either: the generated interceptor has to match the signature of the call it replaces, which takes the argument by value.
