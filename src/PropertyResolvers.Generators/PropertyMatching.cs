using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PropertyResolvers.Generators;

internal static class PropertyMatching
{
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
                                  SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    internal static TypeCandidate? Source(GeneratorSyntaxContext context, CancellationToken token)
    {
        var syntax = (TypeDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(syntax, token) is not INamedTypeSymbol type)
        {
            return null;
        }

        // A partial type has one model, with members from every declaration.
        var first = type.DeclaringSyntaxReferences.FirstOrDefault();
        if (first?.SyntaxTree != syntax.SyntaxTree || first.Span != syntax.Span)
        {
            return null;
        }

        var compilation = context.SemanticModel.Compilation;
        return Candidate(compilation, type, ResolverConfiguration.Read(compilation, token), isSource: true, token);
    }

    internal static ImmutableArray<TypeCandidate> Metadata(MetadataInput input, CancellationToken token)
    {
        var candidates = ImmutableArray.CreateBuilder<TypeCandidate>();
        if (input.Configs.Length == 0)
        {
            return [];
        }

        void Type(INamedTypeSymbol type)
        {
            token.ThrowIfCancellationRequested();
            var candidate = Candidate(input.Compilation, type, input.Configs, isSource: false, token);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
            foreach (var nested in type.GetTypeMembers())
            {
                Type(nested);
            }
        }

        void Namespace(INamespaceSymbol ns)
        {
            token.ThrowIfCancellationRequested();
            foreach (var type in ns.GetTypeMembers())
            {
                Type(type);
            }
            foreach (var child in ns.GetNamespaceMembers())
            {
                Namespace(child);
            }
        }

        foreach (var reference in input.Compilation.References.Where(reference =>
                     reference.Properties.Aliases.Length == 0 || reference.Properties.Aliases.Contains("global", StringComparer.Ordinal)))
        {
            if (input.Compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
            {
                Namespace(assembly.GlobalNamespace);
            }
        }
        return candidates.ToImmutable();
    }

    private static TypeCandidate? Candidate(
        Compilation compilation, INamedTypeSymbol type, ImmutableArray<ResolverConfig> configs, bool isSource, CancellationToken token)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        {
            return null;
        }

        var fileLocal = false;
        for (var container = type; container is not null; container = container.ContainingType)
        {
            fileLocal |= container.IsFileLocal;
        }
        var eligible = !type.IsGenericType && !type.IsStatic && !type.IsRefLikeType && !fileLocal &&
            (type.ContainingType is not null || type.DeclaredAccessibility == Accessibility.Public) &&
            compilation.IsSymbolAccessibleWithin(type, compilation.Assembly);
        var matches = ImmutableArray.CreateBuilder<TypeMatch>();
        foreach (var config in configs)
        {
            token.ThrowIfCancellationRequested();
            if (!ResolverConfiguration.Path(config.PropertyName))
            {
                continue;
            }

            var paths = new[] { config.PropertyName }.Concat(config.Aliases)
                .Distinct(config.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            var properties = ImmutableArray.CreateBuilder<PropertyMatch>();
            string? problem = null;
            foreach (var path in paths.Where(ResolverConfiguration.Path))
            {
                var (match, failure) = Resolve(compilation, type, path, config.CaseSensitive, token);
                if (match is not null)
                {
                    if (!properties.Any(existing => existing.Expression == match.Expression))
                    {
                        properties.Add(match);
                    }
                }
                problem ??= failure;
            }

            if (properties.Count > 0 || problem is not null)
            {
                matches.Add(new TypeMatch(config.MatchKey, properties.ToImmutable(), problem));
            }
        }

        if (matches.Count == 0)
        {
            return null;
        }
        eligible &= Nameable(compilation, type);
        var depth = 0;
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            depth++;
        }
        return new TypeCandidate(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            RawNamespace(type.ContainingNamespace),
            depth, eligible, isSource, matches.ToImmutable());
    }

