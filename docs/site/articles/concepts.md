---
uid: concepts
---

# How does it work?

IfItQuacks is a Roslyn incremental source generator. For every `[DuckTyped]` method it performs four steps at compile time.

The generator only looks at calls whose name matches a `[DuckTyped]` method or `Duck.As`, and caches its results per call site. Editing a file without such calls doesn't regenerate anything, which keeps the IDE responsive in large solutions.

## 1. Fallback overload

A call like `Ops.Foo(new A())` would not compile, because `A` is not an `IDoable`. To make it compile, the generator adds a generic overload to the `partial` containing type, with one type parameter per interface parameter. All other parameters are copied, including ref-kinds and default values:

```csharp
// [DuckTyped] public string Meet(INamed first, int times, INamed second)
[EditorBrowsable(EditorBrowsableState.Never)]
public string Meet<TDuck0, TDuck2>(TDuck0 first, int times, TDuck2 second)
{
    return Meet(first is global::INamed __duck0 ? __duck0 : throw new global::IfItQuacks.DuckTypeMismatchException(typeof(TDuck0), typeof(global::INamed)), times, /* same for second */);
}
```

The call now binds to the generic overload. It is hidden from IntelliSense and replaced in the next steps. It only runs if a call couldn't be verified at compile time, e.g. from generic code or another assembly: then it calls your method if the value implements the interface at runtime, and throws `DuckTypeMismatchException` otherwise.

A generic parameter can't be inferred from `null`, `default` or an omitted argument. So the generator emits one overload per combination of generic and non-generic interface parameters - `Meet<TDuck0>(TDuck0 first, int times, INamed second)`, `Meet<TDuck2>(INamed first, int times, TDuck2 second)` and `Meet<TDuck0, TDuck2>(...)` above. Overload resolution then picks the one keeping `null` arguments, omitted defaults and values already typed as the interface as they are. For methods with more than four interface parameters only the all-generic overload is generated.

## 2. Structural matching

