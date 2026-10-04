# PropertyResolvers

Compile-time structural property access for C#. Resolve selected properties across unrelated domain models without reflection, `dynamic`, expression compilation, or requiring a shared interface.

**[Documentation & quick start](https://tombiddulph.github.io/PropertyResolvers/)** · [NuGet](https://www.nuget.org/packages/PropertyResolvers)

```bash
dotnet add package PropertyResolvers
```

The package installs its version-matched `PropertyResolvers.Attributes` dependency automatically.

## Compatibility-first string access

```csharp
using PropertyResolvers.Attributes;

[assembly: GeneratePropertyResolver("AccountId")]

public class Order
{
    public string? AccountId { get; init; }
}

// Generated in the namespace matching your assembly name:
var value = AccountIdResolver.GetAccountId(new Order { AccountId = "ACC-123" });
```

String output remains the default. Conversion is null-safe, and `IFormattable` values use invariant culture. Null sources, unsupported source types, and null property values return null. Getter and formatter exceptions propagate; `TryGet` is not an exception-catching API.

```csharp
if (AccountIdResolver.TryGet(message, out string? accountId))
{
    // The source has the selected property. Its value can still be null.
}
```

`TryGet` returns **true when the source type has the selected property**, including when its value is null; it returns false for null or unsupported sources. This distinguishes absence from a null value.

## Typed resolvers

```csharp
[assembly: GeneratePropertyResolver("AggregateId", Output = ResolverOutput.Typed)]

public class Event { public Guid AggregateId { get; init; } }

Guid? id = AggregateIdResolver.GetAggregateId(message);
if (AggregateIdResolver.TryGet(message, out Guid aggregateId))
{
    // No string conversion; aggregateId remains a Guid.
}
```

Type rules:

- All eligible matches must have the same underlying property type. No numeric widening or arbitrary common-base inference is performed.
- Nullable and non-nullable variants combine into a nullable out parameter. Otherwise, `TryGet` preserves the non-nullable property type.
- `Get…` always returns a nullable type because the source may not match.
- Nullable annotations inside generic type arguments must agree; incompatible types produce `PR003`.
- Typed mode with no eligible type also produces `PR003`: a return type cannot be inferred.
- Existing mixed-type logging scenarios remain valid in string mode.

## Aliases

```csharp
[assembly: GeneratePropertyResolver("AccountId",
    Aliases = new[] { "AccountID", "AccountNumber" },
    Output = ResolverOutput.Typed)]
```

Aliases are alternative names **or paths** for one conceptual property. Canonical names and aliases follow the same case-matching rule. Equivalent aliases are deduplicated. Multiple distinct matching properties on the same type are an error (`PR009`), even if the canonical property is present; selection is never arbitrary.

## Nested property paths

```csharp
[assembly: GeneratePropertyResolver("Customer.AccountId",
    ResolverName = "CustomerAccountResolver",
    Output = ResolverOutput.Typed)]

Guid? id = CustomerAccountResolver.GetCustomerAccountId(order);
```

Default resolver/method names concatenate the path segments: `CustomerAccountIdResolver.GetCustomerAccountId`. Each segment is resolved semantically, including inherited and interface properties. Null reference or nullable-struct intermediates propagate null. Ordinary structs are accessed directly. Each getter in a path is evaluated at most once per invocation.

`TryGet` returns true for an eligible root source even when an intermediate is null, with a null result. Paths are properties only: no fields, methods, wildcards, collection-element traversal or indexer syntax. Properties of collection objects can be accessed normally when eligible, but elements are not automatically traversed. Missing or unsupported segments are diagnosed.

## Stateless dispatch

Each generated namespace has a `PropertyResolverDispatch` class:

```csharp
if (PropertyResolverDispatch.TryResolve("AccountId", message, out string? value))
{
    // Known resolver AND supported source type; value may be null.
}
```

Dispatch uses ordinal case-insensitive canonical resolver names. Aliases select properties, not dispatch keys. It formats typed values as strings when accessed through this compatibility facade. Null/unknown names and unsupported sources return false.

There is no generated runtime dictionary, delegate registration, or module initializer by default. Each assembly dispatches independently, avoiding shared-registry collisions. Direct typed access and static dispatch are suitable for trimming and Native AOT.

### Legacy runtime registry

`PropertyResolverRegistry.Register` / `TryResolve` remain available for explicit dynamic registration. Registration is thread-safe and rejects duplicate property names (case-insensitively) with `InvalidOperationException`; it never silently replaces another assembly's resolver. Registry `TryResolve` retains its legacy meaning: true means a resolver is registered, not necessarily that the source matches.

For applications that require automatic legacy registration:

```csharp
[assembly: GeneratePropertyResolver("AccountId", RegisterRuntime = true)]
```

or set `<PropertyResolversRegisterRuntime>true</PropertyResolversRegisterRuntime>`. This opt-in generates a module initializer and string adapter even for typed resolvers. A compatible initializer attribute is generated automatically for older target frameworks. Runtime-registration flags are **not inherited from referenced assembly attributes**; consumers must opt in themselves. Do not register the same key in multiple assemblies.

## Configuration

```csharp
[assembly: GeneratePropertyResolver("AccountId",
    Namespace = "MyApp.Generated",
    ResolverName = "AccountResolver",
    CaseSensitive = true,
    IncludeNamespaces = new[] { "MyApp.Domain" },
    ExcludeNamespaces = new[] { "MyApp.Domain.Internal" })]
```

| Option | Default | Meaning |
|---|---|---|
| `propertyName` | Required | Unescaped identifier or dot-separated property path |
| `Namespace` | Project default, otherwise assembly name | Generated namespace |
| `ResolverName` | Concatenated path + `Resolver` | Generated class name |
| `CaseSensitive` | `false` | Property and alias matching; dispatch keys remain case-insensitive |
| `IncludeNamespaces` | All | Raw, ordinal namespace prefix matching |
| `ExcludeNamespaces` | None | Exclusions take precedence over inclusions |
| `Output` | `String` | `String` or `Typed` |
| `Aliases` | None | Alternative names/paths |
| `RegisterRuntime` | `false` | Opt into shared legacy registration |

Namespace prefixes are literal prefixes: `MyApp.Domain` also matches `MyApp.DomainExtras`. Include a trailing dot to restrict a prefix to descendants. Keywords use their unescaped names (for example `"event"`), and are escaped automatically in generated C#.

Project-wide defaults are exposed to Roslyn automatically by the package:

```xml
<PropertyGroup>
  <PropertyResolversNamespace>MyCompany.Generated</PropertyResolversNamespace>
  <PropertyResolversOutput>Typed</PropertyResolversOutput>
  <PropertyResolversRegisterRuntime>false</PropertyResolversRegisterRuntime>
</PropertyGroup>
```

Explicit attribute options override project defaults. Direct analyzer `ProjectReference` users must also import `src/PropertyResolvers.Generators/build/PropertyResolvers.props` or declare its `CompilerVisibleProperty` items themselves.

## Discovery and supported shapes

Discovery includes source types and public, globally accessible types in referenced assemblies. Assembly attributes in references are inherited defaults; a local declaration of the same canonical property overrides the inherited configuration. Duplicate local declarations are errors, including across files. Extern-alias-only types are not resolver roots; property values of those types can be formatted in string mode, but typed mode diagnoses them rather than emitting an unbindable public signature.

Classes, structs, records, record structs, partial types, accessible nested types, and inherited properties are supported. Derived patterns precede base patterns; overrides and `new` hiding are respected. A hidden base property is not selected through the derived declaration. A base pattern may still match that instance if the base itself participates.

Open generic root types, static classes, file-local types, `ref struct` roots, inaccessible types/getters, static properties, indexers, pointer/function-pointer/ref-like values are excluded. Non-generic roots may inherit from constructed generic bases, and nested paths may traverse constructed generic property types. Top-level internal types are not resolver roots, preserving the original public-root policy.

Source discovery uses semantic syntax candidates and immutable, value-equatable match models. Metadata models are invalidated only by reference, matching-configuration, or assembly-identity changes. Generated text is independently cached: unrelated method/getter-body edits do not regenerate resolvers. Relevant property, inheritance, alias, reference, and configuration changes invalidate their results.

## Diagnostics

| ID | Severity | Meaning |
|---|---|---|
| `PR001` | Error | Duplicate local canonical resolver configuration |
| `PR002` | Warning | No eligible matches |
| `PR003` | Error | Incompatible or uninferable typed result |
| `PR004` | Error | Invalid property/path/resolver identifier |
| `PR005` | Warning | Unsupported type/property/path shape prevents resolution |
| `PR006` | Warning | Namespace rules exclude all matches or all include rules |
| `PR007` | Error | Invalid generated namespace |
| `PR008` | Error | Existing or generated type/namespace/member name collision |
| `PR009` | Error | Ambiguous case-insensitive or alias match |
| `PR010` | Error | Invalid output mode or project default |

Diagnostics point at the relevant local assembly attribute when available. Metadata-only defaults have no local source location. Invalid/ambiguous configurations are skipped; other valid resolvers continue to generate. Name collisions also account for dispatch and optional registration infrastructure. A no-match string resolver remains callable and returns null/false. Diagnostics can be configured with normal Roslyn `.editorconfig` severity controls.

The IDE code fix for `PR001` removes the duplicate attribute while preserving other attributes, comments and declarations, and supports Fix All. Naming, aliases and output-mode choices are intentionally not guessed by automatic fixes.

## Requirements and development

- Package libraries target .NET Standard 2.0.
- C# 10+ for generated file-scoped namespaces.
- Roslyn 4.11+ (Visual Studio 2022 17.11+ or .NET 8.0.4xx SDK+).
- Repository development uses the .NET 10 SDK selected by `global.json`.

```bash
dotnet build PropertyResolvers.slnx -c Release
dotnet test PropertyResolvers.slnx -c Release
dotnet test -c Release --filter FullyQualifiedName~IncrementalGeneratorTests --logger "console;verbosity=detailed"
```

The incremental tests verify cached work and measure cold, unrelated-edit, and relevant-edit generation over 1,000 synthetic types. Timings are observations, not brittle pass/fail thresholds.

```powershell
./scripts/Test-Package.ps1 -WorkDirectory <temporary-directory>
# With the platform Native AOT prerequisites installed:
./scripts/Test-Package.ps1 -WorkDirectory <temporary-directory> -PublishAot
# Validate an already published version from NuGet using fresh package caches:
./scripts/Test-Package.ps1 -WorkDirectory <temporary-directory> -PackageVersion 2.5.0-rc.1 -PublishAot
```

Package validation uses isolated package caches and real NuGet consumption on .NET 8, .NET 10, and .NET Standard 2.0. CI additionally publishes and executes a Native AOT consumer on Linux. Release publication is manually requested with an explicit version, serialized, and gated by build/tests/package validation; pushes to main do not publish automatically.

## Migration from 1.x

String resolver names/signatures remain available. Formatting is now null-safe and invariant-culture. Prefer generated `PropertyResolverDispatch` over the shared registry; opt into `RegisterRuntime` only if legacy consumers require it. Duplicate registry keys now throw instead of overwriting. No-match warnings can become build errors in projects treating warnings as errors: correct or intentionally configure their severity. Typed output is opt-in, so mixed-type logging is not rejected.

## License

MIT — see [LICENSE](LICENSE).
