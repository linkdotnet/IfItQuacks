# Changelog

All notable changes to **IfItQuacks** will be documented in this file. The project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

<!-- The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) -->

## [Unreleased]

### Added

- `[DuckShape]` and `[DuckTyped]` attributes for compile-time checked structural typing via generated adapters and interceptors.
- Diagnostics `IFITQUACKS001` - `IFITQUACKS006`.
- Generic `[DuckShape]` interfaces and generic `[DuckTyped]` methods, with type arguments inferred per argument type.
- `Duck.As<TShape>(value)` to convert a value explicitly, e.g. to store it in a field or collection. Diagnostic `IFITQUACKS007`.
- `[DuckTyped]` instance methods and methods with several parameters, including multiple `[DuckShape]` parameters, `ref`/`out`, `params`, default values and named arguments.
- Shapes with events, indexers and default interface members. Unsupported shape members are reported with `IFITQUACKS005`.
- Generated types are `internal`, so several projects in a solution can use IfItQuacks.
- Generated overloads are hidden from IntelliSense.
- The generator is incremental and only re-analyzes calls to `[DuckTyped]` methods and `Duck.As`.

[unreleased]: https://github.com/linkdotnet/IfItQuacks/compare/64a9c6e...HEAD
