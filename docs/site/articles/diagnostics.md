---
uid: diagnostics
---

# Diagnostics

All diagnostics are in the `IfItQuacks` category and reported as errors, except the warning [`IFITQUACKS012`](#ifitquacks012).

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
- the method is overloaded by a generic method with the signature of its generated fallback overload, e.g. `Greet<T>(T value)` next to `Greet(INamed n)`,
- a base type has an accessible method of the same name, which the generated fallback would hide (overload resolution ignores a base type's methods once one of the derived type applies, so `OverloadResolutionPriorityAttribute` can't help); this includes the methods of `object`, e.g. a `[DuckTyped]` method named `Equals`,
- the method is overloaded by a regular method of the same name, but the project uses a language version below C# 13, which is needed for `OverloadResolutionPriorityAttribute` to keep the generated fallback from taking that overload's calls (on .NET 8, set `<LangVersion>13</LangVersion>`),
- the containing type is an interface or a `file`-local type,
- its signature uses a type that isn't accessible to the whole assembly, e.g. a `private` nested interface, or a non-generic method is declared in such a type; the generated interceptors and forwarder can't name it (see [`IFITQUACKS013`](#ifitquacks013)),
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

## IFITQUACKS010

**Mapped shape target must be a partial interface**

`[DuckShape<T>]` fills a second declaration of the interface, so the interface - and every type around it - has to be `partial`. A generic or `file`-local containing type is reported as well.

```csharp
[DuckShape<Customer>]
public interface ICustomerView;   // error IFITQUACKS010: it is not declared 'partial'
```

## IFITQUACKS011

**Unsupported [DuckShape<>] usage**

The mapping can't be applied: `Pick` and `Omit` are both set, a name in `Pick` or `Omit` is not a public instance member of the source, the source is not a named class, struct, record or interface, the mapping derives no member at all, or a derived member's type is less accessible than the interface.

```csharp
[DuckShape<Customer>(Omit = ["Nope"])]
public partial interface ICustomerView;   // error IFITQUACKS011: 'Nope' is not a public instance member of the source
```

See [Mapped shapes](mapped_shapes.md).

## IFITQUACKS012

**Duck-typed parameter is cast to a concrete type** (warning)

An argument that only matches the interface structurally is wrapped in a generated adapter, and that adapter is what your method receives. With a [duck-typed constraint](constrained_duck_typing.md), `T` is the adapter type itself. A cast, `as` or type pattern on the parameter for a concrete type, typically the caller's own type, therefore fails for these calls. It succeeds for arguments that implement the interface, so the method behaves differently depending on the caller.

```csharp
[DuckTyped]
public static string Describe(INamed named) =>
    named is Person p ? p.Nickname : named.Name; // warning IFITQUACKS012: Whenever the argument doesn't implement 'INamed' itself,
                                                 // 'named' receives a generated adapter instead of the caller's instance, so this
                                                 // type test for 'Person' never succeeds for such calls; use 'Duck.Unwrap(named)'
                                                 // to get the original instance
```

Conversion operators can't bridge this: C# doesn't allow user-defined conversions from an interface, and inside a generic method the compiler doesn't know the adapter type. Test the original instance instead:

```csharp
Duck.Unwrap(named) is Person p ? p.Nickname : named.Name;
```

Casts and type tests for interfaces and type parameters are not reported, because the adapter may implement those.

## IFITQUACKS013

**Type is not accessible to generated code**

Adapters and interceptors are generated in their own namespace, outside your types, so they can only name types the whole assembly can access. A `private`, `protected` or `private protected` nested type, a type nested in one, and a `file`-local type can't be named there. This is reported for the argument of a `[DuckTyped]` call, a method group, a duck-typed constraint, `Duck.As`, `Duck.Stub`, `Duck.Merge` and `Duck.To`, including element types of sequences like `List<Hidden>`, and for an inaccessible interface or `Duck.To` target.

```csharp
public class Host
{
    private class Hidden { public string Name => "hidden"; }

    public static string Run() => Ops.Greet(new Hidden()); // error IFITQUACKS013: Type 'Host.Hidden' cannot be duck-typed to 'INamed':
                                                           // 'Host.Hidden' is private, so the generated code can't name it;
                                                           // declare it 'internal' or 'public'
}
```

Declare the type `internal`, or make it implement the interface. An argument implementing the interface itself needs no adapter, so it is only reported where a generated signature names it, e.g. next to an argument that is adapted.
