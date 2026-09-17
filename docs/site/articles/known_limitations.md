---
uid: known_limitations
---

# Known limitations

IfItQuacks is intentionally narrow. The following scenarios are currently not supported:

- **Signature**: `[DuckTyped]` methods must be `static`, have exactly one parameter and must not be overloaded. The parameter must be passed by value - `ref`, `in`, `out` and `ref readonly` are rejected with [`IFITQUACKS004`](diagnostics.md#ifitquacks004).
- **Exact matching**: member types are compared exactly. There is no support for variance or implicit conversions.
- **Static type only**: matching uses the argument's compile-time type. Passing an open generic type parameter (for example from inside a generic method) can't be verified; the call ends up in the generated fallback, which throws `DuckShapeMismatchException` at runtime.
- **Same compilation**: only calls inside the project referencing the generator are intercepted. Calls from other assemblies bind to the fallback and throw.
- **Direct invocations**: only direct calls like `Ops.Foo(x)` or `Foo(x)` are intercepted, not method groups or delegates.
- **Generic methods**: every type parameter of a generic `[DuckTyped]` method must appear in its parameter type ([`IFITQUACKS004`](diagnostics.md#ifitquacks004)). Type arguments are inferred by exact matching, without variance or implicit conversions. Arguments whose type is still an open generic (e.g. `Box<T>` inside another generic method) are not supported, and the compiler reports `CS0411`.
- **Generated overloads**: generic `[DuckTyped]` methods are dispatched through generated concrete overloads (see [Generic methods](concepts.md#generic-methods)). They are visible on the containing type, e.g. in IntelliSense.
- **Generic shape members**: shape members that are generic methods themselves (`U Map<U>()`) are not supported.
- **Boxing**: the adapter is passed as an interface, so every intercepted call allocates a small object (24 bytes on x64 for class arguments) unless the JIT inlines your method and can stack-allocate it. `Duck.As` with a `readonly struct` allocates twice. See [Allocations](concepts.md#allocations).
- **`Duck.As` static type**: `Duck.As` takes `object`, so the argument's static type matters. `Duck.As<IDoable>((object)a)` is reported as a mismatch ([`IFITQUACKS001`](diagnostics.md#ifitquacks001)) because `object` has no `Do()`. Inside generic code (`Duck.As<IDoable>(value)` with `value` of type `T`) the call can't be verified and throws `DuckShapeMismatchException`; a type parameter as target (`Duck.As<TShape>`) is rejected with [`IFITQUACKS007`](diagnostics.md#ifitquacks007).
- **Not a mapper**: `Duck.As` returns a view that forwards to the original instance. It doesn't copy values, rename members or convert nested objects.

## Structs

The argument is wrapped in an adapter, which holds a **copy** of it. For classes that copy is just a reference, but for structs it is the whole value. To avoid surprises, struct arguments are restricted and reported with [`IFITQUACKS006`](diagnostics.md#ifitquacks006):

| Argument type | Supported | Why |
|---|---|---|
| `class` | Yes | The adapter holds a reference to the original object. |
| `readonly struct` | Yes | The value can't be mutated, so the copy is invisible. |
| `struct` implementing the shape | Yes | No adapter is involved - the compiler boxes it, exactly like passing it to an interface parameter without IfItQuacks. |
| mutable `struct` | No | Mutations made through the shape would only affect the hidden copy inside the adapter, never the caller's value. |
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

If you need a mutable struct, either make it implement the shape interface or wrap it in a class yourself - both make the copy semantics explicit.

Passing the argument by `ref` or `in` wouldn't help either: the generated interceptor has to match the signature of the call it replaces, which takes the argument by value.
