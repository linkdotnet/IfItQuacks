---
uid: mapped_shapes
---

# Mapped shapes

TypeScript can build a type out of another one - `Pick<Customer, "Id" | "Name">`, `Omit<Customer, "PasswordHash">`,
`Partial<Customer>`, `Readonly<Customer>`, `A & B`. C# has no such thing, and adding it on its own
wouldn't help much: a derived interface that nothing implements is dead weight.

Under IfItQuacks it isn't, because matching is structural. Mark a `partial interface` with
`[DuckShape<TSource>]` and the generator fills it with members derived from the source - and every type
carrying those members satisfies it, including the source itself, a DTO, and an object literal in a test.

```csharp
public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}

[DuckShape<Customer>(Omit = [nameof(Customer.PasswordHash)], Readonly = true)]
public partial interface ICustomerView;
```

The generator emits:

```csharp
public partial interface ICustomerView
{
    int Id { get; }
    string Name { get; }
    string Email { get; }
}
```

`Customer` doesn't implement `ICustomerView` and never has to:

```csharp
[DuckTyped]
public static string Render(ICustomerView view) => $"{view.Id} {view.Name} <{view.Email}>";

Render(new Customer());                                   // the entity
Render(new CustomerRow(2, "Donald", "d@example.com"));    // a DTO
Render(new { Id = 3, Name = "Daisy", Email = "" });       // an object literal
```

## The options

| Option | TypeScript | What it does |
|---|---|---|
| `Pick` | `Pick<T, K>` | Derives only the named members. |
| `Omit` | `Omit<T, K>` | Derives every member except the named ones. |
| `Optional` | `Partial<T>` | Makes every derived property nullable. |
| `Readonly` | `Readonly<T>` | Drops the setters of the derived properties and indexers. |
| `IncludeMethods` | - | Derives methods, events and indexers as well. |
| several attributes | `A & B` | Intersects: every member of every source is derived into the same interface. |

`Pick` and `Omit` are mutually exclusive, and both take member names - use `nameof` so a rename keeps
working. An unknown name is a build error rather than a silently smaller interface.

```csharp
[DuckShape<Customer>(Pick = [nameof(Customer.Id), nameof(Customer.Name)], Readonly = true)]
public partial interface ICustomerKey;

[DuckShape<Customer>(Omit = [nameof(Customer.PasswordHash)], Optional = true, Readonly = true)]
public partial interface ICustomerPatch;      // int? Id, string? Name, string? Email

[DuckShape<IReadable>(IncludeMethods = true)]
[DuckShape<IWritable>(IncludeMethods = true)]
public partial interface ITextPort;           // string Read(); void Write(string value);
```

`Optional` widens the *declared* member and matching follows: `ICustomerPatch` is satisfied both by a
`Customer` (whose `Id` is `int`, which converts to `int?`) and by a partial payload
(`new { Id = (int?)null, Name = "Donald", Email = (string?)null }`).

## What it is good for

- **Views and redaction.** `Duck.As<ICustomerView>(customer)` hands out a live view with the sensitive
  members simply absent from the type - no DTO, no copy, no mapping code.
- **Response shaping.** One entity, several derived interfaces, each one a documented contract.
- **Tests.** An object literal satisfies a derived shape, so a `Pick` of the three members a test cares
  about is all the fixture it needs.
- **Intersections.** C# can't express `IReadable & IWritable` without declaring a type that implements
  both; here the intersection is a shape anything fitting both satisfies.

## Interaction with the rest of IfItQuacks

A derived interface is an ordinary interface, so everything works on it: `[DuckTyped]` parameters,
[constrained duck typing](constrained_duck_typing.md), `Duck.As`, `Duck.Stub`, `Duck.Merge` and
`Duck.To`.

You can add members by hand; they are kept as written and not derived a second time.

```csharp
[DuckShape<Customer>(Pick = [nameof(Customer.Name)])]
public partial interface ICustomerName
{
    string Display { get; }   // yours, kept as is
}
```

## Limits

- The target must be a `partial interface`, and so must every type around it
  ([`IFITQUACKS010`](diagnostics.md#ifitquacks010)).
- **Public fields of the source are not derived.** Only properties are, plus methods, events and
  indexers under `IncludeMethods`. (A field on the *argument* still satisfies a derived property, as
  everywhere else in IfItQuacks.)
- Static members, generic methods and `init`-only setters are not derived.
- A member whose type is less accessible than the interface is reported
  ([`IFITQUACKS011`](diagnostics.md#ifitquacks011)), as is an unknown `Pick`/`Omit` name, `Pick` and
  `Omit` together, and a mapping that derives nothing.
- Nested shaping is not done: a derived `Address` member keeps its own type, it does not become an
  `IAddressView`.
- `[DuckShape<T>]` is a generic attribute, so the consuming project needs C# 11 or later.

Runnable examples live in [`samples/IfItQuacks.Sample.MappedShapes`](https://github.com/linkdotnet/IfItQuacks/tree/main/samples/IfItQuacks.Sample.MappedShapes).