For every call of the `[DuckTyped]` method, the static type of each argument passed to an interface parameter is checked against that interface. Arguments are matched to parameters by position or by name. If the type already implements the interface (including explicit implementations and variance), nothing else is needed. Otherwise every interface member needs a public counterpart (see [Matching an interface](getting_started.md#matching-an-interface)); members with a default implementation are optional.

Members inherited from base classes count, public fields can satisfy properties, and member types only need to be assignable. If something is missing or incompatible, [`IFITQUACKS001`](diagnostics.md#ifitquacks001) is reported on the argument.

## 3. Adapter

If the argument type does not already implement the interface, a `readonly struct` adapter is emitted into `IfItQuacks.Generated`:

```csharp
internal readonly struct ShapeAdapter_IDoable_A : global::IDoable, global::IfItQuacks.IDuckAdapter
{
    private readonly A _value;
    public ShapeAdapter_IDoable_A(A value) => _value = value;
    object? global::IfItQuacks.IDuckAdapter.Value => _value;
    void global::IDoable.Do() => _value.Do();
    public override bool Equals(object? obj) => global::System.Object.Equals(_value, global::IfItQuacks.Duck.Unwrap(obj));
    public override int GetHashCode() => _value?.GetHashCode() ?? 0;
    public override string ToString() => _value?.ToString() ?? string.Empty;
}
```

One adapter is generated per interface/type combination. Interface members are implemented explicitly, so members with the same name from different interfaces (like `GetEnumerator()` of `IEnumerable<T>` and `IEnumerable`) don't clash. `Equals`, `GetHashCode` and `ToString` forward to the wrapped value, so adapters of the same instance are equal; `IDuckAdapter` lets `Duck.Unwrap` return that instance.

If a member was matched with an assignable instead of an identical parameter type, the forwarder casts the argument (`_value.Add((object)item)`), so the call binds to exactly the member that was matched.

### Anonymous types

An anonymous type has no name the adapter could use. But two anonymous object expressions with the same property names, types and order in one compilation have the same type, so the adapter stores the value as `object` and casts it back using an example expression:

```csharp
internal readonly struct ShapeAdapter_INamed_Anonymous_... : global::INamed, global::IfItQuacks.IDuckAdapter
{
    private readonly object _value;
    string global::INamed.Name { get => __CastByExample(_value, static () => new { Name = default(global::System.String)! }).Name; }
    private static T __CastByExample<T>(object value, global::System.Func<T> example) => (T)value;
    // Equals, GetHashCode, ToString, ...
}
```

The example lambda is never invoked and doesn't capture anything, so it isn't allocated per call. Because the argument's type can't be named in the interceptor either, calls with anonymous arguments get a generic interceptor with the same type parameters as the fallback overload.

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

`Duck.As<TShape>(object value)` is part of the generated `IfItQuacks` namespace. Its body only casts, throwing `DuckTypeMismatchException` if the value doesn't implement the interface; every call the generator can verify is intercepted instead:

```csharp
// You write
IDoable doable = Duck.As<IDoable>(new A());

// The generator emits
[InterceptsLocation(...)]
public static global::IDoable Interceptor_2(object value) =>
    (global::IDoable)(new global::IfItQuacks.Generated.ShapeAdapter_IDoable_A((A)value));
```

The argument's static type (`A`) drives structural matching and adapter selection, exactly as for `[DuckTyped]` methods, and the same adapters are shared. If `A` already implements `IDoable`, the interceptor is a plain cast and no adapter is involved.

## Allocations

Nothing here is free, but nothing is hidden either. The generated code is what you would write by hand:

| Scenario | What happens | Allocation (x64/arm64) |
|---|---|---|
| Class argument | The adapter struct holds the reference and is boxed as the interface. | 24 bytes, one object |
| Argument already implements the interface | Passed through (`[DuckTyped]`) or cast (`Duck.As`). | none |
| `readonly struct` via `[DuckTyped]` | The interceptor takes the struct by value; the adapter holding a copy is boxed. | one object: 16 bytes + struct size |
| `readonly struct` via `Duck.As` | The struct is boxed into the `object` parameter, unboxed by the interceptor, then the adapter is boxed. | two objects |

Numbers were measured with `GC.GetAllocatedBytesForCurrentThread`. The JIT may elide the box when it can inline your method and prove the adapter doesn't escape, but don't rely on it. Prefer classes (or types implementing the interface) on hot paths when using `Duck.As`.

## Generic methods

A generic `[DuckTyped]` method like `T Unwrap<T>(IContainer<T> c)` can't use the fallback and interceptor from above. An interceptor has to keep the signature of the call it replaces, and a generic fallback `Unwrap<TArg>(TArg value)` can't return `int` for one call and `string` for another.

Instead, the generator infers the method's type arguments per argument type by matching the interface's members against the argument's members, e.g. `int Get()` against `T Get()` gives `T = int`. It then emits one adapter per closed interface (`IContainer<int>`, `IContainer<string>`, ...) and one concrete overload per combination of argument types. The overloads are hidden from IntelliSense:

```csharp
public static partial class Ops
{
    public static int Unwrap(global::IntBox value) =>
        Unwrap<int>(new global::IfItQuacks.Generated.ShapeAdapter_IContainer_int__IntBox(value));

    public static string Unwrap(global::Box<string> value) =>
        Unwrap<string>(new global::IfItQuacks.Generated.ShapeAdapter_IContainer_string__Box_string_(value));
}
```

The call `Ops.Unwrap(new IntBox())` binds to the concrete overload, which has the correct return type. Other parameters are substituted as well, so `T Add<T>(IContainer<T> c, T fallback)` gets `int Add(IntBox c, int fallback)`. If every duck-typed argument already implements its closed interface, the call binds to your method directly and no overload is generated. If the argument's type isn't publicly visible, the overload is generated as `internal`.

A non-generic method with a closed generic interface, e.g. `int Sum(IContainer<int> c)`, uses the regular fallback and interceptor.
