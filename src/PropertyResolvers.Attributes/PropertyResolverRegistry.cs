#nullable enable
using System;
using System.Collections.Concurrent;

namespace PropertyResolvers.Attributes;

public static class PropertyResolverRegistry
{
    private static readonly ConcurrentDictionary<string, Func<object?, string?>> Resolvers =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(string propertyName, Func<object?, string?> resolver)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(propertyName));
        }

        Resolvers[propertyName] = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public static bool TryResolve(string propertyName, object? source, out string? value)
    {
        if (!Resolvers.TryGetValue(propertyName, out var resolver))
        {
            value = null;
            return false;
        }

        value = resolver(source);
        return true;
    }
}
