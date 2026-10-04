using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PropertyResolvers.Generators;

internal static class ResolverConfiguration
{
    internal const string AttributeName = "PropertyResolvers.Attributes.GeneratePropertyResolverAttribute";

    internal static ImmutableArray<ResolverConfig> Read(Compilation compilation, CancellationToken token)
    {
        var configs = ImmutableArray.CreateBuilder<ResolverConfig>();
        foreach (var assembly in new[] { compilation.Assembly }.Concat(compilation.SourceModule.ReferencedAssemblySymbols))
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                token.ThrowIfCancellationRequested();
                if (attribute.AttributeClass?.ToDisplayString() != AttributeName)
                {
                    continue;
                }

                var args = attribute.NamedArguments.ToDictionary(argument => argument.Key, argument => argument.Value, StringComparer.Ordinal);
                TypedConstant Arg(string name) => args.TryGetValue(name, out var value) ? value : default;
                var property = attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string : null;
                configs.Add(new ResolverConfig(
                    property ?? "", Strings(Arg("Aliases"), preserveNull: true), Arg("CaseSensitive").Value is true,
                    Arg("Namespace").Value as string, Arg("ResolverName").Value as string,
                    Arg("Output").Value as int?,
                    SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly) ? Arg("RegisterRuntime").Value as bool? : null,
                    Strings(Arg("IncludeNamespaces")), Strings(Arg("ExcludeNamespaces")),
                    SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly)
                        ? attribute.ApplicationSyntaxReference?.GetSyntax(token).GetLocation() ?? Location.None
                        : Location.None));
            }
        }

        // Local declarations override inherited defaults. Duplicate local declarations
        // are still diagnosed compilation-wide by PR001.
        return [.. configs.GroupBy(config => config.PropertyName, StringComparer.OrdinalIgnoreCase).Select(group => group.First())];
    }

    private static ImmutableArray<string> Strings(TypedConstant value, bool preserveNull = false) =>
        value.Kind == TypedConstantKind.Array && !value.IsNull
            ? [.. value.Values.Select(item => item.Value as string).Where(item => item is not null || preserveNull).Select(item => item ?? "")]
            : [];

    internal static ProjectDefaults Defaults(AnalyzerConfigOptions options)
    {
        options.TryGetValue("build_property.PropertyResolversNamespace", out var ns);
        options.TryGetValue("build_property.PropertyResolversOutput", out var output);
        options.TryGetValue("build_property.PropertyResolversRegisterRuntime", out var register);
        var mode = string.Equals(output, "Typed", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var problem = !string.IsNullOrEmpty(output) && mode == 0 && !string.Equals(output, "String", StringComparison.OrdinalIgnoreCase)
            ? "PropertyResolversOutput must be String or Typed" : null;
        var runtime = false;
        if (!string.IsNullOrEmpty(register) && !bool.TryParse(register, out runtime))
        {
            problem = "PropertyResolversRegisterRuntime must be true or false";
        }

        return new ProjectDefaults(string.IsNullOrEmpty(ns) ? null : ns, mode, runtime, problem);
    }

    internal static bool Identifier(string value)
    {
        var escaped = Escape(value);
        var token = SyntaxFactory.ParseToken(escaped);
        return token.IsKind(SyntaxKind.IdentifierToken) && !token.ContainsDiagnostics &&
               token.ToFullString() == escaped && token.ValueText == value;
    }

    internal static bool Path(string value) => value.Split('.').All(Identifier);
    internal static string Escape(string value) =>
        SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(value) != SyntaxKind.None
            ? "@" + value : value;
    internal static string EscapeNamespace(string value) => string.Join(".", value.Split('.').Select(Escape));
    internal static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    internal static bool Include(string ns, ResolverConfig config) =>
        (config.Includes.Length == 0 || config.Includes.Any(prefix => ns.StartsWith(prefix, StringComparison.Ordinal))) &&
        !config.Excludes.Any(prefix => ns.StartsWith(prefix, StringComparison.Ordinal));

    internal static GenerationSettings Settings(Compilation compilation, CancellationToken token)
    {
        var types = ImmutableArray.CreateBuilder<string>();
        var namespaces = ImmutableArray.CreateBuilder<string>();
        void Visit(INamespaceSymbol ns, string prefix)
        {
            token.ThrowIfCancellationRequested();
            foreach (var type in ns.GetTypeMembers().Where(type => type.Arity == 0 && !type.IsFileLocal))
            {
                types.Add(prefix + type.Name);
            }
            foreach (var child in ns.GetNamespaceMembers())
            {
                var name = prefix + child.Name;
                namespaces.Add(name);
                Visit(child, name + ".");
            }
        }
        Visit(compilation.Assembly.GlobalNamespace, "");
        return new GenerationSettings(compilation.AssemblyName ?? "Generated",
            compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ModuleInitializerAttribute") is not null,
            [.. Read(compilation, token).Select(config => config with { Location = Location.None })],
            [.. types.OrderBy(name => name, StringComparer.Ordinal)], [.. namespaces.OrderBy(name => name, StringComparer.Ordinal)]);
    }
}
