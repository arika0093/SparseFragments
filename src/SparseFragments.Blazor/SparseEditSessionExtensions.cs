using System;
using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Components.Forms;
using SparseFragments;

namespace SparseFragments.Blazor;

/// <summary>Blazor helpers for framework-neutral SparseFragments edit sessions.</summary>
public static class SparseEditSessionExtensions
{
    /// <summary>Creates an <see cref="EditContext"/> bound to the session's original model.</summary>
    /// <remarks>
    /// The model is obtained through trusted framework access, so creating the
    /// context does not by itself disable the session's observable-change cache.
    /// </remarks>
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
    /// <remarks>
    /// Path resolution reads through trusted framework access and does not by
    /// itself disable the session's observable-change cache. List brackets take
    /// positional indexes (<c>Lines[1]</c>) or, for keyed element types, quoted
    /// stable keys (<c>Lines["b"]</c>) matching the change-enumeration spelling.
    /// Quoted keys never act as positions, and removed keys fail as invalid paths.
    /// </remarks>
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
        // Trusted framework access keeps the observable-change cache intact;
        // sessions without it fall back to the raw model, which disables caching.
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
