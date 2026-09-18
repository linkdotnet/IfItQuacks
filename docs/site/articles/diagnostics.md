---
uid: diagnostics
---

# Diagnostics

All diagnostics are reported as errors in the `IfItQuacks` category.

## IFITQUACKS001

**Argument does not structurally satisfy interface**

The argument's type is missing a member of the interface, or a member has an incompatible type (see [Matching an interface](getting_started.md#matching-an-interface)), no public getter/setter or is a `readonly` field where the interface declares a setter. It is also reported for anonymous types passed to generic `[DuckTyped]` methods, and for anonymous types with a property whose type contains another anonymous type as a type argument or array element. For generic `[DuckTyped]` methods this is also reported when the type arguments can't be inferred from the argument. The compiler reports `CS0411` in addition, because no generic fallback overload exists for these methods.

```csharp
public class Rock { }

Ops.Foo(new Rock()); // error IFITQUACKS001: Type 'Rock' does not structurally satisfy 'IDoable'
```

## IFITQUACKS002

**Duck-typed method's containing type must be partial**

The generator adds a fallback overload to the containing type, so it (and every enclosing type) has to be `partial`.

## IFITQUACKS003

**Duck-typed method needs an interface parameter**

None of the parameters of a `[DuckTyped]` method is an interface passed by value.

## IFITQUACKS004

**Unsupported [DuckTyped] method signature**

Reported when

- the containing type (or one of its enclosing types) is generic,
- the method is overloaded by another `[DuckTyped]` method of the same name in the same type,
- the containing type is an interface or a `file`-local type,
- a type parameter of a generic method is not used by any interface parameter (it can't be inferred), or
- the method mixes interface parameters with [duck-typed constraints](getting_started.md#static-members-and-operators), or not all of its type parameters have a single interface constraint used by a parameter.

## IFITQUACKS005

**Unsupported interface member**

The interface contains a member an adapter can't implement: a generic method (`U Map<U>()`), or a `static abstract` member outside a [duck-typed constraint](getting_started.md#static-members-and-operators). For a duck-typed constraint it is reported for an unsupported operator and for instance members whose signature uses the self type. It is reported on every argument that would need an adapter; types implementing the interface are fine.

```csharp
public interface IMapper { U Map<U>(); }

Ops.Run(new Mapper()); // error IFITQUACKS005
```

## IFITQUACKS006

**Unsupported struct argument**

The argument is a mutable struct or a ref struct. A mutable struct would be copied into the adapter, so mutations made through the interface would be lost silently. A ref struct can't be converted to an interface at all. Use a class, a `readonly struct`, or let the struct implement the interface. See [Known limitations](known_limitations.md#structs).

```csharp
public struct Counter { public int Count { get; private set; } public void Increment() => Count++; }

Ops.Bump(new Counter()); // error IFITQUACKS006
```

For ref structs the compiler additionally reports `CS9244`, because the generated fallback overload can't accept a ref struct.

## IFITQUACKS007

**Duck conversion target must be an interface**

`Duck.As`, `Duck.Stub` and `Duck.Merge` only convert to interfaces. This is also reported for type parameters, because the target can't be verified at compile time. `Duck.To` is the one that takes a concrete type.

```csharp
public class Duckling { }

var duckling = Duck.As<Duckling>(new A()); // error IFITQUACKS007
```

## IFITQUACKS008

**Unsupported duck-typed call**

The call itself can't be redirected, whatever its arguments are. So far this is only a `base` call: it invokes the method non-virtually, which neither an interceptor nor the generated fallback overload can reproduce - both would end up in the overriding method.

```csharp
public override string Greet(INamed named) => "derived";
public string ViaBase() => base.Greet(new Person()); // error IFITQUACKS008
```

Pass a value already typed as the interface, or call the method without `base`.

## IFITQUACKS009

**Unsupported Duck.To target**

`Duck.To<TTarget>` can't build the target from the source: the target is abstract, an interface (use [`Duck.As`](getting_started.md#converting-explicitly) instead), has no accessible constructor that the source can fill, or has a member the source has no counterpart for.

```csharp
public record PersonDto(string Name, int Age);
public class OnlyName { public string Name { get; set; } = ""; }

Duck.To<PersonDto>(new OnlyName()); // error IFITQUACKS009: no member of the source fills 'Age'
```
