
#nullable enable
using System;

namespace PropertyResolvers.Attributes
{
    /// <summary>
    /// Attribute to apply at the assembly level to generate property resolvers for the specified property name.
    /// </summary>
    /// <param name="propertyName"></param>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class GeneratePropertyResolverAttribute(string propertyName) : Attribute
    {
        /// <summary>
        /// The property name or dot-separated property path to resolve.
        /// </summary>
        public string PropertyName { get; } = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
        /// <summary>
        /// Namespaces to include when searching for types with the specified property.
        /// </summary>
        public string[]? IncludeNamespaces { get; set; }

        /// <summary>
        /// Namespaces to exclude when searching for types with the specified property.
        /// </summary>
        public string[]? ExcludeNamespaces { get; set; }

        /// <summary>The generated namespace; overrides project defaults.</summary>
        public string? Namespace { get; set; }

        /// <summary>The resolver class name; defaults to the path segments followed by Resolver.</summary>
        public string? ResolverName { get; set; }

        /// <summary>Whether property and alias matching is case-sensitive.</summary>
        public bool CaseSensitive { get; set; }

        /// <summary>The output mode; defaults to string conversion.</summary>
        public ResolverOutput Output { get; set; }

        /// <summary>Alternative property names or paths for this conceptual property.</summary>
        public string[]? Aliases { get; set; }

        /// <summary>Opt into legacy module-initializer registration in the shared runtime registry.</summary>
        public bool RegisterRuntime { get; set; }
    }
}
