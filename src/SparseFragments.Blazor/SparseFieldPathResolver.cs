using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using SparseFragments;

namespace SparseFragments.Blazor;

/// <summary>Resolves dotted field paths against a session model instance.</summary>
internal static class SparseFieldPathResolver
{
    internal static FieldIdentifier Resolve(object model, string fieldName)
    {
        object? current = model;
        object? fieldOwner;
        string fieldNamePart;
        var index = 0;
        while (index < fieldName.Length)
        {
            var start = index;
            while (index < fieldName.Length && fieldName[index] is not '.' and not '[')
            {
                index++;
            }

            if (start == index || current is null)
            {
                throw InvalidFieldPath(fieldName);
            }

            fieldNamePart = fieldName[start..index];
            fieldOwner = current;
            var property = current
                .GetType()
                .GetProperty(fieldNamePart, BindingFlags.Instance | BindingFlags.Public);
            if (property is null || property.GetIndexParameters().Length > 0)
            {
                throw InvalidFieldPath(fieldName);
            }

            current = property.GetValue(current);
            while (index < fieldName.Length && fieldName[index] == '[')
            {
                if (current is null)
                {
                    throw InvalidFieldPath(fieldName);
                }

                var keyStart = ++index;
                var insideQuotes = false;
                var escaped = false;
                while (index < fieldName.Length)
                {
                    var currentCharacter = fieldName[index];
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (insideQuotes && currentCharacter == '\\')
                    {
                        escaped = true;
                    }
                    else if (currentCharacter == '"')
                    {
                        insideQuotes = !insideQuotes;
                    }
                    else if (!insideQuotes && currentCharacter == ']')
                    {
                        break;
                    }

                    index++;
                }

                if (index == fieldName.Length || keyStart == index)
                {
                    throw InvalidFieldPath(fieldName);
                }

                var rawKey = fieldName[keyStart..index];
                var quoted = rawKey.Length > 0 && rawKey[0] == '"';
                var key = ParseFieldKey(rawKey, fieldName);
                index++;
                current = ResolveIndexedValue(current, key, quoted, fieldName);
            }

            if (index == fieldName.Length)
            {
                return new FieldIdentifier(fieldOwner, fieldNamePart);
            }

            if (fieldName[index] != '.')
            {
                throw InvalidFieldPath(fieldName);
            }

            index++;
        }

        throw InvalidFieldPath(fieldName);
    }

    private static string ParseFieldKey(string key, string path)
    {
        if (key[0] != '"')
        {
            return key;
        }

        try
        {
            return JsonSerializer.Deserialize<string>(key) ?? string.Empty;
        }
        catch (JsonException)
        {
            throw InvalidFieldPath(path);
        }
    }

    private static object? ResolveIndexedValue(
        object collection,
        string key,
        bool quoted,
        string path
    )
    {
        if (collection is string)
        {
            throw InvalidFieldPath(path);
        }

        if (collection is IList list)
        {
            if (quoted)
            {
                return ResolveKeyedElement(list, GetSequenceElementType(collection), key, path);
            }

            if (
                !int.TryParse(
                    key,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var listIndex
                )
                || (uint)listIndex >= (uint)list.Count
            )
            {
                throw InvalidFieldPath(path);
            }

            return list[listIndex];
        }

        if (collection is IDictionary dictionary)
        {
            var dictionaryKey = ConvertDictionaryKey(collection, key, path);
            if (!dictionary.Contains(dictionaryKey))
            {
                throw InvalidFieldPath(path);
            }

            return dictionary[dictionaryKey];
        }

        if (TryGetReadOnlyDictionaryInterface(collection, out var dictionaryInterface))
        {
            var keyType = dictionaryInterface.GetGenericArguments()[0];
            var dictionaryKey = ConvertKeyTextOrInvalidPath(keyType, key, path);
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
            // Index lookup, not linear enumeration: the position spelling
            // stays numeric-only here, while quoted stable keys resolve
            // through the keyed-collection lookup below.
            if (quoted)
            {
                return ResolveKeyedElement(
                    (IEnumerable)collection,
                    listInterface.GetGenericArguments()[0],
                    key,
                    path
                );
            }

            if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                throw InvalidFieldPath(path);
            }

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
        string key,
        string path
    )
    {
        // Quoted list segments name stable keys (the EnumerateChanges spelling),
        // never positions: even a numeric quoted key looks up key identity.
        elementType ??= FirstElementType(elements);
        if (elementType is null || !TryGetKeyReader(elementType, out var reader))
        {
            throw InvalidFieldPath(path);
        }

        var convertedKey = ConvertKeyTextOrInvalidPath(reader.KeyType, key, path);
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
