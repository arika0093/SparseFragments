using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Components.Forms;
using SparseFragments;

namespace SparseFragments.Blazor;

/// <summary>Resolves typed paths against a session model instance.</summary>
internal static class SparseFieldPathResolver
{
    /// <summary>Resolves a wire-compatible field path.</summary>
    /// <remarks>String paths parse to <see cref="SparsePath"/> once, then resolve through the typed walk.</remarks>
    internal static FieldIdentifier Resolve(object model, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        if (!SparsePath.TryParse(model.GetType(), fieldName, out var path) || path is null)
        {
            throw InvalidFieldPath(fieldName);
        }

        return Resolve(model, path, fieldName);
    }

    /// <summary>Resolves a canonical typed path.</summary>
    internal static FieldIdentifier Resolve(object model, SparsePath path)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(path);
        return Resolve(model, path, path.ToString());
    }

    private static FieldIdentifier Resolve(object model, SparsePath path, string errorPath)
    {
        if (path.IsRoot)
        {
            throw InvalidFieldPath(errorPath);
        }

        object? current = model;
        object? fieldOwner = null;
        string? fieldNamePart = null;
        foreach (var segment in path.Segments)
        {
            switch (segment.Kind)
            {
                case SparsePathSegmentKind.Member:
                    if (current is null)
                    {
                        throw InvalidFieldPath(errorPath);
                    }

                    fieldNamePart = segment.Name;
                    fieldOwner = current;
                    var property = current
                        .GetType()
                        .GetProperty(fieldNamePart, BindingFlags.Instance | BindingFlags.Public);
                    if (property is null || property.GetIndexParameters().Length > 0)
                    {
                        throw InvalidFieldPath(errorPath);
                    }

                    current = property.GetValue(current);
                    break;
                case SparsePathSegmentKind.Key:
                    if (current is null)
                    {
                        throw InvalidFieldPath(errorPath);
                    }

                    // Trailing entry segments validate resolvability while the
                    // reported field stays the owning member, matching the
                    // historical bracket spelling.
                    current = ResolveKeyedValue(current, segment.Key, errorPath);
                    break;
                default:
                    if (current is null)
                    {
                        throw InvalidFieldPath(errorPath);
                    }

                    current = ResolvePositionalValue(current, segment.Index, errorPath);
                    break;
            }
        }

        if (fieldOwner is null || fieldNamePart is null)
        {
            throw InvalidFieldPath(errorPath);
        }

        return new FieldIdentifier(fieldOwner, fieldNamePart);
    }

    private static object? ResolveKeyedValue(object collection, object? key, string path)
    {
        if (collection is string || key is null)
        {
            throw InvalidFieldPath(path);
        }

        // Key segments name stable entry identity (the EnumerateChanges
        // spelling), never positions: even a numeric string key looks up key
        // identity, while positions use index segments.
        if (collection is IList list)
        {
            return ResolveKeyedElement(list, GetSequenceElementType(collection), key, path);
        }

        if (collection is IDictionary dictionary)
        {
            var dictionaryKey = key is string text
                ? ConvertDictionaryKey(collection, text, path)
                : key;
            if (!dictionary.Contains(dictionaryKey))
            {
                throw InvalidFieldPath(path);
            }

            return dictionary[dictionaryKey];
        }

        if (TryGetReadOnlyDictionaryInterface(collection, out var dictionaryInterface))
        {
            var keyType = dictionaryInterface.GetGenericArguments()[0];
            var dictionaryKey = key is string text
                ? ConvertKeyTextOrInvalidPath(keyType, text, path)
                : key;
            var containsKey = dictionaryInterface.GetMethod("ContainsKey");
            var indexer = dictionaryInterface.GetProperty("Item");
            if (
                containsKey is null
                || indexer is null
                || containsKey.Invoke(collection, [dictionaryKey]) is not true
            )
            {
                throw InvalidFieldPath(path);
            }

            return indexer.GetValue(collection, [dictionaryKey]);
        }

        if (TryGetReadOnlyListInterface(collection, out var listInterface))
        {
            return ResolveKeyedElement(
                (IEnumerable)collection,
                listInterface.GetGenericArguments()[0],
                key,
                path
            );
        }

        throw InvalidFieldPath(path);
    }

    private static object? ResolvePositionalValue(object collection, int index, string path)
    {
        if (collection is string || index < 0)
        {
            throw InvalidFieldPath(path);
        }

        if (collection is IList list)
        {
            if ((uint)index >= (uint)list.Count)
            {
                throw InvalidFieldPath(path);
            }

            return list[index];
        }

        if (TryGetReadOnlyListInterface(collection, out var listInterface))
        {
            // Index lookup, not linear enumeration.
            var elementType = listInterface.GetGenericArguments()[0];
            var indexer = listInterface.GetProperty("Item");
            if (
                indexer is null
                || !TryGetReadOnlyCollectionCount(collection, elementType, out var count)
            )
            {
                throw InvalidFieldPath(path);
            }

            if ((uint)index >= (uint)count)
            {
                throw InvalidFieldPath(path);
            }

            return indexer.GetValue(collection, [index]);
        }

        if (collection is IDictionary || TryGetReadOnlyDictionaryInterface(collection, out _))
        {
            // Historical spelling accepts unquoted numerics as dictionary
            // keys, so index segments fall back to invariant key text here.
            // Positions stay list-only; dictionaries never gain positional
            // identity from this fallback.
            return ResolveKeyedValue(
                collection,
                index.ToString(CultureInfo.InvariantCulture),
                path
            );
        }

        throw InvalidFieldPath(path);
    }

    private static object ConvertDictionaryKey(object collection, string key, string path)
    {
        if (collection is not IDictionary)
        {
            throw InvalidFieldPath(path);
        }

        var keyType = collection
            .GetType()
            .GetInterfaces()
            .Where(static type =>
                type.IsGenericType
                && (
                    type.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                    || type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
                )
            )
            .Select(static type => type.GetGenericArguments()[0])
            .FirstOrDefault();
        if (keyType is null || keyType == typeof(string))
        {
            return key;
        }

        return ConvertKeyTextOrInvalidPath(keyType, key, path);
    }

    private static object ConvertKeyTextOrInvalidPath(Type keyType, string key, string path)
    {
        try
        {
            return ConvertKeyText(keyType, key, path);
        }
        catch (Exception exception)
            when (exception is ArgumentException or FormatException or InvalidCastException)
        {
            throw InvalidFieldPath(path);
        }
    }

    private static object ConvertKeyText(Type keyType, string key, string path)
    {
        // Guid is not IConvertible, so Convert.ChangeType cannot parse it.
        // Guid.TryParse is culture-independent.
        var targetType = Nullable.GetUnderlyingType(keyType) ?? keyType;
        if (targetType == typeof(Guid))
        {
            if (Guid.TryParse(key, out var guid))
            {
                return guid;
            }

            throw InvalidFieldPath(path);
        }

        return keyType.IsEnum
            ? Enum.Parse(keyType, key, ignoreCase: false)
            : Convert.ChangeType(key, keyType, CultureInfo.InvariantCulture);
    }

    private sealed record KeyReader
    {
        public required Func<object, object?> GetKey { get; init; }

        public required Type KeyType { get; init; }
    }

    private static object ResolveKeyedElement(
        IEnumerable elements,
        Type? elementType,
        object? key,
        string path
    )
    {
        // Key segments name stable keys (the EnumerateChanges spelling),
        // never positions: even a numeric string key looks up key identity.
        elementType ??= FirstElementType(elements);
        if (elementType is null || !TryGetKeyReader(elementType, out var reader))
        {
            throw InvalidFieldPath(path);
        }

        // Parsed string keys convert to the key type; typed path keys compare
        // directly so equal display text never merges distinct keys.
        var convertedKey = key is string text
            ? ConvertKeyTextOrInvalidPath(reader.KeyType, text, path)
            : key;
        foreach (var element in elements)
        {
            if (element is not null && Equals(reader.GetKey(element), convertedKey))
            {
                return element;
            }
        }

        // Removed keys no longer resolve against the live model.
        throw InvalidFieldPath(path);
    }

    private static Type? GetSequenceElementType(object collection)
    {
        foreach (var candidate in collection.GetType().GetInterfaces())
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

    private static Type? FirstElementType(IEnumerable elements)
    {
        foreach (var element in elements)
        {
            if (element is not null)
            {
                return element.GetType();
            }
        }

        return null;
    }

    private static bool TryGetKeyReader(Type elementType, out KeyReader reader)
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
                reader = new KeyReader
                {
                    GetKey = property.GetValue,
                    KeyType = property.PropertyType,
                };
                return true;
            }
        }

        var keyAttribute = elementType.GetCustomAttribute<SparseKeyAttribute>(inherit: true);
        if (keyAttribute is not null)
        {
            if (keyAttribute.PropertyNames.Length != 1)
            {
                // Composite keys have no single-segment field spelling.
                reader = null!;
                return false;
            }

            var property = elementType.GetProperty(
                keyAttribute.PropertyNames[0],
                BindingFlags.Instance | BindingFlags.Public
            );
            if (
                property is not null
                && property.CanRead
                && property.GetIndexParameters().Length == 0
            )
            {
                reader = new KeyReader
                {
                    GetKey = property.GetValue,
                    KeyType = property.PropertyType,
                };
                return true;
            }

            reader = null!;
            return false;
        }

        foreach (var candidate in elementType.GetInterfaces())
        {
            if (
                candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(ISparseKeyed<>)
            )
            {
                var sparseKey = candidate.GetProperty("SparseKey");
                if (sparseKey is not null)
                {
                    reader = new KeyReader
                    {
                        GetKey = sparseKey.GetValue,
                        KeyType = candidate.GetGenericArguments()[0],
                    };
                    return true;
                }
            }
        }

        reader = null!;
        return false;
    }

    internal static bool TryGetReadOnlyDictionaryInterface(
        object collection,
        out Type interfaceType
    )
    {
        foreach (var candidate in collection.GetType().GetInterfaces())
        {
            if (
                candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
            )
            {
                interfaceType = candidate;
                return true;
            }
        }

        interfaceType = null!;
        return false;
    }

    private static bool TryGetReadOnlyListInterface(object collection, out Type interfaceType)
    {
        foreach (var candidate in collection.GetType().GetInterfaces())
        {
            if (
                candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
            )
            {
                interfaceType = candidate;
                return true;
            }
        }

        interfaceType = null!;
        return false;
    }

    private static bool TryGetReadOnlyCollectionCount(
        object collection,
        Type elementType,
        out int count
    )
    {
        // Count is declared on IReadOnlyCollection<T>, which interface
        // reflection does not flatten onto IReadOnlyList<T>.
        foreach (var candidate in collection.GetType().GetInterfaces())
        {
            if (
                candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>)
                && candidate.GetGenericArguments()[0] == elementType
                && candidate.GetProperty("Count")?.GetValue(collection) is int value
            )
            {
                count = value;
                return true;
            }
        }

        count = 0;
        return false;
    }

    private static ArgumentException InvalidFieldPath(string path) =>
        new($"The field path '{path}' does not resolve to a public model member.", nameof(path));
}
