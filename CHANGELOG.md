# Changelog

All notable changes to **IfItQuacks** will be documented in this file. The project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

<!-- The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) -->

## [Unreleased]

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

[unreleased]: https://github.com/linkdotnet/IfItQuacks/compare/64a9c6e...HEAD
