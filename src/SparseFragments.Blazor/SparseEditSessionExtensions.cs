using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using SparseFragments;

namespace SparseFragments.Blazor;

/// <summary>Blazor helpers for framework-neutral SparseFragments edit sessions.</summary>
public static class SparseEditSessionExtensions
{
    /// <summary>Creates an <see cref="EditContext"/> bound to the session's original model.</summary>
    public static EditContext CreateEditContext<TModel>(this ISparseEditSession<TModel> session)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(session);
        return new EditContext(session.Model);
    }

    /// <summary>Accepts the current model state and clears the associated Blazor modified state.</summary>
    public static void AcceptChanges<TModel>(
        this ISparseEditSession<TModel> session,
        EditContext editContext
    )
        where TModel : class
    {
        ValidateEditContext(session, editContext);
        session.AcceptChanges();
        editContext.MarkAsUnmodified();
    }

    /// <summary>Accepts a previously generated change set and synchronizes Blazor modified state.</summary>
    /// <remarks>
    /// The session model instance is preserved; only the baseline advances. Later live
    /// edits remain pending, so the context is cleared only when the session is clean.
    /// </remarks>
    public static void AcceptChanges<TModel, TChangeSet>(
        this ISparseEditSession<TModel, TChangeSet> session,
        EditContext editContext,
        TChangeSet changes
    )
        where TModel : class
        where TChangeSet : class
    {
        ValidateEditContext(session, editContext);
        session.AcceptChanges(changes);
        if (!session.HasChanges)
            editContext.MarkAsUnmodified();
    }

    /// <summary>Creates a validation store bound to the supplied session edit context.</summary>
    public static ValidationMessageStore CreateValidationStore<TModel>(
        this ISparseEditSession<TModel> session,
        EditContext editContext
    )
        where TModel : class
    {
        ValidateEditContext(session, editContext);
        return new ValidationMessageStore(editContext);
    }

    /// <summary>Resolves a Blazor field identifier for a member of the session model.</summary>
    public static FieldIdentifier Field<TModel>(
        this ISparseEditSession<TModel> session,
        string fieldName
    )
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        var model = session.Model;
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

                var key = ParseFieldKey(fieldName[keyStart..index], fieldName);
                index++;
                current = ResolveIndexedValue(current, key, fieldName);
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

    /// <summary>Surfaces a validation message for a field belonging to this session's model.</summary>
    /// <remarks>
    /// Fields resolved from this session (root, nested, list-element, and
    /// dictionary-value paths) are accepted when their model instance is still
    /// reachable from the session model. Fields from unrelated graphs stay rejected.
    /// </remarks>
    public static void AddValidationError<TModel>(
        this ISparseEditSession<TModel> session,
        ValidationMessageStore store,
        FieldIdentifier field,
        string message
    )
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(message);
        if (!IsSessionOwnedField(session.Model, field.Model))
        {
            throw new ArgumentException(
                "The field must belong to the session's model.",
                nameof(field)
            );
        }

        store.Add(field, message);
    }

    /// <summary>Surfaces a validation message for a session model member path.</summary>
    /// <remarks>
    /// The path uses the same spelling as <see cref="Field{TModel}"/>, so nested,
    /// indexed, and keyed members resolve without handing a <c>FieldIdentifier</c>
    /// across model graphs.
    /// </remarks>
    public static void AddValidationError<TModel>(
        this ISparseEditSession<TModel> session,
        ValidationMessageStore store,
        string fieldPath,
        string message
    )
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrEmpty(fieldPath);
        ArgumentNullException.ThrowIfNull(message);

        store.Add(session.Field(fieldPath), message);
    }

    private static void ValidateEditContext<TModel>(
        ISparseEditSession<TModel> session,
        EditContext editContext
    )
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(editContext);
        if (!ReferenceEquals(editContext.Model, session.Model))
        {
            throw new ArgumentException(
                "The EditContext must be bound to the session's model.",
                nameof(editContext)
            );
        }
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

    private static object? ResolveIndexedValue(object collection, string key, string path)
    {
        if (collection is IList list)
        {
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

    private static ArgumentException InvalidFieldPath(string path) =>
        new($"The field path '{path}' does not resolve to a public model member.", nameof(path));

    private static bool IsSessionOwnedField(object sessionModel, object fieldModel)
    {
        if (ReferenceEquals(fieldModel, sessionModel))
        {
            return true;
        }

        // Reference walk over the live graph: everything reachable through public
        // members and collections belongs to this session, while sibling graphs
        // and detached instances are never visited.
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>();
        pending.Push(sessionModel);
        visited.Add(sessionModel);
        while (pending.Count > 0)
        {
            foreach (var child in EnumerateChildReferences(pending.Pop()))
            {
                if (ReferenceEquals(child, fieldModel))
                {
                    return true;
                }

                if (visited.Add(child))
                {
                    pending.Push(child);
                }
            }
        }

        return false;
    }

    private static IEnumerable<object> EnumerateChildReferences(object parent)
    {
        foreach (
            var property in parent
                .GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        )
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var propertyChild = ToChildReference(property.GetValue(parent));
            if (propertyChild is not null)
            {
                yield return propertyChild;
            }
        }

        if (parent is IDictionary dictionary)
        {
            foreach (var value in dictionary.Values)
            {
                var valueChild = ToChildReference(value);
                if (valueChild is not null)
                {
                    yield return valueChild;
                }
            }
        }
        else if (parent is IEnumerable enumerable and not string)
        {
            foreach (var element in enumerable)
            {
                var elementChild = ToChildReference(element);
                if (elementChild is not null)
                {
                    yield return elementChild;
                }
            }
        }
    }

    private static object? ToChildReference(object? value) =>
        value is null or string || value.GetType().IsValueType ? null : value;
}
