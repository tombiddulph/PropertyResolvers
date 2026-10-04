; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PR001 | Usage | Error | DuplicatePropertyResolverAnalyzer
PR002 | Usage | Warning | No eligible property matches
PR003 | Usage | Error | Incompatible typed resolver properties
PR004 | Usage | Error | Invalid property resolver name
PR005 | Usage | Warning | Unsupported property or type
PR006 | Usage | Warning | Namespace rules exclude all matches
PR007 | Usage | Error | Invalid generated namespace
PR008 | Usage | Error | Generated name conflicts with an existing declaration
PR009 | Usage | Error | Ambiguous structural property match
PR010 | Usage | Error | Invalid resolver configuration
