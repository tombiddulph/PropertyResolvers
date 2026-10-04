---
title: Aliases and nested paths
description: Resolve conceptual properties across legacy names and nested model shapes.
---

## Alternative names

```csharp
[assembly: GeneratePropertyResolver("AccountId",
    Aliases = new[] { "AccountID", "AccountNumber" },
    Output = ResolverOutput.Typed)]
```

Aliases are alternative names or paths for one conceptual property. Matching is case-insensitive by default; equivalent aliases are deduplicated. Multiple distinct eligible properties on one type produce `PR009`, even when the canonical name is present.

Set `CaseSensitive = true` to distinguish case variants. Typed mode still requires matching result types across eligible sources.

## Follow a path

```csharp
[assembly: GeneratePropertyResolver("Customer.AccountId",
    Namespace = "Demo.Generated",
    ResolverName = "CustomerAccountResolver",
    Aliases = new[] { "Legacy.AccountNumber" },
    Output = ResolverOutput.Typed)]
```

```csharp
var id = Demo.Generated.CustomerAccountResolver.GetCustomerAccountId(order);
```

Default class and method names concatenate path segments: `CustomerAccountIdResolver.GetCustomerAccountId`.

Segments are resolved semantically, including inherited and interface properties. Nullable reference or nullable-struct intermediates propagate null; ordinary structs are accessed directly. Each getter in a path is evaluated at most once per invocation.

TryGet returns true for an eligible root even if an intermediate is null.

## Boundaries

Paths contain properties only—not fields, methods, wildcards, indexer syntax, or automatic collection-element traversal. Properties of collections (such as an array’s Length) can be accessed when eligible. Missing and unsupported segments are diagnosed.
