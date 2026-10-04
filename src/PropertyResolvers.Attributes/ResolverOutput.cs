using System.Diagnostics.CodeAnalysis;

namespace PropertyResolvers.Attributes;

/// <summary>Controls whether generated accessors format values or preserve their types.</summary>
public enum ResolverOutput
{
    /// <summary>Null-safe, invariant-culture string conversion (the compatibility default).</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The option explicitly selects string output.")]
    String,
    /// <summary>Preserve the common property type; incompatible types are diagnosed.</summary>
    Typed
}
