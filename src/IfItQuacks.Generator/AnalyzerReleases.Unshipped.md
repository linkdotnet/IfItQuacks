; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

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
