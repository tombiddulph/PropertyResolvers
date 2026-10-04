---
title: Diagnostics
description: Understand PR001–PR010 and correct generation problems.
---

| ID | Severity | Meaning / action |
|---|---|---|
| PR001 | Error | Duplicate local canonical configuration; remove the redundant attribute |
| PR002 | Warning | No eligible matches; check property spelling and visibility |
| PR003 | Error | Typed result incompatible or uninferable; align types or select String mode |
| PR004 | Error | Invalid identifier/path; use unescaped dot-separated identifiers |
| PR005 | Warning | Unsupported type, property, or path shape prevents resolution |
| PR006 | Warning | Namespace rules exclude all matches or all include rules |
| PR007 | Error | Invalid generated namespace; set a valid Namespace |
| PR008 | Error | Type, namespace, or generated-member name collision; change names |
| PR009 | Error | Multiple case-insensitive or alias matches; make selection unambiguous |
| PR010 | Error | Invalid output mode or project default |

Diagnostics point to the local assembly attribute when available. Metadata-only configurations have no local source location. Invalid configurations are skipped without suppressing unrelated valid resolvers. No-match string resolvers remain callable, returning null/false.

The PR001 IDE code fix removes the duplicate attribute and supports Fix All. Other naming and output decisions are intentionally not guessed.

Configure severity through normal Roslyn `.editorconfig` controls. For an intentionally unmatched string resolver:

```ini
[*.cs]
dotnet_diagnostic.PR002.severity = none
```

Prefer correcting unexpected warnings rather than hiding them. Projects with warnings-as-errors may fail on no-match or unsupported-shape warnings.
