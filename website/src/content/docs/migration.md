---
title: Migrating to 2.5
description: Upgrade from earlier releases and preserve legacy registry consumers.
---

Version 2.5.0 introduces typed resolution, aliases, nested paths, and stateless dispatch while keeping String output as the default.

## Review these behavior changes

1. **Automatic registry registration is opt-in.** Prefer generated PropertyResolverDispatch. Legacy applications can set RegisterRuntime = true or the PropertyResolversRegisterRuntime MSBuild property.
2. **Duplicate registry keys throw**, rather than overwriting another assembly’s registration.
3. String formatting is null-safe and invariant-culture for IFormattable values.
4. New no-match and unsupported-shape warnings may fail warnings-as-errors builds.
5. The main package depends on the matching PropertyResolvers.Attributes package, rather than bundling another copy of its assembly.

Existing Get&lt;Property&gt;(object?) string access remains available. Typed mode is opt-in; mixed-type logging remains supported in String mode.

Require C# 10+ and Roslyn 4.11+ (.NET 8.0.4xx SDK+ / Visual Studio 2022 17.11+).

See the [release notes](https://github.com/tombiddulph/PropertyResolvers/releases/tag/v2.5.0) and [dispatch guide](/PropertyResolvers/guides/dispatch/) before migrating registry consumers.
