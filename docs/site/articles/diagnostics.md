---
uid: diagnostics
---

# Diagnostics

All diagnostics are reported as errors in the `IfItQuacks` category.

## DUCK001

**Argument does not structurally satisfy duck shape**

The argument's type is missing a member of the shape, or a member has a different signature or no public getter/setter.

```csharp
public class Rock { }

Ops.Foo(new Rock()); // error DUCK001: Type 'Rock' does not structurally satisfy shape 'IDoable'
```

## DUCK002

**Duck-typed method's containing type must be partial**

The generator adds a fallback overload to the containing type, so it (and every enclosing type) has to be `partial`.

## DUCK003

**Duck-typed parameter must be a [DuckShape] interface**

The parameter of a `[DuckTyped]` method is not an interface, or the interface is not decorated with `[DuckShape]`.

## DUCK004

**Unsupported [DuckTyped] method signature**

Reported when the method is an instance method, doesn't have exactly one parameter, or is overloaded by another `[DuckTyped]` method of the same name.

## DUCK005

**Unsupported shape member**

Reserved for shape members that can't be adapted. Only ordinary instance methods and properties are supported.
