using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SparseTypes.Blazor;

/// <summary>Cached immutable reflection metadata for fallback model reads.</summary>
/// <remarks>
/// Weak keys never root collectible assemblies and never capture model
/// instances; only <see cref="Type"/>, members, and reader delegates are
/// retained. Negative results are cached so misses stay cheap. Callers resolve
/// intermediate objects against the live root on every read; this cache only
/// answers what a type looks like.
/// </remarks>
internal static class SparseTypeMetadataCache
{
    private static readonly ConditionalWeakTable<Type, TypeMetadata> Cache = new();

    /// <summary>Reader for one stable key property.</summary>
    internal sealed record KeyReaderInfo
    {
        /// <summary>Gets the key getter.</summary>
        public required Func<object, object?> GetKey { get; init; }

        /// <summary>Gets the key type.</summary>
        public required Type KeyType { get; init; }
    }

    private sealed class TypeMetadata
    {
        public readonly Dictionary<string, PropertyInfo?> Properties = new(StringComparer.Ordinal);
        public Type? SequenceElementType;
        public bool SequenceElementTypeKnown;
        public Type? ReadOnlyListInterface;
        public bool ReadOnlyListInterfaceKnown;
        public PropertyInfo? ReadOnlyListIndexer;
        public Type? ReadOnlyDictionaryInterface;
        public bool ReadOnlyDictionaryInterfaceKnown;
        public MethodInfo? ReadOnlyDictionaryContainsKey;
        public PropertyInfo? ReadOnlyDictionaryIndexer;
        public bool ReadOnlyCollectionInterfaceKnown;
        public PropertyInfo? ReadOnlyCollectionCount;
        public Type? DictionaryKeyType;
        public bool DictionaryKeyTypeKnown;
        public KeyReaderInfo? AssignedKeyReader;
        public bool AssignedKeyReaderKnown;
        public Func<object, object?>? TemporaryKeyReader;
        public bool TemporaryKeyReaderKnown;
    }

    private static TypeMetadata GetOrAdd(Type type) => Cache.GetValue(type, static _ => new());

