
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
        /// The name of the property to generate a resolver for, note that the generated ressolver class
        /// will match the casing of the property name.
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
    }
}