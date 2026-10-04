using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PropertyResolvers.Generators;

/// <summary>Generates structural property access without runtime reflection.</summary>
[Generator]
public sealed class PropertyResolverGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var sourceTypes = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax,
                static (syntax, token) => PropertyMatching.Source(syntax, token))
            .Where(static candidate => candidate is not null)
            .Select(static (candidate, _) => candidate!)
            .WithComparer(CandidateComparer.Instance)
            .WithTrackingName("SourceTypes");

        var metadataTypes = context.CompilationProvider
            .Select(static (compilation, token) => new MetadataInput(compilation.RemoveAllSyntaxTrees(),
                [.. ResolverConfiguration.Read(compilation, token).Select(config => config with { Location = Location.None })]))
            .WithComparer(MetadataInputComparer.Instance)
            .Select(static (input, token) => PropertyMatching.Metadata(input, token))
            .WithTrackingName("MetadataTypes");

        var defaults = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) => ResolverConfiguration.Defaults(options.GlobalOptions));
        var settings = context.CompilationProvider.Select(static (compilation, token) => ResolverConfiguration.Settings(compilation, token))
            .WithComparer(SettingsComparer.Instance).WithTrackingName("Configuration");
        var locations = context.CompilationProvider.Select(static (compilation, token) => ResolverConfiguration.Read(compilation, token));
        var input = settings.Combine(defaults).Combine(sourceTypes.Collect()).Combine(metadataTypes);
        var output = input.Select(static (value, token) =>
        {
            var (((generationSettings, projectDefaults), source), metadata) = value;
            token.ThrowIfCancellationRequested();
            return Generate(generationSettings, projectDefaults, source.AddRange(metadata), token);
        }).WithTrackingName("ResolverModel");

        // Diagnostics retain current source locations; source text is separately
        // value-equatable so unrelated method edits do not regenerate resolvers.
        context.RegisterSourceOutput(output.Combine(locations), static (production, value) =>
        {
            var (result, configs) = value;
            foreach (var diagnostic in result.Diagnostics)
            {
                var location = configs.FirstOrDefault(config => config.PropertyName == diagnostic.Property)?.Location ?? Location.None;
                production.ReportDiagnostic(Diagnostic.Create(diagnostic.Rule, location, diagnostic.Arguments.ToArray()));
            }
        });
        var files = output.SelectMany(static (result, _) => result.Files)
            .WithComparer(EqualityComparer<GeneratedFile>.Default).WithTrackingName("ResolverSources");
        context.RegisterSourceOutput(files, static (production, file) =>
            production.AddSource(file.HintName, SourceText.From(file.Code, Encoding.UTF8)));
    }

    private static GenerationOutput Generate(
        GenerationSettings settings, ProjectDefaults defaults, ImmutableArray<TypeCandidate> types,
        System.Threading.CancellationToken token)
    {
        var files = ImmutableArray.CreateBuilder<GeneratedFile>();
        var diagnostics = ImmutableArray.CreateBuilder<PendingDiagnostic>();
        var plans = new List<ResolverPlan>();
        var configs = settings.Configs;
        var sourceTypeNames = new HashSet<string>(settings.Types.Select(name => "global::" + ResolverConfiguration.EscapeNamespace(name)), StringComparer.Ordinal);
        // Cached metadata models use a source-less compilation. Apply the current
        // source-name overlay here so a local declaration cannot change the binding
        // of a cached global:: root or public result type.
        types = [.. types.Where(type => type.IsSource || !sourceTypeNames.Any(name =>
            type.FullName == name || type.FullName.StartsWith(name + ".", StringComparison.Ordinal)))];
        bool UnnameableResult(ResolverArm arm) => !arm.Property.Nameable || !arm.IsSource &&
            SyntaxFactory.ParseTypeName(arm.Property.TypeName).DescendantNodesAndSelf()
                .Where(node => node is QualifiedNameSyntax or AliasQualifiedNameSyntax)
                .Any(node => sourceTypeNames.Contains(node.ToString()));
        foreach (var config in configs)
        {
            token.ThrowIfCancellationRequested();
            void Report(DiagnosticDescriptor rule, params object[] args) => diagnostics.Add(AtConfig(rule, config, args));
            if (!ResolverConfiguration.Path(config.PropertyName) || config.Aliases.Any(alias => !ResolverConfiguration.Path(alias)))
            {
                Report(GeneratorDiagnostics.InvalidPropertyName,
                    !ResolverConfiguration.Path(config.PropertyName) ? config.PropertyName : config.Aliases.First(alias => !ResolverConfiguration.Path(alias)));
                continue;
            }
            if (!ResolverConfiguration.Identifier(config.ClassName))
            {
                Report(GeneratorDiagnostics.InvalidPropertyName, config.ClassName);
                continue;
            }
            if (config.ClassName is "TryGet" or "FormatValue" or "__GetString" || config.ClassName == "Get" + config.MethodSuffix)
            {
                Report(GeneratorDiagnostics.GeneratedNameCollision, config.ClassName);
                continue;
            }
            if (defaults.Problem is not null || (config.Output ?? defaults.Output) is not (0 or 1))
            {
                Report(GeneratorDiagnostics.InvalidConfiguration, defaults.Problem ?? "Output must be String or Typed");
                continue;
            }

            var ns = config.Namespace ?? defaults.Namespace ?? settings.AssemblyName;
            if (!ResolverConfiguration.Path(ns))
            {
                Report(GeneratorDiagnostics.InvalidNamespace, ns);
                continue;
            }
            var runtime = config.RegisterRuntime ?? defaults.RegisterRuntime;
            var collision = settings.Collision(ns, config.ClassName);
            if (collision is not null || config.ClassName is "PropertyResolverDispatch" or "PropertyResolverModuleInitializer")
            {
                Report(GeneratorDiagnostics.GeneratedNameCollision, collision ?? ns + "." + config.ClassName);
                continue;
            }

            var all = types.Select(type => (Type: type, Match: type.Matches.FirstOrDefault(match => match.ConfigKey == config.MatchKey)))
                .Where(pair => pair.Match is not null).ToArray();
            var included = all.Where(pair => ResolverConfiguration.Include(pair.Type.Namespace, config)).ToArray();
            var ambiguous = included.Where(pair => pair.Type.Eligible && (pair.Match!.Properties.Length > 1 ||
                pair.Match.Problem?.StartsWith("Ambiguous", StringComparison.Ordinal) == true)).ToArray();
            if (ambiguous.Length > 0)
            {
                foreach (var pair in ambiguous)
                {
                    Report(GeneratorDiagnostics.AmbiguousMatch, config.PropertyName, pair.Type.FullName);
                }
                continue;
            }

            var matches = included.Where(pair => pair.Type.Eligible && pair.Match!.Properties.Length == 1)
                .GroupBy(pair => pair.Type.FullName, StringComparer.Ordinal).Select(group => group.First())
                .OrderByDescending(pair => pair.Type.Depth).ThenBy(pair => pair.Type.FullName, StringComparer.Ordinal)
                .Select(pair => new ResolverArm(pair.Type.FullName, pair.Match!.Properties[0], pair.Type.IsSource)).ToImmutableArray();
            var conflictingFilters = config.Includes.Length > 0 && config.Includes.All(include =>
                config.Excludes.Any(exclude => include.StartsWith(exclude, StringComparison.Ordinal)));
            if (matches.Length == 0)
            {
                var unsupported = included.FirstOrDefault(pair => !pair.Type.Eligible || pair.Match!.Problem is not null);
                if (conflictingFilters)
                {
                    Report(GeneratorDiagnostics.ExcludedMatches, config.PropertyName);
                }
                else if (unsupported.Type is not null)
                {
                    Report(GeneratorDiagnostics.UnsupportedMatch, config.PropertyName, unsupported.Type.FullName,
                        unsupported.Match!.Problem ?? "Type cannot participate in an object-based resolver");
                }
                else
                {
                    Report(all.Any(pair => pair.Type.Eligible && pair.Match!.Properties.Length > 0)
                        ? GeneratorDiagnostics.ExcludedMatches : GeneratorDiagnostics.NoMatches, config.PropertyName);
                }
            }

            var typed = (config.Output ?? defaults.Output) == 1;
            if (typed && (matches.Length == 0 || matches.Any(UnnameableResult) ||
                          matches.Select(arm => arm.Property.TypeName).Distinct(StringComparer.Ordinal).Count() != 1))
            {
                Report(GeneratorDiagnostics.IncompatibleTypes, config.PropertyName,
                    matches.Length == 0 ? "no eligible property type" : matches.Any(UnnameableResult)
                        ? "property type is not uniquely accessible through global::" : string.Join(", ", matches.Select(arm => arm.Property.TypeName).Distinct(StringComparer.Ordinal)));
                continue;
            }
            plans.Add(new ResolverPlan(config, ns, typed, runtime, matches));
        }

        // Two otherwise valid configurations may choose the same generated name.
        var duplicates = new HashSet<ResolverPlan>(plans.GroupBy(plan => plan.Namespace + "." + plan.Config.ClassName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).SelectMany(group => group));
        var generatedTypes = plans.Select(plan => plan.Namespace + "." + plan.Config.ClassName)
            .Concat(plans.Select(plan => plan.Namespace + ".PropertyResolverDispatch"))
            .Concat(plans.Where(plan => plan.RegisterRuntime).Select(plan => plan.Namespace + ".PropertyResolverModuleInitializer"))
            .Distinct(StringComparer.Ordinal).ToArray();
        foreach (var name in generatedTypes.Where(name => plans.Any(plan => plan.Namespace == name ||
                     plan.Namespace.StartsWith(name + ".", StringComparison.Ordinal))))
        {
            foreach (var plan in plans.Where(plan => plan.Namespace == name || plan.Namespace.StartsWith(name + ".", StringComparison.Ordinal) ||
                         plan.Namespace + "." + plan.Config.ClassName == name ||
                         plan.Namespace + ".PropertyResolverDispatch" == name || plan.Namespace + ".PropertyResolverModuleInitializer" == name))
            {
                duplicates.Add(plan);
            }
        }
        foreach (var plan in duplicates)
        {
            diagnostics.Add(AtConfig(GeneratorDiagnostics.GeneratedNameCollision, plan.Config,
                plan.Namespace + "." + plan.Config.ClassName));
        }
        plans.RemoveAll(duplicates.Contains);
        foreach (var plan in plans)
        {
            files.Add(new GeneratedFile(plan.Namespace + "." + plan.Config.ClassName + ".g.cs", ResolverWriter.Resolver(plan)));
        }
        foreach (var group in plans.GroupBy(plan => plan.Namespace, StringComparer.Ordinal))
        {
            var members = group.ToImmutableArray();
            var config = members[0].Config;
            var dispatchCollision = settings.Collision(group.Key, "PropertyResolverDispatch");
            if (dispatchCollision is null)
            {
                files.Add(new GeneratedFile(group.Key + ".PropertyResolverDispatch.g.cs", ResolverWriter.Dispatch(group.Key, members)));
            }
            else
            {
                diagnostics.Add(AtConfig(GeneratorDiagnostics.GeneratedNameCollision, config, dispatchCollision));
            }
            var registrations = members.Where(plan => plan.RegisterRuntime).ToImmutableArray();
            if (registrations.Length > 0)
            {
                var initializerCollision = settings.Collision(group.Key, "PropertyResolverModuleInitializer");
                if (initializerCollision is null)
                {
                    files.Add(new GeneratedFile(group.Key + ".PropertyResolverRegistration.g.cs", ResolverWriter.Registration(group.Key, registrations)));
                }
                else
                {
                    diagnostics.Add(AtConfig(GeneratorDiagnostics.GeneratedNameCollision, config, initializerCollision));
                }
            }
        }
        if (plans.Any(plan => plan.RegisterRuntime) &&
            !settings.HasModuleInitializer)
        {
            files.Add(new GeneratedFile("ModuleInitializerAttribute.g.cs", ResolverWriter.ModuleInitializerAttribute));
        }
        return new GenerationOutput(files.ToImmutable(), diagnostics.ToImmutable());
    }

    private static PendingDiagnostic AtConfig(DiagnosticDescriptor rule, ResolverConfig config, params object[] args) =>
        new(rule, config.PropertyName, [.. args]);
}
