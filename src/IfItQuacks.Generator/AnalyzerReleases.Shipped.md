; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md


## Release 1.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
IFITQUACKS001 | IfItQuacks | Error | Argument does not structurally satisfy interface
IFITQUACKS002 | IfItQuacks | Error | Duck-typed method's containing type must be partial
IFITQUACKS003 | IfItQuacks | Error | Duck-typed method needs an interface parameter
IFITQUACKS004 | IfItQuacks | Error | Unsupported [DuckTyped] method signature
IFITQUACKS005 | IfItQuacks | Error | Unsupported shape member
IFITQUACKS006 | IfItQuacks | Error | Unsupported struct argument
IFITQUACKS007 | IfItQuacks | Error | Duck.As type argument must be an interface

## Release 1.3.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
IFITQUACKS008 | IfItQuacks | Error | Unsupported duck-typed call
IFITQUACKS009 | IfItQuacks | Error | Unsupported Duck.To target

## Release 1.4.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
IFITQUACKS010 | IfItQuacks | Error | Mapped shape target must be a partial interface
IFITQUACKS011 | IfItQuacks | Error | Unsupported [DuckShape<>] usage
