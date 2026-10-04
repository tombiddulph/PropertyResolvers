using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace PropertyResolvers.Generators;

internal sealed record ResolverConfig(
    string PropertyName, ImmutableArray<string> Aliases, bool CaseSensitive,
    string? Namespace, string? ResolverName, int? Output, bool? RegisterRuntime,
    ImmutableArray<string> Includes, ImmutableArray<string> Excludes, Location Location)
{
    internal string MatchKey => PropertyName + "\0" + CaseSensitive + "\0" + string.Join("\0", Aliases);
    internal string MethodSuffix => string.Concat(PropertyName.Split('.'));
    internal string ClassName => ResolverName ?? MethodSuffix + "Resolver";
}

internal sealed record PropertyMatch(
    string Expression, string TypeName, bool IsValueType, bool Nullable, bool Nameable);

internal sealed record TypeMatch(string ConfigKey, ImmutableArray<PropertyMatch> Properties, string? Problem);

internal sealed record TypeCandidate(
    string FullName, string Namespace, int Depth, bool Eligible, bool IsSource, ImmutableArray<TypeMatch> Matches)
{
    internal string Fingerprint => FullName + "\0" + Namespace + "\0" + Depth + "\0" + Eligible + "\0" + IsSource + "\0" +
        string.Join("\0", Matches.Select(match => match.ConfigKey + "\0" + match.Problem + "\0" +
            string.Join("\0", match.Properties.Select(property => property.ToString()))));
}

internal sealed class CandidateComparer : IEqualityComparer<TypeCandidate>
{
    internal static readonly CandidateComparer Instance = new();
    public bool Equals(TypeCandidate? x, TypeCandidate? y) => x?.Fingerprint == y?.Fingerprint;
    public int GetHashCode(TypeCandidate obj) => StringComparer.Ordinal.GetHashCode(obj.Fingerprint);
}

internal sealed record GeneratedFile(string HintName, string Code);
internal sealed record PendingDiagnostic(DiagnosticDescriptor Rule, string Property, ImmutableArray<object> Arguments);
internal sealed record GenerationOutput(ImmutableArray<GeneratedFile> Files, ImmutableArray<PendingDiagnostic> Diagnostics);
internal sealed record ProjectDefaults(string? Namespace, int Output, bool RegisterRuntime, string? Problem);

internal sealed record GenerationSettings(
    string AssemblyName, bool HasModuleInitializer, ImmutableArray<ResolverConfig> Configs,
    ImmutableArray<string> Types, ImmutableArray<string> Namespaces)
{
    internal string? Collision(string ns, string name)
    {
        var parts = ns.Split('.');
        for (var index = 1; index <= parts.Length; index++)
        {
            var prefix = string.Join(".", parts.Take(index));
            if (Types.Contains(prefix, StringComparer.Ordinal))
            {
                return prefix;
            }
        }
        var fullName = ns + "." + name;
        return Types.Contains(fullName, StringComparer.Ordinal) || Namespaces.Contains(fullName, StringComparer.Ordinal) ? fullName : null;
    }
}

internal sealed class SettingsComparer : IEqualityComparer<GenerationSettings>
{
    internal static readonly SettingsComparer Instance = new();
    public bool Equals(GenerationSettings? x, GenerationSettings? y) => x is not null && y is not null &&
        x.AssemblyName == y.AssemblyName && x.HasModuleInitializer == y.HasModuleInitializer &&
        x.Types.SequenceEqual(y.Types) && x.Namespaces.SequenceEqual(y.Namespaces) &&
        x.Configs.Length == y.Configs.Length && x.Configs.Zip(y.Configs, SameConfig).All(equal => equal);
    private static bool SameConfig(ResolverConfig x, ResolverConfig y) =>
        x.PropertyName == y.PropertyName && x.CaseSensitive == y.CaseSensitive && x.Namespace == y.Namespace &&
        x.ResolverName == y.ResolverName && x.Output == y.Output && x.RegisterRuntime == y.RegisterRuntime &&
        x.Aliases.SequenceEqual(y.Aliases) && x.Includes.SequenceEqual(y.Includes) && x.Excludes.SequenceEqual(y.Excludes);
    public int GetHashCode(GenerationSettings obj) => StringComparer.Ordinal.GetHashCode(obj.AssemblyName);
}

internal sealed record MetadataInput(Compilation Compilation, ImmutableArray<ResolverConfig> Configs);

// A source-less compilation is used only to interpret an unchanged reference set,
// avoiding retention of source symbols or syntax trees in the metadata cache.
// Source edits do not invalidate metadata discovery. A reference, assembly-name,
// or matching-configuration change does invalidate it.
internal sealed class MetadataInputComparer : IEqualityComparer<MetadataInput>
{
    internal static readonly MetadataInputComparer Instance = new();
    public bool Equals(MetadataInput? x, MetadataInput? y) => x is not null && y is not null &&
        x.Compilation.AssemblyName == y.Compilation.AssemblyName &&
        x.Compilation.Options.Equals(y.Compilation.Options) &&
        x.Compilation.References.SequenceEqual(y.Compilation.References) &&
        x.Configs.Select(config => config.MatchKey).SequenceEqual(y.Configs.Select(config => config.MatchKey));
    public int GetHashCode(MetadataInput obj) => StringComparer.Ordinal.GetHashCode(obj.Compilation.AssemblyName ?? "");
}
