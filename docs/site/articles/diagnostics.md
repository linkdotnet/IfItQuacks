---
uid: diagnostics
---

# Diagnostics

All diagnostics are reported as errors in the `IfItQuacks` category.

## IFITQUACKS001

**Argument does not structurally satisfy duck shape**

The argument's type is missing a member of the shape, or a member has a different signature or no public getter/setter.

```csharp
public class Rock { }

Ops.Foo(new Rock()); // error IFITQUACKS001: Type 'Rock' does not structurally satisfy shape 'IDoable'
```

## IFITQUACKS002

**Duck-typed method's containing type must be partial**

The generator adds a fallback overload to the containing type, so it (and every enclosing type) has to be `partial`.

## IFITQUACKS003

**Duck-typed parameter must be a [DuckShape] interface**

The parameter of a `[DuckTyped]` method is not an interface, or the interface is not decorated with `[DuckShape]`.

## IFITQUACKS004

**Unsupported [DuckTyped] method signature**

Reported when the method is an instance method, doesn't have exactly one parameter, the parameter is declared `ref`, `in`, `out` or `ref readonly`, or the method is overloaded by another `[DuckTyped]` method of the same name.

## IFITQUACKS005

**Unsupported shape member**

Reserved for shape members that can't be adapted. Only ordinary instance methods and properties are supported.

## IFITQUACKS006

**Unsupported struct argument**

The argument is a mutable struct or a ref struct. A mutable struct would be copied into the adapter, so mutations made through the shape would be lost silently. A ref struct can't be converted to an interface at all. Use a class, a `readonly struct`, or let the struct implement the shape interface. See [Known limitations](known_limitations.md#structs).

```csharp
public struct Counter { public int Count { get; private set; } public void Increment() => Count++; }

Ops.Bump(new Counter()); // error IFITQUACKS006
```

For ref structs the compiler additionally reports `CS9244`, because the generated fallback overload can't accept a ref struct.
