# Changelog

All notable changes to **IfItQuacks** will be documented in this file. The project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

<!-- The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) -->

## [Unreleased]

### Added

- Documentation: a [Benchmarks](docs/site/articles/benchmarks.md) page with measured numbers (BenchmarkDotNet, `MemoryDiagnoser`) for duck-typed calls and for `Duck.As` used as a view instead of a mapper.
- Samples: `IfItQuacks.Sample.Members` (events, indexers, `ref` returns, default interface members) and `IfItQuacks.Sample.Signatures` (`ref`/`out`/`params` parameters, `private` methods, methods on a struct, runtime fallback).

### Changed

- Documentation: *Known limitations* is grouped into fewer sections, each showing the code that doesn't work.

### Fixed

- `IFITQUACKS005` no longer mentions `ref` returns, which are supported now, and its title matches the documentation.
- Interface properties and indexers with an `init` setter are adapted correctly. A type whose setter is `init`-only is reported as a mismatch instead of producing invalid code.
- `[DuckTyped]` methods in an interface or in a `file`-local type are reported with `IFITQUACKS004` instead of silently generating code for a different type. `readonly` and `ref` struct containing types keep their modifiers.
- Members inherited from base interfaces are found when the argument is statically typed as an interface.
- A member of the argument's type that hides the inherited member the interface was matched against no longer breaks the generated adapter.
- Type argument inference skips static and generic candidate members and honours `ref` returns, matching the rules used for structural matching.

## [v1.1.0] - 2026-09-17

### Added

- `[DuckTyped]` instance methods on structs. The receiver is passed by reference, so mutations reach the caller's value.
- `private`, `protected` and `private protected` `[DuckTyped]` methods.
- Interface members returning by `ref` or `ref readonly` (methods, properties and indexers) can be adapted if the matching member returns the same type by reference.

## [v1.0.0] - 2026-09-17

### Added

- `[DuckTyped]` attribute for compile-time checked structural typing of interface parameters via generated adapters and interceptors. Any interface works, no attribute on the interface needed.
- Diagnostics `IFITQUACKS001` - `IFITQUACKS006`.
- Generic interfaces and generic `[DuckTyped]` methods, with type arguments inferred per argument type.
- `Duck.As<TShape>(value)` to convert a value explicitly, e.g. to store it in a field or collection. Diagnostic `IFITQUACKS007`.
- `[DuckTyped]` instance methods and methods with several parameters, including multiple interface parameters, `ref`/`out`, `params`, default values and named arguments.
- Interfaces with events, indexers and default interface members. Unsupported interface members are reported with `IFITQUACKS005`.
- Framework interfaces like `IDisposable` and `IEnumerable<T>`; types implementing an interface (also explicitly or through variance) are passed through without an adapter.
- Interface parameters accept `null`, `default` and omitted default values.
- Calls that can't be verified at compile time (e.g. from generic code) work at runtime if the value implements the interface, otherwise `DuckTypeMismatchException` is thrown.
- Generated types are `internal`, so several projects in a solution can use IfItQuacks.
- Generated overloads are hidden from IntelliSense.
- The generator is incremental and only re-analyzes calls to `[DuckTyped]` methods and `Duck.As`.
- The package enables `InterceptorsNamespaces` for `IfItQuacks.Generated`, so no project file changes are needed.
- Member types only need to be assignable (covariant returns and getters, contravariant parameters and setters, `void` interface methods discard results) instead of identical.
- Public fields satisfy interface properties.
- Anonymous types can be passed to `[DuckTyped]` methods and `Duck.As`.
- Adapters forward `Equals`, `GetHashCode` and `ToString` to the wrapped instance; `Duck.Unwrap` returns it.

[unreleased]: https://github.com/linkdotnet/IfItQuacks/compare/v1.1.0...HEAD
[v1.1.0]: https://github.com/linkdotnet/IfItQuacks/compare/v1.0.0...v1.1.0
[v1.0.0]: https://github.com/linkdotnet/IfItQuacks/compare/64a9c6ea49de0a82dd1cd59556dbaebca74381cb...v1.0.0
