---
title: Supported shapes
description: Discovery rules, inheritance, unsupported property shapes, and incremental caching.
---

Discovery includes source types and public, globally accessible referenced types.

## Supported

- Classes, structs, records, record structs, and partial types.
- Accessible nested types and inherited properties.
- Non-generic roots inheriting constructed generic bases.
- Nested paths through constructed generic and interface property types.
- Nullable references and nullable structs.

Derived type patterns precede base patterns. Overrides and `new` hiding are respected. A hidden base property is not selected through a derived declaration, although a participating base pattern may still match that instance.

## Excluded

Open generic roots, static classes, file-local types, ref struct roots, inaccessible types/getters, static properties, indexers, and pointer/function-pointer/ref-like values. Top-level internal types are not roots; this preserves the original public-root policy.

Extern-alias-only types cannot be roots. Their property values can be formatted in String mode, but Typed mode diagnoses an unbindable public signature. Ambiguous or shadowed metadata names are not emitted blindly.

## Incremental generation

Source discovery uses semantic syntax candidates and value-equatable models. Metadata discovery is cached by reference and matching configuration. Generated sources are independently cached, so unrelated method/getter-body edits do not regenerate resolvers.

Relevant property, inheritance, alias, reference, and configuration changes invalidate their results. The repository tests incremental tracking and measures cold, unrelated-edit, and relevant-edit generation over 1,000 synthetic types. These observations are not a claim that every workload is faster than reflection.
