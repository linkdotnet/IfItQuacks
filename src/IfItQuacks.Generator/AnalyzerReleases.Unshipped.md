; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DUCK001 | IfItQuacks | Error | Argument does not structurally satisfy duck shape
DUCK002 | IfItQuacks | Error | Duck-typed method's containing type must be partial
DUCK003 | IfItQuacks | Error | Duck-typed parameter must be a [DuckShape] interface
DUCK004 | IfItQuacks | Error | Unsupported [DuckTyped] method signature
DUCK005 | IfItQuacks | Error | Unsupported shape member
