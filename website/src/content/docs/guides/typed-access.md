---
title: Typed access and nulls
description: Preserve property types and understand Get and TryGet null semantics.
---

```csharp
[assembly: GeneratePropertyResolver("AggregateId",
    Namespace = "Demo.Generated", Output = ResolverOutput.Typed)]

public class Event { public Guid AggregateId { get; init; } }
```

```csharp
Guid? id = Demo.Generated.AggregateIdResolver.GetAggregateId(message);
if (Demo.Generated.AggregateIdResolver.TryGet(message, out Guid aggregateId))
{
    // aggregateId remains a Guid: no string conversion.
}
```

## Type inference

All eligible matches must have the same underlying property type. No numeric widening or arbitrary common-base inference is performed. Nullable annotations inside generic type arguments must agree.

- `Get…` always returns a nullable result because the source may not match.
- `TryGet` preserves a non-nullable out type when every match is non-nullable.
- Nullable and non-nullable variants combine into a nullable out parameter.
- A null intermediate in a property path makes the result nullable.
- Incompatible types, or no eligible type to infer, produce `PR003`.

String mode remains the default and permits different underlying property types.

## What does success mean?

| Source | TryGet | Result |
|---|---|---|
| Eligible source, populated property | true | Property value |
| Eligible source, null property | true | null |
| Eligible root, null path intermediate | true | null |
| Unsupported source | false | default |
| Null source | false | default |

Only use an out value when TryGet returns true. Getter exceptions are not swallowed.
