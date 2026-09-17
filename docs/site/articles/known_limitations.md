---
uid: known_limitations
---

# Known limitations

IfItQuacks is intentionally narrow. The following scenarios are currently not supported:

- **Signature**: `[DuckTyped]` methods must be `static`, have exactly one parameter and must not be overloaded.
- **Exact matching**: member types are compared exactly. There is no support for variance or implicit conversions.
- **Static type only**: matching uses the argument's compile-time type. Passing an open generic type parameter (for example from inside a generic method) can't be verified; the call ends up in the generated fallback, which throws `DuckShapeMismatchException` at runtime.
- **Same compilation**: only calls inside the project referencing the generator are intercepted. Calls from other assemblies bind to the fallback and throw.
- **Direct invocations**: only direct calls like `Ops.Foo(x)` or `Foo(x)` are intercepted, not method groups or delegates.
- **Boxing**: adapters are structs passed as interfaces, so every intercepted call allocates.
