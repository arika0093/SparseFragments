using System.Collections;
using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Components.Forms;
using SparseTypes;

namespace SparseTypes.Blazor;

/// <summary>Resolves typed paths against a session model instance.</summary>
internal static class SparseFieldPathResolver
{
    // Carries the failure spelling without formatting it: wire text renders
    // only when a lookup actually fails.
    private readonly struct ErrorName(SparsePath path, string? text)
    {
        public string GetText() => text ?? path.ToString();
    }

    /// <summary>Resolves a wire-compatible field path.</summary>
    /// <remarks>String paths parse to <see cref="SparsePath"/> once, then resolve through the typed walk.</remarks>
    internal static FieldIdentifier Resolve(object model, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        if (!SparsePath.TryParse(model.GetType(), fieldName, out var path))
        {
            throw InvalidFieldPath(fieldName);
        }

        return Resolve(model, path, new ErrorName(path, fieldName));
    }

    /// <summary>Resolves a canonical typed path.</summary>
    internal static FieldIdentifier Resolve(object model, SparsePath path)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Resolve(model, path, new ErrorName(path, null));
    }

    private static FieldIdentifier Resolve(object model, SparsePath path, ErrorName error)
    {
        if (path.IsRoot)
        {
            throw InvalidFieldPath(error);
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
                        throw InvalidFieldPath(error);
                    }

                    fieldNamePart = segment.Name;
                    fieldOwner = current;
                    if (
                        !SparseTypeMetadataCache.TryGetProperty(
                            current.GetType(),
                            fieldNamePart,
                            out var property
                        )
                        || property is null
                        || !property.CanRead
                    )
                    {
                        throw InvalidFieldPath(error);
                    }

                    current = property.GetValue(current);
                    break;
                case SparsePathSegmentKind.Key:
                    if (current is null)
                    {
                        throw InvalidFieldPath(error);
                    }

                    // Trailing entry segments validate resolvability while the
                    // reported field stays the owning member.
                    current = ResolveKeyedValue(current, segment.Key, error);
                    break;
                case SparsePathSegmentKind.TemporaryKey:
                    if (current is null)
                    {
                        throw InvalidFieldPath(error);
                    }

                    current = ResolveTemporaryValue(current, segment.TemporaryKey, error);
                    break;
                default:
                    if (current is null)
                    {
                        throw InvalidFieldPath(error);
                    }

                    current = ResolvePositionalValue(current, segment.Index, error);
                    break;
            }
        }

        if (fieldOwner is null || fieldNamePart is null)
        {
            throw InvalidFieldPath(error);
        }

        return new FieldIdentifier(fieldOwner, fieldNamePart);
    }

    private static object? ResolveKeyedValue(object collection, object? key, ErrorName error)
    {
        if (collection is string || key is null)
        {
            throw InvalidFieldPath(error);
        }

        // Key segments name stable entry identity (the EnumerateChanges
        // spelling), never positions.
        if (collection is IList list)
        {
            return ResolveKeyedElement(
                list,
                SparseTypeMetadataCache.GetSequenceElementType(collection.GetType()),
                key,
                error
            );
        }

        if (collection is IDictionary dictionary)
        {
            var dictionaryKey = key is string text
                ? ConvertDictionaryKey(collection, text, error)
                : key;
            if (!dictionary.Contains(dictionaryKey))
            {
                throw InvalidFieldPath(error);
            }

            return dictionary[dictionaryKey];
        }

        var collectionType = collection.GetType();
        if (
            SparseTypeMetadataCache.TryGetReadOnlyDictionary(
                collectionType,
                out var dictionaryInterface,
                out var containsKey,
                out var indexer
            )
        )
        {
            var keyType = dictionaryInterface.GetGenericArguments()[0];
            var dictionaryKey = key is string text
                ? ConvertKeyTextOrInvalidPath(keyType, text, error)
                : key;
            if (
                containsKey is null
                || indexer is null
                || containsKey.Invoke(collection, [dictionaryKey]) is not true
            )
            {
                throw InvalidFieldPath(error);
            }

            return indexer.GetValue(collection, [dictionaryKey]);
        }

        if (
            SparseTypeMetadataCache.TryGetReadOnlyList(collectionType, out var listInterface, out _)
        )
        {
            return ResolveKeyedElement(
                (IEnumerable)collection,
                listInterface.GetGenericArguments()[0],
                key,
                error
            );
        }

        throw InvalidFieldPath(error);
    }

    private static object? ResolvePositionalValue(object collection, int index, ErrorName error)
    {
        if (collection is string || index < 0)
        {
            throw InvalidFieldPath(error);
        }

        if (collection is IList list)
        {
            if ((uint)index >= (uint)list.Count)
            {
                throw InvalidFieldPath(error);
            }

            return list[index];
        }

        var collectionType = collection.GetType();
        if (
            SparseTypeMetadataCache.TryGetReadOnlyList(
                collectionType,
                out var listInterface,
                out var indexer
            )
        )
        {
            // Index lookup, not linear enumeration.
            var elementType = listInterface.GetGenericArguments()[0];
            if (
                indexer is null
                || !SparseTypeMetadataCache.TryGetReadOnlyCollectionCount(
                    collectionType,
                    elementType,
                    collection,
                    out var count
                )
            )
            {
                throw InvalidFieldPath(error);
            }

            if ((uint)index >= (uint)count)
            {
                throw InvalidFieldPath(error);
            }

            return indexer.GetValue(collection, [index]);
        }

        if (collection is IDictionary || TryGetReadOnlyDictionaryInterface(collection, out _))
        {
            // Historical spelling accepts unquoted numerics as dictionary keys,
            // so index segments fall back to invariant key text here.
            return ResolveKeyedValue(
                collection,
                index.ToString(CultureInfo.InvariantCulture),
                error
            );
        }

        throw InvalidFieldPath(error);
    }

    private static object ConvertDictionaryKey(object collection, string key, ErrorName error)
    {
        if (collection is not IDictionary)
        {
            throw InvalidFieldPath(error);
        }

        var keyType = SparseTypeMetadataCache.GetDictionaryKeyType(collection.GetType());
        if (keyType is null || keyType == typeof(string))
        {
            return key;
        }

        return ConvertKeyTextOrInvalidPath(keyType, key, error);
    }

    private static object ConvertKeyTextOrInvalidPath(Type keyType, string key, ErrorName error)
    {
        try
        {
            return ConvertKeyText(keyType, key, error);
        }
        catch (Exception exception)
            when (exception is ArgumentException or FormatException or InvalidCastException)
        {
            throw InvalidFieldPath(error);
        }
    }

    private static object ConvertKeyText(Type keyType, string key, ErrorName error)
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

            throw InvalidFieldPath(error);
        }

        return keyType.IsEnum
            ? Enum.Parse(keyType, key, ignoreCase: false)
            : Convert.ChangeType(key, keyType, CultureInfo.InvariantCulture);
    }

    private static object ResolveTemporaryValue(
        object collection,
        Guid? temporaryKey,
        ErrorName error
    )
    {
        // The element is located through its SparseTemporaryKey member, so the
        // lookup follows row replacement exactly like the keyed lookup does.
        if (collection is string || temporaryKey is null)
        {
            throw InvalidFieldPath(error);
        }

        if (collection is not IEnumerable elements)
        {
            throw InvalidFieldPath(error);
        }

        var elementType =
            SparseTypeMetadataCache.GetSequenceElementType(collection.GetType())
            ?? FirstElementType(elements);
        if (
            elementType is null
            || !SparseTypeMetadataCache.TryGetTemporaryReader(elementType, out var reader)
            || reader is null
        )
        {
            throw InvalidFieldPath(error);
        }

        foreach (var element in elements)
        {
            if (element is not null && Equals(reader(element), temporaryKey.Value))
            {
                return element;
            }
        }

        // Removed or assigned entries no longer resolve against the live model.
        throw InvalidFieldPath(error);
    }

    private static object ResolveKeyedElement(
        IEnumerable elements,
        Type? elementType,
        object? key,
        ErrorName error
    )
    {
        // Key segments name stable keys (the EnumerateChanges spelling),
        // never positions: even a numeric string key looks up key identity.
        elementType ??= FirstElementType(elements);
        if (
            elementType is null
            || !SparseTypeMetadataCache.TryGetKeyReader(elementType, out var reader)
            || reader is null
        )
        {
            throw InvalidFieldPath(error);
        }

        // Parsed string keys convert to the key type; typed path keys compare
        // directly so equal display text never merges distinct keys.
        var convertedKey = key is string text
            ? ConvertKeyTextOrInvalidPath(reader.KeyType, text, error)
            : key;
        foreach (var element in elements)
        {
            if (element is not null && Equals(reader.GetKey(element), convertedKey))
            {
                return element;
            }
        }

        // Removed keys no longer resolve against the live model.
        throw InvalidFieldPath(error);
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

    internal static bool TryGetReadOnlyDictionaryInterface(
        object collection,
        out Type interfaceType
    )
    {
        ArgumentNullException.ThrowIfNull(collection);
        if (
            SparseTypeMetadataCache.TryGetReadOnlyDictionary(
                collection.GetType(),
                out interfaceType,
                out _,
                out _
            )
        )
        {
            return true;
        }

        interfaceType = null!;
        return false;
    }

    private static ArgumentException InvalidFieldPath(ErrorName error) =>
        InvalidFieldPath(error.GetText());

    private static ArgumentException InvalidFieldPath(string path) =>
        new($"The field path '{path}' does not resolve to a public model member.", nameof(path));
}
