---
uid: benchmarks
---

# Benchmarks

Numbers you can expect in the usual scenarios, measured with [BenchmarkDotNet](https://benchmarkdotnet.org/) and `MemoryDiagnoser`. The benchmarks live in `benchmarks/IfItQuacks.Benchmarks` and are not part of the solution, so you can re-run them with:

```bash
dotnet run --project benchmarks/IfItQuacks.Benchmarks -c Release -- --filter '*'
```

Each benchmark walks the same **1000 items**, so the tables are per 1000 calls. Micro-benchmarking a single forwarding call is pointless: the JIT folds it away.

```text
BenchmarkDotNet v0.15.8, macOS 27.0 (26A428) [Darwin 27.0.0]
Apple M2 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.400, Arm64 RyuJIT armv8.0-a
```

## Calling a `[DuckTyped]` method

The method is `int Describe(IPerson person) => person.Name.Length + person.Age`, called 1000 times.

| Scenario | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Concrete parameter (no interface) | 590 ns | 1.00 | - |
| Interface parameter, hand-written implementation | 588 ns | 1.00 | - |
| `[DuckTyped]`, argument implements the interface | 586 ns | 0.99 | - |
| `[DuckTyped]`, class argument | 3,920 ns | 6.65 | 24,000 B |
| `[DuckTyped]`, `readonly struct` argument | 5,023 ns | 8.52 | 40,000 B |

What this says:

- **An argument that already implements the interface costs nothing.** The generator passes it through, so you get the same code you would have written by hand. Mixing real implementations and ducks in the same API is free for the implementations.
- **A duck-typed argument costs about 3 ns and 24 bytes per call** on this machine: one boxed adapter plus an interface call that can't be inlined. For the `readonly struct` the box holds a copy of the value, hence 40 bytes here.
- The ratio looks dramatic because the method itself does almost nothing. In absolute terms you are paying a few nanoseconds and one small object per call.

## `Duck.As` as a view (the "mapper" case)

A common use is exposing an entity through a narrower interface instead of copying it into a DTO:

```csharp
public interface ICustomerView { int Id { get; } string Name { get; } string Email { get; } }

// A view: no copy, InternalNotes stays invisible.
ICustomerView view = Duck.As<ICustomerView>(customer);

// The DTO alternative, by hand.
ICustomerView dto = new CustomerDto(customer.Id, customer.Name, customer.Email);
```

Reading three members from 1000 customers:

| Scenario | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Read the entity directly | 1.66 us | 1.00 | - |
| Read through existing DTOs | 1.90 us | 1.15 | - |
| Read through existing views | 1.96 us | 1.18 | - |
| `Duck.As`, then read | 4.97 us | 3.01 | 24,000 B |
| Copy into a DTO, then read | 5.07 us | 3.06 | 40,000 B |
| `Duck.As`, kept (escapes) | 7.94 us | 4.80 | 24,000 B |
| Copy into DTOs, kept (escapes) | 11.26 us | 6.81 | 40,000 B |

What this says:

- **As a "mapper", IfItQuacks is as fast as hand-written DTO copying and allocates less** (24 bytes per adapter against 40 for a three-member record) - and it stays in sync with the entity, because it forwards instead of copying.
- **Reading through a view costs ~15-20% over touching the entity directly**, the same as reading through any interface. That is the price of the interface call, not of IfItQuacks.
- **Creating the view dominates.** If you read a few members once, the conversion is the expensive part; if you keep the view and read it repeatedly, the per-read cost is what the third row shows.
- It is not a mapper, though: a view forwards to the live entity and can't rename members, flatten nested objects or convert types. See [Known limitations](known_limitations.md#members-match-by-name-and-assignable-type).

## Rules of thumb

- Real implementations are free; only structurally matched arguments allocate.
- Prefer classes over structs on hot paths: the adapter copies a struct into the box.
- Hoist `Duck.As` out of loops when you can - convert once, read many times.
- Nothing here matters at the scale of I/O, logging or serialization. If a duck-typed call sits in a tight numeric loop, take the interface out of that loop instead.
- The JIT *can* remove the box when the adapter never leaves your method, and in small methods it does - the same benchmarks written as a single call per invocation measured 0 bytes. In the loops above it did not, so the 24 bytes per item are what you should plan for. Storing the view (*"kept (escapes)"*) adds the GC work on top. See [Allocations](concepts.md#allocations).