    private static (PropertyMatch? Match, string? Problem) Resolve(
        Compilation compilation, INamedTypeSymbol root, string path, bool caseSensitive, CancellationToken token)
    {
        var expression = "x";
        ITypeSymbol current = root;
        var nullableChain = false;
        var segments = path.Split('.');
        for (var index = 0; index < segments.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            if (index > 0)
            {
                current = Underlying(current);
            }
            if (current is IArrayTypeSymbol)
            {
                current = compilation.GetSpecialType(SpecialType.System_Array);
            }
            if (current is not INamedTypeSymbol named)
            {
                return (null, "Unsupported intermediate type in '" + path + "'");
            }
            var namedProperties = Members(named).Where(property => string.Equals(property.Name, segments[index],
                caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)).ToArray();
            if (namedProperties.Length == 0)
            {
                return (null, index == 0 ? null : "Missing path segment '" + segments[index] + "' in '" + path + "'");
            }
            var properties = namedProperties.Where(property => Supported(compilation, property)).ToArray();
            if (properties.Length == 0)
            {
                return (null, "Unsupported property shape in '" + path + "'");
            }
            if (properties.Length > 1)
            {
                return (null, "Ambiguous property segment '" + segments[index] + "'");
            }

            var property = properties[0];
            expression += (nullableChain ? "?." : ".") + ResolverConfiguration.Escape(property.Name);
            current = property.Type;
            var canBeNull = current.IsReferenceType || NullableValue(current);
            if (index < segments.Length - 1)
            {
                nullableChain |= canBeNull;
            }
            else
            {
                if (current.TypeKind == TypeKind.Dynamic)
                {
                    // Structural access must never require the runtime binder.
                    expression = "(object?)" + expression;
                }
                var underlying = (current.TypeKind == TypeKind.Dynamic
                    ? compilation.GetSpecialType(SpecialType.System_Object)
                    : Underlying(current)).WithNullableAnnotation(NullableAnnotation.NotAnnotated);
                return (new PropertyMatch(expression, underlying.ToDisplayString(TypeFormat), underlying.IsValueType,
                    nullableChain || NullableValue(current) || current.TypeKind == TypeKind.Dynamic ||
                    current.IsReferenceType && property.NullableAnnotation != NullableAnnotation.NotAnnotated,
                    Nameable(compilation, underlying)), null);
            }
        }

        return (null, null);
    }

    private static bool NullableValue(ITypeSymbol type) => type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    private static bool Supported(Compilation compilation, IPropertySymbol property) =>
        !property.IsStatic && !property.IsIndexer && property.DeclaredAccessibility == Accessibility.Public &&
        property.GetMethod is not null && compilation.IsSymbolAccessibleWithin(property.GetMethod, compilation.Assembly) &&
        property.Type.TypeKind is not (TypeKind.Pointer or TypeKind.FunctionPointer) &&
        property.Type is not INamedTypeSymbol { IsRefLikeType: true };
    private static ITypeSymbol Underlying(ITypeSymbol type) => NullableValue(type) && type is INamedTypeSymbol named ? named.TypeArguments[0] : type;

    private static string RawNamespace(INamespaceSymbol ns)
    {
        var parts = new Stack<string>();
        for (var current = ns; !current.IsGlobalNamespace; current = current.ContainingNamespace)
        {
            parts.Push(current.Name);
        }
        return string.Join(".", parts);
    }

    // global:: names must bind uniquely. Alias-only types remain usable in string
    // property access, but cannot appear in typed public signatures without a
    // globally accessible reference. Never emit an ambiguous metadata type name.
    private static bool Nameable(Compilation compilation, ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return Nameable(compilation, array.ElementType);
        }
        if (type is not INamedTypeSymbol named)
        {
            return type.TypeKind == TypeKind.Dynamic;
        }
        named = named.TupleUnderlyingType ?? named;
        var names = new Stack<string>();
        for (var current = named; current is not null; current = current.ContainingType)
        {
            names.Push(current.MetadataName);
        }
        var ns = RawNamespace(named.ContainingNamespace);
        var metadataName = (ns.Length == 0 ? "" : ns + ".") + string.Join("+", names);
        var globallyBound = compilation.GetTypeByMetadataName(metadataName);
        var globalReference = SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, compilation.Assembly) ||
            compilation.References.Any(reference =>
                (reference.Properties.Aliases.Length == 0 || reference.Properties.Aliases.Contains("global", StringComparer.Ordinal)) &&
                SymbolEqualityComparer.Default.Equals(compilation.GetAssemblyOrModuleSymbol(reference), named.ContainingAssembly));
        return globalReference && SymbolEqualityComparer.Default.Equals(globallyBound, named.OriginalDefinition) &&
            named.TypeArguments.All(argument => argument is ITypeParameterSymbol || Nameable(compilation, argument));
    }

    private static IEnumerable<IPropertySymbol> Members(INamedTypeSymbol type)
    {
        var hidden = new HashSet<string>(StringComparer.Ordinal);
        for (var current = type; current is not null; current = current.BaseType)
        {
            var members = current.GetMembers();
            foreach (var property in members.OfType<IPropertySymbol>().Where(property => !hidden.Contains(property.Name)))
            {
                yield return property;
            }
            hidden.UnionWith(members.Select(member => member.Name));
        }
        var inheritedProperties = (type.TypeKind == TypeKind.Interface ? type.AllInterfaces : [])
            .SelectMany(inherited => inherited.GetMembers().OfType<IPropertySymbol>())
            .Where(property => !hidden.Contains(property.Name)).ToArray();
        foreach (var property in inheritedProperties)
        {
            // Redeclarations on a more specific interface hide ancestor members;
            // unrelated sibling declarations remain distinct and are diagnosed.
            if (!inheritedProperties.Any(other => other.Name == property.Name &&
                other.ContainingType.AllInterfaces.Contains(property.ContainingType, SymbolEqualityComparer.Default)))
            {
                yield return property;
            }
        }
    }
}
