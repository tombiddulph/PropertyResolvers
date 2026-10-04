---
title: Static dispatch and legacy registration
description: Choose stateless dispatch or explicitly opt into the legacy shared registry.
---

Each generated namespace includes a stateless facade:

```csharp
if (Demo.Generated.PropertyResolverDispatch.TryResolve(
    "AccountId", message, out string? value))
{
    // Known resolver AND supported source; value can still be null.
}
```

Keys are canonical resolver names, compared ordinally and case-insensitively. Aliases select model properties, not dispatch keys. Typed values are formatted as strings through this facade. Unknown/null keys and unsupported/null sources return false.

There is no generated dictionary, delegate registration, or module initializer by default. Each assembly dispatches independently. Direct access and static dispatch are suitable for trimming and Native AOT.

## Legacy registry

`PropertyResolverRegistry.Register` and `TryResolve` remain available for dynamic registration. Duplicate keys (case-insensitively) throw `InvalidOperationException`, rather than silently replacing another resolver.

For automatic legacy registration, explicitly opt in:

```csharp
[assembly: GeneratePropertyResolver("AccountId", RegisterRuntime = true)]
```

Or set `<PropertyResolversRegisterRuntime>true</PropertyResolversRegisterRuntime>`. This generates a module initializer and string adapter, even in typed mode. A compatible initializer attribute is supplied for older framework reference surfaces.

Registry TryResolve retains its legacy meaning: true means a resolver is registered, not that the source matches. Runtime-registration flags are not inherited from referenced assembly attributes; consumers must opt in. Do not register the same key in multiple assemblies.
