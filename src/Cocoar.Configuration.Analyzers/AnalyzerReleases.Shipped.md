; Shipped analyzer releases
; See https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 3.0.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
COCFG001 | Cocoar.Configuration | Warning | Secret path conflict detected
COCFG002 | Cocoar.Configuration | Error | Rule dependency ordering violation
COCFG003 | Cocoar.Configuration | Warning | Required rule configuration validation
COCFG004 | Cocoar.Configuration | Error | Configuration accessor type safety violation
COCFG005 | Cocoar.Configuration | Info | Duplicate unconditional rules detected
COCFG006 | Cocoar.Configuration | Info | Static provider ordering suggestion
