---
uid: diagnostics
---

# Diagnostics

All diagnostics are reported as errors in the `IfItQuacks` category.

## IFITQUACKS001

**Argument does not structurally satisfy duck shape**

The argument's type is missing a member of the shape, or a member has a different signature or no public getter/setter. For generic `[DuckTyped]` methods this is also reported when the type arguments can't be inferred from the argument. The compiler reports `CS0411` in addition, because no generic fallback overload exists for these methods.

```csharp
public class Rock { }

Ops.Foo(new Rock()); // error IFITQUACKS001: Type 'Rock' does not structurally satisfy shape 'IDoable'
```

## IFITQUACKS002

**Duck-typed method's containing type must be partial**

The generator adds a fallback overload to the containing type, so it (and every enclosing type) has to be `partial`.

## IFITQUACKS003

**Duck-typed method needs a [DuckShape] parameter**

None of the parameters of a `[DuckTyped]` method is an interface decorated with `[DuckShape]`.

## IFITQUACKS004

**Unsupported [DuckTyped] method signature**

Reported when

- a `[DuckShape]` parameter is declared `ref`, `in`, `out` or `ref readonly`,
- the method is an extension method, or an instance method of a struct,
- the method is `private` or `protected`, so the generated interceptors can't call it,
- the containing type (or one of its enclosing types) is generic,
- the method is overloaded by another `[DuckTyped]` method of the same name in the same type, or
- a type parameter of a generic method is not used by any `[DuckShape]` parameter (it can't be inferred).

## IFITQUACKS005

**Unsupported shape member**

The shape contains a member the adapter can't implement: a generic method (`U Map<U>()`), a member returning by `ref`, or a `static abstract` member. It is reported on every argument passed to that shape.

```csharp
[DuckShape]
public interface IMapper { U Map<U>(); }

Ops.Run(new Mapper()); // error IFITQUACKS005
```

## IFITQUACKS006

**Unsupported struct argument**

The argument is a mutable struct or a ref struct. A mutable struct would be copied into the adapter, so mutations made through the shape would be lost silently. A ref struct can't be converted to an interface at all. Use a class, a `readonly struct`, or let the struct implement the shape interface. See [Known limitations](known_limitations.md#structs).

```csharp
public struct Counter { public int Count { get; private set; } public void Increment() => Count++; }

Ops.Bump(new Counter()); // error IFITQUACKS006
```

For ref structs the compiler additionally reports `CS9244`, because the generated fallback overload can't accept a ref struct.

## IFITQUACKS007

**Duck.As type argument must be a [DuckShape] interface**

`Duck.As<TShape>` only converts to interfaces decorated with `[DuckShape]`. This is also reported for type parameters, because the target can't be verified at compile time.

```csharp
public interface IDoable { void Do(); }

var doable = Duck.As<IDoable>(new A()); // error IFITQUACKS007
```
