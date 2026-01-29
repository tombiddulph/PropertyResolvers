
#nullable enable
using System;

namespace PropertyResolvers.Attributes
{
    [AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class GeneratePropertyResolverAttribute(string propertyName) : System.Attribute
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