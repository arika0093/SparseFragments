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
    public static EditContext CreateEditContext<TModel, TFragment, TPatch, TChangeSet, TObservable>(
        this SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable> session
    )
        where TModel : class
        where TFragment : class
        where TPatch : class
        where TChangeSet : class
        where TObservable : class
    {
        ArgumentNullException.ThrowIfNull(session);
        return new EditContext(session.Model);
    }

    /// <summary>Accepts the current model state and clears the associated Blazor modified state.</summary>
    public static void AcceptChanges<TModel, TFragment, TPatch, TChangeSet, TObservable>(
        this SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable> session,
        EditContext editContext
    )
        where TModel : class
        where TFragment : class
        where TPatch : class
        where TChangeSet : class
        where TObservable : class
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
    public static void AcceptChanges<TModel, TFragment, TPatch, TChangeSet, TObservable>(
        this SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable> session,
        EditContext editContext,
        TChangeSet changes
    )
        where TModel : class
        where TFragment : class
        where TPatch : class
        where TChangeSet : class
        where TObservable : class
    {
        ValidateEditContext(session, editContext);
        session.AcceptChanges(changes);
        if (!session.HasChanges)
            editContext.MarkAsUnmodified();
    }

    /// <summary>Creates a validation store bound to the supplied session edit context.</summary>
    public static ValidationMessageStore CreateValidationStore<
        TModel,
        TFragment,
        TPatch,
        TChangeSet,
        TObservable
    >(
        this SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable> session,
        EditContext editContext
    )
        where TModel : class
        where TFragment : class
        where TPatch : class
        where TChangeSet : class
        where TObservable : class
    {
        ValidateEditContext(session, editContext);
        return new ValidationMessageStore(editContext);
    }

    /// <summary>Resolves a Blazor field identifier for a member of the session model.</summary>
    public static FieldIdentifier Field<TModel, TFragment, TPatch, TChangeSet, TObservable>(
        this SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable> session,
        string fieldName
    )
        where TModel : class
        where TFragment : class
        where TPatch : class
        where TChangeSet : class
        where TObservable : class
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
    public static void AddValidationError<TModel, TFragment, TPatch, TChangeSet, TObservable>(
        this SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable> session,
        ValidationMessageStore store,
        FieldIdentifier field,
        string message
    )
        where TModel : class
        where TFragment : class
        where TPatch : class
        where TChangeSet : class
        where TObservable : class
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(message);
        if (!ReferenceEquals(field.Model, session.Model))
        {
            throw new ArgumentException(
                "The field must belong to the session's model.",
                nameof(field)
            );
        }

        store.Add(field, message);
    }

    private static void ValidateEditContext<TModel, TFragment, TPatch, TChangeSet, TObservable>(
        SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable> session,
        EditContext editContext
    )
        where TModel : class
        where TFragment : class
        where TPatch : class
        where TChangeSet : class
        where TObservable : class
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
            return keyType.IsEnum
                ? Enum.Parse(keyType, key, ignoreCase: false)
                : Convert.ChangeType(key, keyType, CultureInfo.InvariantCulture);
        }
        catch (Exception exception)
            when (exception is ArgumentException or FormatException or InvalidCastException)
        {
            throw InvalidFieldPath(path);
        }
    }

    private static ArgumentException InvalidFieldPath(string path) =>
        new($"The field path '{path}' does not resolve to a public model member.", nameof(path));
}
