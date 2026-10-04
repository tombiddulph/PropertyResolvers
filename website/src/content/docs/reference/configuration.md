---
title: Configuration
description: Attribute options, project defaults, and namespace filtering.
---

| Attribute option | Default | Meaning |
|---|---|---|
| `propertyName` | Required | Unescaped identifier or dot-separated path |
| `Namespace` | Project default, then assembly name | Generated namespace |
| `ResolverName` | Concatenated path + Resolver | Generated class name |
| `CaseSensitive` | false | Property and alias matching |
| `IncludeNamespaces` | All | Ordinal namespace prefix matching |
| `ExcludeNamespaces` | None | Exclusions override inclusions |
| `Output` | String | ResolverOutput.String or ResolverOutput.Typed |
| `Aliases` | None | Alternative names or paths |
| `RegisterRuntime` | false | Opt into legacy shared registration |

```csharp
[assembly: GeneratePropertyResolver("AccountId",
    Namespace = "MyApp.Generated",
    ResolverName = "AccountResolver",
    CaseSensitive = true,
    IncludeNamespaces = new[] { "MyApp.Domain" },
    ExcludeNamespaces = new[] { "MyApp.Domain.Internal" })]
```

Namespace filters are literal prefixes: `MyApp.Domain` also matches `MyApp.DomainExtras`. A trailing dot restricts a prefix to descendants (and does not include the exact parent namespace). Use unescaped keywords, such as `"event"`; generated C# escapes them automatically.

## MSBuild defaults

```xml
<PropertyGroup>
  <PropertyResolversNamespace>MyCompany.Generated</PropertyResolversNamespace>
  <PropertyResolversOutput>Typed</PropertyResolversOutput>
  <PropertyResolversRegisterRuntime>false</PropertyResolversRegisterRuntime>
</PropertyGroup>
```

Explicit attribute options override project defaults. NuGet exposes these properties to Roslyn automatically. Direct analyzer ProjectReference users must import `src/PropertyResolvers.Generators/build/PropertyResolvers.props` or declare its CompilerVisibleProperty items themselves.

Referenced assembly attributes supply inherited configurations; a local declaration of the same canonical property takes precedence. Duplicate local declarations are errors, including across files.
