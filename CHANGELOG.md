# Changelog

All notable changes to **IfItQuacks** will be documented in this file. The project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

<!-- The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) -->

## [Unreleased]

### Added

- `[DuckShape]` and `[DuckTyped]` attributes for compile-time checked structural typing via generated adapters and interceptors.
- Diagnostics `IFITQUACKS001` - `IFITQUACKS006`.
- Generic `[DuckShape]` interfaces and generic `[DuckTyped]` methods, with type arguments inferred per argument type.
- `Duck.As<TShape>(value)` to convert a value explicitly, e.g. to store it in a field or collection. Diagnostic `IFITQUACKS007`.

[unreleased]: https://github.com/linkdotnet/IfItQuacks/compare/64a9c6e...HEAD
