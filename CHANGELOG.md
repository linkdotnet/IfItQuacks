# Changelog

All notable changes to **IfItQuacks** will be documented in this file. The project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

<!-- The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) -->

## [Unreleased]

## [v1.5.3] - 2026-09-25

### Fixed

- An argument whose type is a `private`, `protected` or `private protected` nested type failed the build with `CS0122` in
  generated code. Its adapter is now nested in the type declaring it, which has to be `partial`; otherwise the call is
  reported with `IFITQUACKS001`. Reported by [@robertodalmonte](https://github.com/robertodalmonte) in #3
- A `[DuckTyped]` override of a method that isn't `[DuckTyped]` was reported with `IFITQUACKS004` (and, before v1.5.2,
  overflowed the stack at runtime). It is now supported; a `protected` override is still reported.
- Calling a `protected` `[DuckTyped]` method from a derived type that overrides it threw `DuckTypeMismatchException`.

## [v1.5.2] - 2026-09-24

### Fixed

- A `[DuckTyped]` method overloaded by a generic method with the signature of its generated fallback, e.g. `Greet<T>(T value)`
  next to `Greet(INamed n)`, failed the build with `CS0111` in generated code. It is now reported with `IFITQUACKS004`.
- A `[DuckTyped]` method sharing its name with a method of a base type, e.g. an inherited `Greet(object)` or
  `object.Equals`, built without a diagnostic, but the generated fallback hid the inherited method and every call threw
  `DuckTypeMismatchException`, including the ones meant for the inherited method. It is now reported with `IFITQUACKS004`.

## [v1.5.1] - 2026-09-24

## [v1.5.0] - 2026-09-22

### Added

- `IFITQUACKS012` warns when a `[DuckTyped]` method casts or type-tests a duck-typed parameter for a concrete type. Such
  checks fail whenever the argument was adapted, because the method receives the adapter; use `Duck.Unwrap` instead.

### Changed

- **Breaking:** an adapter no longer equals the instance it wraps. `Duck.As<INamed>(person).Equals(person)` was true
  while `person.Equals(...)` was false, so a collection holding both depended on insertion order. Adapters of the same
  type wrapping equal values are still equal and hash alike; `ToString` is still forwarded. Compare with the original
  through `Duck.Unwrap`.

### Fixed

