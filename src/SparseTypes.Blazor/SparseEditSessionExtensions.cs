using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.AspNetCore.Components.Forms;
using SparseTypes;

namespace SparseTypes.Blazor;

/// <summary>Blazor helpers for framework-neutral SparseTypes edit sessions.</summary>
public static class SparseEditSessionExtensions
{
    /// <summary>Creates an <see cref="EditContext"/> bound to the session's original model.</summary>
    /// <remarks>The model is obtained through trusted framework access.</remarks>
    public static EditContext CreateEditContext<TModel>(this ISparseEditSession<TModel> session)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(session);
        return new EditContext(GetSessionModel(session));
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
    /// <remarks>The session model instance is preserved; only the baseline advances.</remarks>
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
        {
            editContext.MarkAsUnmodified();
        }
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
    /// <remarks>Path resolution reads through trusted framework access and does not by itself disable the session's observable-change cache.</remarks>
    public static FieldIdentifier Field<TModel>(
        this ISparseEditSession<TModel> session,
        string fieldName
    )
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        return SparseFieldPathResolver.Resolve(GetSessionModel(session), fieldName);
    }

    /// <summary>Resolves a Blazor field identifier from a canonical typed path.</summary>
    /// <remarks>The typed walk carries actual key values, so distinct keys with identical display text never merge.</remarks>
    public static FieldIdentifier Field<TModel>(
        this ISparseEditSession<TModel> session,
        SparsePath path
    )
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(session);
        return SparseFieldPathResolver.Resolve(GetSessionModel(session), path);
    }

    /// <summary>Resolves a Blazor field identifier from a compile-time typed path.</summary>
    /// <param name="session">The edit session.</param>
    /// <param name="path">The typed path, for example <c>Order.SparsePath.Items.Key(id).Price</c>.</param>
    /// <typeparam name="TModel">The session model type.</typeparam>
    /// <typeparam name="TValue">The addressed value type.</typeparam>
    public static FieldIdentifier Field<TModel, TValue>(
        this ISparseEditSession<TModel> session,
        SparsePath<TModel, TValue> path
    )
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(session);
        return SparseFieldPathResolver.Resolve(GetSessionModel(session), path.Path);
    }

    /// <summary>Surfaces a validation message for a field belonging to this session's model.</summary>
    /// <remarks>Resolves fields against the active session model.</remarks>
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
        if (!IsSessionOwnedField(GetSessionModel(session), field.Model))
        {
            throw new ArgumentException(
                "The field must belong to the session's model.",
                nameof(field)
            );
        }

        store.Add(field, message);
    }

    /// <summary>Surfaces a validation message for a session model member path.</summary>
    /// <remarks>The path uses the same spelling as the string-based field lookup.</remarks>
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

    /// <summary>Surfaces a validation message for a canonical typed path.</summary>
    /// <param name="session">The edit session.</param>
    /// <param name="store">The validation store.</param>
    /// <param name="fieldPath">The typed path.</param>
    /// <param name="message">The message.</param>
    /// <typeparam name="TModel">The session model type.</typeparam>
    public static void AddValidationError<TModel>(
        this ISparseEditSession<TModel> session,
        ValidationMessageStore store,
        SparsePath fieldPath,
        string message
    )
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(message);

        store.Add(session.Field(fieldPath), message);
    }

    /// <summary>Surfaces a validation message for a compile-time typed path.</summary>
    /// <param name="session">The edit session.</param>
    /// <param name="store">The validation store.</param>
    /// <param name="fieldPath">The typed path.</param>
    /// <param name="message">The message.</param>
    /// <typeparam name="TModel">The session model type.</typeparam>
    /// <typeparam name="TValue">The addressed value type.</typeparam>
    public static void AddValidationError<TModel, TValue>(
        this ISparseEditSession<TModel> session,
        ValidationMessageStore store,
        SparsePath<TModel, TValue> fieldPath,
        string message
    )
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(message);

        store.Add(session.Field(fieldPath), message);
    }

    /// <summary>Creates a typed-path-first binding over the session observable and original-model context.</summary>
    /// <remarks>Reads resolve live so nested replacement is followed; removed keyed entries report stale.</remarks>
    /// <param name="session">The edit session.</param>
    /// <param name="path">The typed path, for example <c>Order.SparsePath.Name</c>.</param>
    /// <param name="editContext">The original-model edit context.</param>
    /// <param name="options">Optional batching and disabled state.</param>
    /// <typeparam name="TModel">The session model type.</typeparam>
    /// <typeparam name="TValue">The addressed value type.</typeparam>
    public static SparseFieldBinding<TModel, TValue> Bind<TModel, TValue>(
        this ISparseEditSession<TModel> session,
        SparsePath<TModel, TValue> path,
        EditContext editContext,
        SparseFieldBindingOptions? options = null
    )
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(editContext);
        ValidateEditContext(session, editContext);
        if (path.Path.IsRoot)
        {
            throw new ArgumentException("The path must address a model member.", nameof(path));
        }

        ValidateBindPath(typeof(TModel), path.Path, nameof(path));

        return new SparseFieldBinding<TModel, TValue>(session, editContext, path, options);
    }

    /// <summary>Creates a model-agnostic binding over a canonical typed path.</summary>
    /// <param name="session">The edit session.</param>
    /// <param name="path">The canonical path.</param>
    /// <param name="editContext">The original-model edit context.</param>
    /// <param name="options">Optional batching and disabled state.</param>
    /// <typeparam name="TModel">The session model type.</typeparam>
    public static SparseFieldBinding<TModel, object?> Bind<TModel>(
        this ISparseEditSession<TModel> session,
        SparsePath path,
        EditContext editContext,
        SparseFieldBindingOptions? options = null
    )
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(editContext);
        ValidateEditContext(session, editContext);
        if (path.RootType != typeof(TModel))
        {
            throw new ArgumentException(
                "The path root does not match the session model.",
                nameof(path)
            );
        }

        if (path.IsRoot)
        {
            throw new ArgumentException("The path must address a model member.", nameof(path));
        }

        ValidateBindPath(typeof(TModel), path, nameof(path));

        return new SparseFieldBinding<TModel, object?>(session, editContext, path, options);
    }

    /// <summary>Creates a model-agnostic binding from a wire-compatible field path.</summary>
    /// <remarks>String paths parse once; prefer the typed overload for the trim-friendly path.</remarks>
    /// <param name="session">The edit session.</param>
    /// <param name="fieldPath">The field path text.</param>
    /// <param name="editContext">The original-model edit context.</param>
    /// <param name="options">Optional batching and disabled state.</param>
    /// <typeparam name="TModel">The session model type.</typeparam>
    public static SparseFieldBinding<TModel, object?> Bind<TModel>(
        this ISparseEditSession<TModel> session,
        string fieldPath,
        EditContext editContext,
        SparseFieldBindingOptions? options = null
    )
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrEmpty(fieldPath);
        ArgumentNullException.ThrowIfNull(editContext);
        ValidateEditContext(session, editContext);
        if (!SparsePath.TryParse(typeof(TModel), fieldPath, out var path))
        {
            throw new ArgumentException(
                $"The field path '{fieldPath}' does not resolve to a public model member.",
                nameof(fieldPath)
            );
        }

        try
        {
            ValidateBindPath(typeof(TModel), path, nameof(fieldPath));
        }
        catch (ArgumentException exception) when (exception.ParamName == "path")
        {
            throw new ArgumentException(
                $"The field path '{fieldPath}' does not resolve to a public model member.",
                nameof(fieldPath)
            );
        }

        return new SparseFieldBinding<TModel, object?>(session, editContext, path, options);
    }

    private static void ValidateBindPath(Type root, SparsePath path, string paramName)
    {
        // Unknown members fail at bind time, while missing keys and null
        // ancestors stay stale-but-bindable (live resolution decides).
        var current = root;
        foreach (var segment in path.Segments)
        {
            switch (segment.Kind)
            {
                case SparsePathSegmentKind.Member:
                    var property = current.GetProperty(
                        segment.Name,
                        BindingFlags.Instance | BindingFlags.Public
                    );
                    if (property is null || property.GetIndexParameters().Length > 0)
                    {
                        throw new ArgumentException(
                            $"The field path '{path}' does not resolve to a public model member.",
                            paramName
                        );
                    }

                    current =
                        Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                    break;
                case SparsePathSegmentKind.Key:
                case SparsePathSegmentKind.TemporaryKey:
                    current = StepBindCollection(current, path, paramName, keyed: true);
                    break;
                default:
                    current = StepBindCollection(current, path, paramName, keyed: false);
                    break;
            }
        }
    }

    private static Type StepBindCollection(
        Type current,
        SparsePath path,
        string paramName,
        bool keyed
    )
    {
        if (current == typeof(string))
        {
            throw InvalidBindPath(path, paramName);
        }

        foreach (var candidate in current.GetInterfaces())
        {
            if (!candidate.IsGenericType)
            {
                continue;
            }

            var definition = candidate.GetGenericTypeDefinition();
            if (
                definition == typeof(IDictionary<,>)
                || definition == typeof(IReadOnlyDictionary<,>)
            )
            {
                return Nullable.GetUnderlyingType(candidate.GetGenericArguments()[1])
                    ?? candidate.GetGenericArguments()[1];
            }
        }

        var element = SequenceElementType(current);
        if (element is null)
        {
            if (current.IsArray)
            {
                return current.GetElementType()!;
            }

            throw InvalidBindPath(path, paramName);
        }

        if (keyed && !HasSparseKey(element))
        {
            throw InvalidBindPath(path, paramName);
        }

        return Nullable.GetUnderlyingType(element) ?? element;
    }

    private static Type? SequenceElementType(Type current)
    {
        foreach (var candidate in current.GetInterfaces())
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

    private static bool HasSparseKey(Type element)
    {
        foreach (var property in element.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (
                property.CanRead
                && property.GetIndexParameters().Length == 0
                && property.GetCustomAttribute<SparseKeyAttribute>(inherit: true) is not null
            )
            {
                return true;
            }
        }

        return false;
    }

    private static ArgumentException InvalidBindPath(SparsePath path, string paramName) =>
        new($"The field path '{path}' does not resolve to a public model member.", paramName);

    private static void ValidateEditContext<TModel>(
        ISparseEditSession<TModel> session,
        EditContext editContext
    )
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(editContext);
        if (!ReferenceEquals(editContext.Model, GetSessionModel(session)))
        {
            throw new ArgumentException(
                "The EditContext must be bound to the session's model.",
                nameof(editContext)
            );
        }
    }

    private static TModel GetSessionModel<TModel>(ISparseEditSession<TModel> session)
        where TModel : class =>
        // Trusted framework access keeps the observable-change cache intact.
        session is ISparseEditSessionModelAccessor<TModel> accessor
            ? accessor.GetModelForFrameworkAccess()
            : session.Model;

    private static bool TryGetReadOnlyDictionaryValues(object collection, out IEnumerable? values)
    {
        if (
            SparseFieldPathResolver.TryGetReadOnlyDictionaryInterface(
                collection,
                out var interfaceType
            )
        )
        {
            values = interfaceType.GetProperty("Values")?.GetValue(collection) as IEnumerable;
            return values is not null;
        }

        values = null;
        return false;
    }

    private static bool IsSessionOwnedField(object sessionModel, object fieldModel)
    {
        if (ReferenceEquals(fieldModel, sessionModel))
        {
            return true;
        }

        // Reference walk over the live graph.
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
        else if (
            TryGetReadOnlyDictionaryValues(parent, out var readOnlyValues)
            && readOnlyValues is not null
        )
        {
            // KeyValuePair enumerables expose structs only, so read-only
            // dictionary values are visited through the Values view instead.
            foreach (var value in readOnlyValues)
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