    /// <summary>Gets a public instance property, caching misses.</summary>
    internal static bool TryGetProperty(Type type, string name, out PropertyInfo? property)
    {
        var metadata = GetOrAdd(type);
        lock (metadata.Properties)
        {
            if (metadata.Properties.TryGetValue(name, out property))
            {
                return property is not null;
            }

            property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property is not null && property.GetIndexParameters().Length > 0)
            {
                property = null;
            }

            metadata.Properties.Add(name, property);
            return property is not null;
        }
    }

    /// <summary>Gets the first sequence element type, if any.</summary>
    internal static Type? GetSequenceElementType(Type type)
    {
        var metadata = GetOrAdd(type);
        lock (metadata.Properties)
        {
            if (!metadata.SequenceElementTypeKnown)
            {
                metadata.SequenceElementTypeKnown = true;
                metadata.SequenceElementType = FindSequenceElementType(type);
            }

            return metadata.SequenceElementType;
        }
    }

    /// <summary>Gets the read-only list interface and element indexer, if supported.</summary>
    internal static bool TryGetReadOnlyList(
        Type type,
        out Type interfaceType,
        out PropertyInfo? indexer
    )
    {
        var metadata = GetOrAdd(type);
        lock (metadata.Properties)
        {
            if (!metadata.ReadOnlyListInterfaceKnown)
            {
                metadata.ReadOnlyListInterfaceKnown = true;
                metadata.ReadOnlyListInterface = FindGenericInterface(
                    type,
                    typeof(IReadOnlyList<>)
                );
                metadata.ReadOnlyListIndexer = metadata.ReadOnlyListInterface?.GetProperty("Item");
            }

            interfaceType = metadata.ReadOnlyListInterface!;
            indexer = metadata.ReadOnlyListIndexer;
            return metadata.ReadOnlyListInterface is not null;
        }
    }

    /// <summary>Gets the read-only dictionary interface with lookup members, if supported.</summary>
    internal static bool TryGetReadOnlyDictionary(
        Type type,
        out Type interfaceType,
        out MethodInfo? containsKey,
        out PropertyInfo? indexer
    )
    {
        var metadata = GetOrAdd(type);
        lock (metadata.Properties)
        {
            if (!metadata.ReadOnlyDictionaryInterfaceKnown)
            {
                metadata.ReadOnlyDictionaryInterfaceKnown = true;
                metadata.ReadOnlyDictionaryInterface = FindGenericInterface(
                    type,
                    typeof(IReadOnlyDictionary<,>)
                );
                metadata.ReadOnlyDictionaryContainsKey =
                    metadata.ReadOnlyDictionaryInterface?.GetMethod("ContainsKey");
                metadata.ReadOnlyDictionaryIndexer =
                    metadata.ReadOnlyDictionaryInterface?.GetProperty("Item");
            }

            interfaceType = metadata.ReadOnlyDictionaryInterface!;
            containsKey = metadata.ReadOnlyDictionaryContainsKey;
            indexer = metadata.ReadOnlyDictionaryIndexer;
            return metadata.ReadOnlyDictionaryInterface is not null;
        }
    }

    /// <summary>Reads the read-only collection count through the cached Count property.</summary>
    internal static bool TryGetReadOnlyCollectionCount(
        Type type,
        Type elementType,
        object collection,
        out int count
    )
    {
        var metadata = GetOrAdd(type);
        PropertyInfo? counter;
        lock (metadata.Properties)
        {
            if (!metadata.ReadOnlyCollectionInterfaceKnown)
            {
                metadata.ReadOnlyCollectionInterfaceKnown = true;
                metadata.ReadOnlyCollectionCount = null;
                foreach (var candidate in type.GetInterfaces())
                {
                    if (
                        candidate.IsGenericType
                        && candidate.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>)
                        && candidate.GetGenericArguments()[0] == elementType
                    )
                    {
                        metadata.ReadOnlyCollectionCount = candidate.GetProperty("Count");
                        break;
                    }
                }
            }

            counter = metadata.ReadOnlyCollectionCount;
        }

        if (counter?.GetValue(collection) is int value)
        {
            count = value;
            return true;
        }

        count = 0;
        return false;
    }

    /// <summary>Gets the dictionary key type, if any generic dictionary interface applies.</summary>
    internal static Type? GetDictionaryKeyType(Type type)
    {
        var metadata = GetOrAdd(type);
        lock (metadata.Properties)
        {
            if (!metadata.DictionaryKeyTypeKnown)
            {
                metadata.DictionaryKeyTypeKnown = true;
                metadata.DictionaryKeyType = null;
                foreach (var candidate in type.GetInterfaces())
                {
                    if (
                        candidate.IsGenericType
                        && (
                            candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                            || candidate.GetGenericTypeDefinition()
                                == typeof(IReadOnlyDictionary<,>)
                        )
                    )
                    {
                        metadata.DictionaryKeyType = candidate.GetGenericArguments()[0];
                        break;
                    }
                }
            }

            return metadata.DictionaryKeyType;
        }
    }

    /// <summary>Gets the assigned-key reader for an element type, if declared.</summary>
    internal static bool TryGetKeyReader(Type elementType, out KeyReaderInfo? reader)
    {
        var metadata = GetOrAdd(elementType);
        lock (metadata.Properties)
        {
            if (!metadata.AssignedKeyReaderKnown)
            {
                metadata.AssignedKeyReaderKnown = true;
                metadata.AssignedKeyReader = FindKeyReader(elementType);
            }

            reader = metadata.AssignedKeyReader;
            return reader is not null;
        }
    }

    /// <summary>Gets the temporary-key reader for an element type, if declared.</summary>
    internal static bool TryGetTemporaryReader(Type elementType, out Func<object, object?>? reader)
    {
        var metadata = GetOrAdd(elementType);
        lock (metadata.Properties)
        {
            if (!metadata.TemporaryKeyReaderKnown)
            {
                metadata.TemporaryKeyReaderKnown = true;
                metadata.TemporaryKeyReader = FindTemporaryReader(elementType);
            }

            reader = metadata.TemporaryKeyReader;
            return reader is not null;
        }
    }

    private static Type? FindSequenceElementType(Type type)
    {
        foreach (var candidate in type.GetInterfaces())
        {
            if (
                candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            )
            {
                return candidate.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static Type? FindGenericInterface(Type type, Type definition)
    {
        foreach (var candidate in type.GetInterfaces())
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == definition)
            {
                return candidate;
            }
        }

        return null;
    }

    private static KeyReaderInfo? FindKeyReader(Type elementType)
    {
        foreach (
            var property in elementType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
        )
        {
            if (
                property.CanRead
                && property.GetIndexParameters().Length == 0
                && property.GetCustomAttribute<SparseKeyAttribute>(inherit: true) is not null
            )
            {
                return new KeyReaderInfo
                {
                    GetKey = property.GetValue,
                    KeyType = property.PropertyType,
                };
            }
        }

        return null;
    }

    private static Func<object, object?>? FindTemporaryReader(Type elementType)
    {
        foreach (
            var property in elementType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
        )
        {
            if (
                property.CanRead
                && property.GetIndexParameters().Length == 0
                && property.GetCustomAttribute<SparseTemporaryKeyAttribute>(inherit: true)
                    is not null
            )
            {
                return property.GetValue;
            }
        }

        return null;
    }
}
