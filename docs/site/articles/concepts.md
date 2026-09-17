---
uid: concepts
---

# How does it work?

IfItQuacks is a Roslyn incremental source generator. For every `[DuckTyped]` method it performs four steps at compile time.

The generator only looks at calls whose name matches a `[DuckTyped]` method or `Duck.As`, and caches its results per call site. Editing a file without such calls doesn't regenerate anything, which keeps the IDE responsive in large solutions.

## 1. Fallback overload

A call like `Ops.Foo(new A())` would not compile, because `A` is not an `IDoable`. To make it compile, the generator adds a generic overload to the `partial` containing type, with one type parameter per `[DuckShape]` parameter. All other parameters are copied, including ref-kinds and default values:

```csharp
// [DuckTyped] public string Meet(INamed first, int times, INamed second)
[EditorBrowsable(EditorBrowsableState.Never)]
public string Meet<TDuck0, TDuck2>(TDuck0 first, int times, TDuck2 second)
{
    throw new global::IfItQuacks.DuckShapeMismatchException(typeof(TDuck0), typeof(INamed));
}
```

The call now binds to the generic overload. It is hidden from IntelliSense and never supposed to run - it is replaced in the next steps.

## 2. Shape matching

For every call of the `[DuckTyped]` method, the static type of each argument passed to a `[DuckShape]` parameter is checked against that shape. Arguments are matched to parameters by position or by name. Every shape member needs a public counterpart (see [Defining a shape](getting_started.md#defining-a-shape)); members with a default implementation are optional.

Members inherited from base classes count. If something is missing, [`IFITQUACKS001`](diagnostics.md#ifitquacks001) is reported on the argument.

## 3. Adapter

If the argument type does not already implement the shape, a `readonly struct` adapter is emitted into `IfItQuacks.Generated`:

```csharp
internal readonly struct ShapeAdapter_IDoable_A : global::IDoable
{
    private readonly A _value;
    public ShapeAdapter_IDoable_A(A value) => _value = value;
    public void Do() => _value.Do();
}
```

One adapter is generated per shape/type combination.

## 4. Interceptor

Finally, every verified call site is intercepted:

```csharp
[InterceptsLocation(...)]
public static void Interceptor_1(A value)
{
    global::Ops.Foo((global::IDoable)(new global::IfItQuacks.Generated.ShapeAdapter_IDoable_A(value)));
}
```

The compiler replaces the call to the fallback overload with the interceptor, which wraps each duck-typed argument and calls your original method. The cast to the interface makes sure overload resolution picks your method and not the generic fallback.

For instance methods the interceptor takes the receiver as its first parameter and forwards the remaining arguments as they are:

```csharp
[InterceptsLocation(...)]
public static string Interceptor_2(this global::Greeter @this, Person first, Pet second, string greeting)
{
    return @this.Greet((global::INamed)(new ShapeAdapter_INamed_Person(first)), (global::INamed)(new ShapeAdapter_INamed_Pet(second)), greeting);
}
```

Because the adapter is passed as an interface, each call boxes the adapter (see [Allocations](#allocations)). The generated members are trivial forwarders, so they are not marked with `[MethodImpl(MethodImplOptions.AggressiveInlining)]`: calls through an interface can't be inlined by that attribute, and benchmarks showed no difference.

Since the adapter holds a copy of the argument, only classes and `readonly struct`s are supported. See [Known limitations](known_limitations.md#structs) for details.

## Duck.As

`Duck.As<TShape>(object value)` is part of the generated `IfItQuacks` namespace. Its body only throws `DuckShapeMismatchException`; every call the generator can verify is intercepted instead:

```csharp
// You write
IDoable doable = Duck.As<IDoable>(new A());

// The generator emits
[InterceptsLocation(...)]
public static global::IDoable Interceptor_2(object value) =>
    (global::IDoable)(new global::IfItQuacks.Generated.ShapeAdapter_IDoable_A((A)value));
```

The argument's static type (`A`) drives shape matching and adapter selection, exactly as for `[DuckTyped]` methods, and the same adapters are shared. If `A` already implements `IDoable`, the interceptor is a plain cast and no adapter is involved.

## Allocations

Nothing here is free, but nothing is hidden either. The generated code is what you would write by hand:

| Scenario | What happens | Allocation (x64/arm64) |
|---|---|---|
| Class argument | The adapter struct holds the reference and is boxed as the interface. | 24 bytes, one object |
| Argument already implements the shape | Passed through (`[DuckTyped]`) or cast (`Duck.As`). | none |
| `readonly struct` via `[DuckTyped]` | The interceptor takes the struct by value; the adapter holding a copy is boxed. | one object: 16 bytes + struct size |
| `readonly struct` via `Duck.As` | The struct is boxed into the `object` parameter, unboxed by the interceptor, then the adapter is boxed. | two objects |

Numbers were measured with `GC.GetAllocatedBytesForCurrentThread`. The JIT may elide the box when it can inline your method and prove the adapter doesn't escape, but don't rely on it. Prefer classes (or types implementing the shape) on hot paths when using `Duck.As`.

## Generic methods

A generic `[DuckTyped]` method like `T Unwrap<T>(IContainer<T> c)` can't use the fallback and interceptor from above. An interceptor has to keep the signature of the call it replaces, and a generic fallback `Unwrap<TArg>(TArg value)` can't return `int` for one call and `string` for another.

Instead, the generator infers the method's type arguments per argument type by matching the shape's members against the argument's members, e.g. `int Get()` against `T Get()` gives `T = int`. It then emits one adapter per closed shape (`IContainer<int>`, `IContainer<string>`, ...) and one concrete overload per combination of argument types. The overloads are hidden from IntelliSense:

```csharp
public static partial class Ops
{
    public static int Unwrap(global::IntBox value) =>
        Unwrap<int>(new global::IfItQuacks.Generated.ShapeAdapter_IContainer_int__IntBox(value));

    public static string Unwrap(global::Box<string> value) =>
        Unwrap<string>(new global::IfItQuacks.Generated.ShapeAdapter_IContainer_string__Box_string_(value));
}
```

The call `Ops.Unwrap(new IntBox())` binds to the concrete overload, which has the correct return type. Other parameters are substituted as well, so `T Add<T>(IContainer<T> c, T fallback)` gets `int Add(IntBox c, int fallback)`. If every duck-typed argument already implements its closed shape, the call binds to your method directly and no overload is generated. If the argument's type isn't publicly visible, the overload is generated as `internal`.

A non-generic method with a closed generic shape, e.g. `int Sum(IContainer<int> c)`, uses the regular fallback and interceptor.
