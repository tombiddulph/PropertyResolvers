using Microsoft.CodeAnalysis;

namespace PropertyResolvers.Generators;

internal static class GeneratorDiagnostics
{
    internal static readonly DiagnosticDescriptor NoMatches = new("PR002", "No eligible property matches",
        "No eligible properties were found for '{0}'", "Usage", DiagnosticSeverity.Warning, true);
    internal static readonly DiagnosticDescriptor IncompatibleTypes = new("PR003", "Incompatible typed resolver properties",
        "Typed resolver '{0}' cannot infer a common property type: {1}", "Usage", DiagnosticSeverity.Error, true);
    internal static readonly DiagnosticDescriptor UnsupportedMatch = new("PR005", "Unsupported property or type",
        "Resolver '{0}' cannot use type '{1}': {2}", "Usage", DiagnosticSeverity.Warning, true);
    internal static readonly DiagnosticDescriptor ExcludedMatches = new("PR006", "Namespace rules exclude all matches",
        "Namespace configuration excludes every eligible match for '{0}'", "Usage", DiagnosticSeverity.Warning, true);
    internal static readonly DiagnosticDescriptor AmbiguousMatch = new("PR009", "Ambiguous structural property match",
        "Resolver '{0}' matches multiple properties or aliases on type '{1}'", "Usage", DiagnosticSeverity.Error, true);
    internal static readonly DiagnosticDescriptor InvalidConfiguration = new("PR010", "Invalid resolver configuration",
        "Invalid property resolver configuration: {0}", "Usage", DiagnosticSeverity.Error, true);
    internal static readonly DiagnosticDescriptor InvalidPropertyName = new(
        "PR004",
        "Invalid property resolver name",
        "Name or path '{0}' contains an invalid unescaped C# identifier",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Specify valid dot-separated identifiers without @ prefixes, wildcards or indexers.");

    internal static readonly DiagnosticDescriptor InvalidNamespace = new(
        "PR007",
        "Invalid generated namespace",
        "Name '{0}' cannot be used as a generated C# namespace",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Each dot-separated assembly name segment must be a valid unescaped C# identifier.");

    internal static readonly DiagnosticDescriptor GeneratedNameCollision = new(
        "PR008",
        "Generated name conflicts with an existing declaration",
        "Generated name '{0}' conflicts with an existing type or namespace",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Rename the conflicting declaration or change the assembly name to avoid a generated name collision.");
}