- A `[DuckTyped]` method overloaded by a regular method, e.g. `Greet(object)`, threw `DuckTypeMismatchException`. Calls
  now go to that overload unless the argument matches the interface structurally. Requires C# 13, `IFITQUACKS004`
  otherwise. Reported by [@robertodalmonte](https://github.com/robertodalmonte) in #1

## [v1.4.2] - 2026-09-22

### Changed

- **Minimum toolchain is now Visual Studio 2022 17.13 or .NET SDK 9.0.200** (Roslyn 4.13), where interceptors became
  stable. The generator already required the .NET 9 SDK's `InterceptorsNamespaces` switch, so .NET 8 SDKs were not
  supported before either.

## [v1.4.1] - 2026-09-22

### Changed

- `Duck.Merge` adapters compare and hash over every merged value instead of only the first, so
  `Merge(new { Value = 1 }, new { Test = 2 })` no longer equals `Merge(new { Value = 1 }, new { Test = 3 })`.
- `Duck.Merge` takes a member from the value that implements the member's interface before falling back to the first
  value providing it structurally. Interfaces sharing a member name are routed to their own implementer, and explicit
  interface implementations are used.

## [v1.4.0] - 2026-09-20

### Added

- **Mapped shapes.** `[DuckShape<TSource>]` fills a `partial interface` with members derived from another
  type - `Pick`, `Omit`, `Optional` (TypeScript's `Partial`), `Readonly` and, by applying the attribute
  several times, intersections. Because matching is structural, the derived interface is satisfied by the
  source, by a DTO and by an object literal alike. Diagnostics `IFITQUACKS010` and `IFITQUACKS011`.

- Sample `IfItQuacks.Sample.MappedShapes` and a [Mapped shapes](docs/site/articles/mapped_shapes.md) article.

- **Constrained duck typing.** A `[DuckTyped]` method can take its duck type as a constrained type
  parameter (`[DuckTyped] static int Describe<T>(T person) where T : IPerson`). The generated overload
  passes the adapter as the _type argument_ instead of an interface, so nothing is boxed and the runtime
  specializes the body per shape: `562 ns` and 0 bytes per 1000 calls against `3,694 ns` and 24,000 B for
  the interface parameter, and `3,005 ns` against `10,459 ns` when three shapes share one method. This
  mode previously existed only for `static abstract` members and generic math.

- `[DuckTyped]` **extension methods with a constrained receiver** (`static string Shout<T>(this T named) where T : INamed`).

- A duck-typed constraint may now sit **next to ordinary interface parameters**
  (`static string Label<T>(T priced, ILog log) where T : IPriced`). Previously `IFITQUACKS004`.

- Sample `IfItQuacks.Sample.Constraints` and a [Constrained duck typing](docs/site/articles/constrained_duck_typing.md) article.

### Fixed

- An anonymous type passed to a duck-typed constraint produced uncompilable code. The generated overload
  has to name the argument's type, which an anonymous type has none for, so it is reported with
  `IFITQUACKS001` instead.

## [v1.3.0] - 2026-09-18

### Added

- `[DuckTyped]` extension methods: the receiver is duck-typed too, so `person.Greet()` works for any type fitting the interface. Previously reported with `IFITQUACKS004`.

- A public property or field of a delegate type satisfies an interface **method** of the same name, which makes an object literal a test double: `Duck.As<IRepository>(new { Find = (Func<int, Order?>)(id => ...) })`.

- `Duck.Stub<TShape>(value)` and `Duck.Stub<TShape>()` return a test double that forwards the members the value provides and throws `DuckStubException` for the rest. A member that is present but doesn't fit is still reported with `IFITQUACKS001`.

- `Duck.Merge<TShape>(first, second)` (and a three-value overload) builds one interface from several values, taking each member from the first value providing it - a spy or a partial fake without a mocking library.

- Sequences are adapted element by element: `IEnumerable<T>`, `IReadOnlyCollection<T>` and `IReadOnlyList<T>` parameters accept a `List<Person>` or a `Person[]` whose elements fit, and `Duck.As<IReadOnlyList<INamed>>(people)` keeps `Count` and the indexer.

- `Duck.To<TTarget>(value)` copies a value into a new one of a concrete type, filling the constructor and the remaining settable members from the members of the same name. Diagnostic `IFITQUACKS009`.

- Samples: `IfItQuacks.Sample.Extensions`, `IfItQuacks.Sample.Sequences`, `IfItQuacks.Sample.Testing` and `IfItQuacks.Sample.Copying`.

### Fixed

- `greeter?.Greet(duck)` is intercepted. A conditional access used to fall through to the runtime fallback and throw `DuckTypeMismatchException` although the call was right there in the project.

- `base.Greet(duck)` no longer silently calls the overriding method. A `base` call is not virtual, which neither an interceptor nor the fallback overload can reproduce, so it is reported with the new `IFITQUACKS008`.

- An argument typed as `Person?` shares the adapter generated for `Person` instead of getting a second one whose generated code warned about a possible null dereference.

### Changed

- `IFITQUACKS007` is titled _Duck conversion target must be an interface_ and names the conversion (`Duck.As`, `Duck.Stub` or `Duck.Merge`) that was called.

## [v1.2.0] - 2026-09-17

### Added

- Delegates, lambdas and method groups satisfy an interface with a single method (a functional interface), both as `[DuckTyped]` arguments and through `Duck.As`.

- A `[DuckTyped]` method converts to a delegate over the duck type (`Func<Person, string> f = Ops.Describe;`, `items.Select(Ops.Describe)`) through a generated overload.

- `static abstract` members and operators via duck-typed constraints (`where T : IAddable<T>`), including BCL generic math interfaces such as `IAdditionOperators<T, T, T>`. The generated adapter is its own type argument, so these calls don't box.

- Samples: `IfItQuacks.Sample.Delegates` and `IfItQuacks.Sample.GenericMath`.

- Documentation: a [Benchmarks](docs/site/articles/benchmarks.md) page with measured numbers (BenchmarkDotNet, `MemoryDiagnoser`) for duck-typed calls and for `Duck.As` used as a view instead of a mapper.

- Samples: `IfItQuacks.Sample.Members` (events, indexers, `ref` returns, default interface members) and `IfItQuacks.Sample.Signatures` (`ref`/`out`/`params` parameters, `private` methods, methods on a struct, runtime fallback).

### Changed

- Documentation: _Known limitations_ is grouped into fewer sections, each showing the code that doesn't work.

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

[unreleased]: https://github.com/linkdotnet/IfItQuacks/compare/v1.5.3...HEAD
[v1.5.3]: https://github.com/linkdotnet/IfItQuacks/compare/v1.5.2...v1.5.3
[v1.5.2]: https://github.com/linkdotnet/IfItQuacks/compare/v1.5.1...v1.5.2
[v1.5.1]: https://github.com/linkdotnet/IfItQuacks/compare/v1.5.0...v1.5.1
[v1.5.0]: https://github.com/linkdotnet/IfItQuacks/compare/v1.4.2...v1.5.0
[v1.4.2]: https://github.com/linkdotnet/IfItQuacks/compare/v1.4.1...v1.4.2
[v1.4.1]: https://github.com/linkdotnet/IfItQuacks/compare/v1.4.0...v1.4.1
[v1.4.0]: https://github.com/linkdotnet/IfItQuacks/compare/v1.3.0...v1.4.0
[v1.3.0]: https://github.com/linkdotnet/IfItQuacks/compare/v1.2.0...v1.3.0
[v1.2.0]: https://github.com/linkdotnet/IfItQuacks/compare/v1.1.0...v1.2.0
[v1.1.0]: https://github.com/linkdotnet/IfItQuacks/compare/v1.0.0...v1.1.0
[v1.0.0]: https://github.com/linkdotnet/IfItQuacks/compare/64a9c6ea49de0a82dd1cd59556dbaebca74381cb...v1.0.0
