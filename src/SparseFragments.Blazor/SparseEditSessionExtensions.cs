using System;
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
        return new FieldIdentifier(session.Model, fieldName);
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
}
