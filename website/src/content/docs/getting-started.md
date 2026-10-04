---
title: Quick start
description: Install PropertyResolvers and generate your first property resolver.
---

## Requirements

- C# 10 or later.
- Roslyn 4.11 or later: Visual Studio 2022 17.11+ or a .NET 8.0.4xx SDK+.
- The package libraries target .NET Standard 2.0; your application can target a compatible framework.

## Install

```sh
dotnet add package PropertyResolvers --version 2.5.0
```

The matching `PropertyResolvers.Attributes` dependency is installed automatically.

## Select a property

Add an assembly attribute in a regular source file. An explicit namespace makes the generated API easy to find regardless of your assembly name.

```csharp
using PropertyResolvers.Attributes;

[assembly: GeneratePropertyResolver("AccountId", Namespace = "Demo.Generated")]

namespace Demo.Models;

public class Order
{
    public string? AccountId { get; init; }
}
```

Then call the generated class from your application:

```csharp
using Demo.Generated;
using Demo.Models;

var order = new Order { AccountId = "ACC-123" };
string? value = AccountIdResolver.GetAccountId(order);
```

Without `Namespace`, the generated namespace defaults to the assembly name, unless a project default overrides it.

## Distinguish absence from null

```csharp
if (AccountIdResolver.TryGet(order, out string? accountId))
{
    // This source has the property. Its value can still be null.
}
```

Null and unsupported sources return false. A matching source with a null property returns true with a null value. Getter and formatter exceptions propagate; TryGet does not catch them.

String mode accepts mixed property types and uses invariant-culture formatting for `IFormattable` values. See [typed access](/PropertyResolvers/guides/typed-access/) when you need the original value type.
