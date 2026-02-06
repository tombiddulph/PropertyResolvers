using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace PropertyResolvers.Generators;

/// <summary>
/// Generates property resolver methods based on assembly-level GeneratePropertyResolver attributes.
/// </summary>
[Generator]
public class PropertyResolverGenerator : IIncrementalGenerator
{
    private const string AttributeFullName = "PropertyResolvers.Attributes.GeneratePropertyResolverAttribute";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Get configs and root namespace from compilation
        var compilationData = context.CompilationProvider
            .Select((compilation, _) => (
                Configs: GetResolverConfigs(compilation),
                RootNamespace: GetRootNamespace(compilation)
            ));

        // Get all named types in the compilation
        var allTypes = context.CompilationProvider
            .SelectMany((compilation, _) => GetAllNamedTypes(compilation));

        // Combine configs with all types
        var combined = compilationData.Combine(allTypes.Collect());

        // Generate the resolvers
        context.RegisterSourceOutput(combined, (ctx, source) =>
        {
            var ((configs, rootNamespace), types) = source;
            GenerateResolvers(ctx, configs, types, rootNamespace);
        });
    }

    private static string GetRootNamespace(Compilation compilation)
    {
        // Try to get from assembly name first
        var assemblyName = compilation.AssemblyName;
        if (!string.IsNullOrEmpty(assemblyName))
        {
            return assemblyName!;
        }

        // Fallback: try to infer from the most common root namespace of types
        var namespaces = compilation.Assembly
            .GlobalNamespace
            .GetNamespaceMembers()
            .Where(ns => !ns.IsGlobalNamespace &&
                         !ns.Name.StartsWith("System", StringComparison.Ordinal) &&
                         !ns.Name.StartsWith("Microsoft", StringComparison.Ordinal))
            .Select(ns => ns.Name)
            .ToList();

        return namespaces.Count switch
        {
            > 0 => namespaces[0],
            _ => "Generated"
        };
    }

    private static ImmutableArray<ResolverConfig> GetResolverConfigs(Compilation compilation)
    {
        var configs = new List<ResolverConfig>();

        // Check the current assembly for attributes
        CollectConfigsFromAssembly(compilation.Assembly, configs);

        // Check referenced assemblies for attributes (e.g., when the attribute is
        // defined in a package that the consuming project references)
        foreach (var referencedAssembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            CollectConfigsFromAssembly(referencedAssembly, configs);
        }

        return [.. configs];
    }

    private static void CollectConfigsFromAssembly(IAssemblySymbol assembly, List<ResolverConfig> configs)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != AttributeFullName)
            {
                continue;
            }

            var propertyName = attribute.ConstructorArguments[0].Value as string;
            if (string.IsNullOrEmpty(propertyName))
            {
                continue;
            }

            var config = new ResolverConfig
            {
                PropertyName = propertyName!
            };

            foreach (var namedArg in attribute.NamedArguments)
            {
                switch (namedArg.Key)
                {
                    case "IncludeNamespaces":
                        config.IncludeNamespaces = [.. namedArg.Value.Values
                            .Select(v => v.Value as string)
                            .Where(v => v != null)
                            .Cast<string>()];
                        break;

                    case "ExcludeNamespaces":
                        config.ExcludeNamespaces = [.. namedArg.Value.Values
                            .Select(v => v.Value as string)
                            .Where(v => v != null)
                            .Cast<string>()];
                        break;

                }
            }

            configs.Add(config);
        }
    }

    private static List<TypeInfo> GetAllNamedTypes(Compilation compilation)
    {
        var types = new List<TypeInfo>();
        CollectTypes(compilation.GlobalNamespace, types);
        return types;
    }

    private static void CollectTypes(INamespaceSymbol ns, List<TypeInfo> types)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            // Skip generic types - they cannot be pattern-matched in switch expressions
            if (type.IsGenericType)
            {
                continue;
            }
            
            if (type is {TypeKind: TypeKind.Class or TypeKind.Struct, DeclaredAccessibility: Accessibility.Public})
            {
                var properties = type.GetMembers()
                    .OfType<IPropertySymbol>()
                    .Where(p => p.DeclaredAccessibility == Accessibility.Public && p.GetMethod != null)
                    .Select(p => new PropertyInfo(p.Name, IsNullableProperty(p)))
                    .ToImmutableArray();

                if (properties.Length > 0)
                {
                    types.Add(new TypeInfo(
                        type.ToDisplayString(),
                        type.ContainingNamespace?.ToDisplayString() ?? "",
                        properties));
                }
            }

            // Recurse into nested types
            foreach (var nested in type.GetTypeMembers())
            {
                CollectNestedTypes(nested, types);
            }
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            CollectTypes(childNs, types);
        }
    }

    private static void CollectNestedTypes(INamedTypeSymbol type, List<TypeInfo> types)
    {
        // Skip generic types - they cannot be pattern-matched in switch expressions
        if (type.IsGenericType)
        {
            return;
        }

        if (type.TypeKind is TypeKind.Class or TypeKind.Struct)
        {
            var properties = type.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p => p.DeclaredAccessibility == Accessibility.Public && p.GetMethod != null)
                .Select(p => new PropertyInfo(p.Name, IsNullableProperty(p)))
                .ToImmutableArray();

            if (properties.Length > 0)
            {
                types.Add(new TypeInfo(
                    type.ToDisplayString(),
                    type.ContainingNamespace?.ToDisplayString() ?? "",
                    properties));
            }
        }

        foreach (var nested in type.GetTypeMembers())
        {
            CollectNestedTypes(nested, types);
        }
    }

    private static void GenerateResolvers(
        SourceProductionContext context,
        ImmutableArray<ResolverConfig> configs,
        ImmutableArray<TypeInfo> allTypes,
        string rootNamespace)
    {
        if (configs.Length == 0)
        {
            return;
        }

        var generatedNamespace = $"{rootNamespace}";

        // Deduplicate configs by property name (take first occurrence only)
        // The analyzer will report diagnostics for duplicates
        var deduplicatedConfigs = configs
            .GroupBy(c => c.PropertyName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        // Group by resolver class name
        var byClassName = deduplicatedConfigs
            .GroupBy(c => c.ResolverClassName ?? "PropertyResolversTest")
            .ToList();

        foreach (var group in byClassName)
        {
            var className = group.Key;
            var methods = new StringBuilder();

            foreach (var config in group)
            {

                var matches = allTypes
                    .Where(t => ShouldIncludeType(t, config))
                    .SelectMany(t => t.Properties
                        .Where(p => p.Name.Equals(config.PropertyName, StringComparison.OrdinalIgnoreCase))
                        .Select(p => (TypeFullName: t.FullName, PropertyName: p.Name, p.IsNullable)))
                    .ToList();

                const string returnType = "string?";
                var methodName = $"Get{config.PropertyName}";

                methods.AppendLine($"    public static {returnType} {methodName}(object? obj) => obj switch");
                methods.AppendLine("    {");

                foreach (var (typeName, propertyName, isNullable) in matches)
                {
                    var toStringCall = isNullable ? "?.ToString()" : ".ToString()";
                    methods.AppendLine($"        global::{typeName} x => x.{propertyName}{toStringCall},");
                }

                methods.AppendLine("        _ => null");
                methods.AppendLine("    };");
                methods.AppendLine();
            }

            var code = $$"""
                         // <auto-generated/>
                         #nullable enable

                         namespace {{generatedNamespace}};

                         public static class {{className}}
                         {
                         {{methods}}}

                         """;

            context.AddSource($"{className}.g.cs", SourceText.From(code, Encoding.UTF8));
        }
    }

    private static bool IsNullableProperty(IPropertySymbol property)
    {
        // Nullable value type (e.g., int?, Guid?)
        if (property.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return true;
        }

        // Nullable reference type (e.g., string?, object?)
        if (property.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return true;
        }

        return false;
    }

    private static bool ShouldIncludeType(TypeInfo type, ResolverConfig config)
    {
        var ns = type.Namespace;

        // If includes specified, namespace must match one
        if (config.IncludeNamespaces is { Length: > 0 })
        {
            if (!config.IncludeNamespaces.Any(inc => ns.StartsWith(inc, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        // If excludes specified, namespace must not match any
        if (config.ExcludeNamespaces is { Length: > 0 })
        {
            if (config.ExcludeNamespaces.Any(exc => ns.StartsWith(exc, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class ResolverConfig
    {
        public string PropertyName { get; set; } = null!;
        public string[]? IncludeNamespaces { get; set; }
        public string[]? ExcludeNamespaces { get; set; }
        public string ResolverClassName => $"{PropertyName}Resolver";
    }

    private record struct PropertyInfo(string Name, bool IsNullable);

    private record struct TypeInfo(string FullName, string Namespace, ImmutableArray<PropertyInfo> Properties);
}
