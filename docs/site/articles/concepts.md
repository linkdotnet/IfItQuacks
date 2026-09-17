---
uid: concepts
---

# How does it work?

IfItQuacks is a Roslyn incremental source generator. For every `[DuckTyped]` method it performs four steps at compile time.

## 1. Fallback overload

A call like `Ops.Foo(new A())` would not compile, because `A` is not an `IDoable`. To make it compile, the generator adds a generic overload to the `partial` containing type:

```csharp
public static void Foo<T>(T value)
{
    throw new global::IfItQuacks.DuckShapeMismatchException(typeof(T), typeof(IDoable));
}
```

The call now binds to `Foo<A>`. That overload is never supposed to run - it is replaced in the next steps.

## 2. Shape matching

The generator scans the compilation for invocations of the `[DuckTyped]` method and checks the static type of each argument against the shape. Every shape member needs a public counterpart:

- **Methods**: same name, return type, parameter count, parameter types and ref-kinds (`ref`, `out`, `in`).
- **Properties**: same name and type, with a public getter and/or setter if the shape declares one.

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

The compiler replaces the call to the fallback overload with the interceptor, which wraps the argument and calls your original method. The cast to the interface makes sure overload resolution picks your method and not the generic fallback.

Because the adapter is passed as an interface, each call boxes the adapter. The generated members are trivial forwarders, so they are not marked with `[MethodImpl(MethodImplOptions.AggressiveInlining)]`: calls through an interface can't be inlined by that attribute, and benchmarks showed no difference.

Since the adapter holds a copy of the argument, only classes and `readonly struct`s are supported. See [Known limitations](known_limitations.md#structs) for details.

## Generic methods

A generic `[DuckTyped]` method like `T Unwrap<T>(IContainer<T> c)` can't use the fallback and interceptor from above. An interceptor has to keep the signature of the call it replaces, and a generic fallback `Unwrap<TArg>(TArg value)` can't return `int` for one call and `string` for another.

Instead, the generator infers the method's type arguments per argument type by matching the shape's members against the argument's members, e.g. `int Get()` against `T Get()` gives `T = int`. It then emits one adapter per closed shape (`IContainer<int>`, `IContainer<string>`, ...) and one concrete overload per argument type:

```csharp
public static partial class Ops
{
    public static int Unwrap(global::IntBox value) =>
        Unwrap<int>(new global::IfItQuacks.Generated.ShapeAdapter_IContainer_int__IntBox(value));

    public static string Unwrap(global::Box<string> value) =>
        Unwrap<string>(new global::IfItQuacks.Generated.ShapeAdapter_IContainer_string__Box_string_(value));
}
```

The call `Ops.Unwrap(new IntBox())` binds to the concrete overload, which has the correct return type. Types that already implement the closed shape bind to your method directly, so no overload is generated for them. If the argument's type isn't publicly visible, the overload is generated as `internal`.

A non-generic method with a closed generic shape, e.g. `int Sum(IContainer<int> c)`, uses the regular fallback and interceptor.
