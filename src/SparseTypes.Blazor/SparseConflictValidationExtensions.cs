using Microsoft.AspNetCore.Components.Forms;

namespace SparseTypes.Blazor;

/// <summary>Fallback target for conflicts whose typed path no longer resolves.</summary>
public enum SparseConflictFallbackMode
{
    /// <summary>Use the nearest surviving parent member; model-level when none resolves.</summary>
    NearestParent = 0,

    /// <summary>Always surface unresolvable conflicts as model-level errors.</summary>
    Model = 1,
}

/// <summary>Options controlling conflict-to-validation projection.</summary>
public sealed record SparseConflictValidationOptions
{
    /// <summary>Gets the fallback target for unresolvable paths.</summary>
    public SparseConflictFallbackMode Fallback { get; init; } =
        SparseConflictFallbackMode.NearestParent;

    /// <summary>Gets whether the store is cleared before conflicts are added.</summary>
    public bool ClearStoreFirst { get; init; }

    /// <summary>Gets the per-conflict message factory, or null for the default message.</summary>
    public Func<SparseConflict, string>? MessageFactory { get; init; }

    /// <summary>Builds the validation message for one conflict.</summary>
    /// <param name="conflict">The conflict to describe.</param>
    /// <returns>The message; defaults name the path and kind so conflicts stay distinct from DataAnnotations.</returns>
    public string GetMessage(SparseConflict conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        if (MessageFactory is not null)
        {
            return MessageFactory(conflict);
        }

        var message =
            $"Conflict at '{conflict.PathText}' ({conflict.Kind}): the server kept a newer value.";
        return string.IsNullOrEmpty(conflict.Reason) ? message : $"{message} {conflict.Reason}";
    }
}

/// <summary>Projects structured rebase conflicts into Blazor validation messages.</summary>
public static class SparseConflictValidationExtensions
{
    /// <summary>Surfaces rebase conflicts as validation errors and notifies the context.</summary>
    /// <remarks>
    /// The store is preserved by default; set <see cref="SparseConflictValidationOptions.ClearStoreFirst"/>
    /// to clear first. Use a dedicated conflict store so clearing never drops DataAnnotations messages,
    /// which live in the validator's own store. Unresolvable paths fall back deterministically and are
    /// never dropped. The call notifies <paramref name="editContext"/> when anything changed.
    /// </remarks>
    /// <param name="session">The edit session owning the live model.</param>
    /// <param name="store">The dedicated conflict validation store.</param>
    /// <param name="editContext">The context the store was created from.</param>
    /// <param name="conflicts">The structured conflicts to surface.</param>
    /// <param name="options">The fallback, clear, and message policy; null keeps defaults.</param>
    /// <typeparam name="TModel">The session model type.</typeparam>
    /// <returns>The number of messages added.</returns>
    public static int AddConflicts<TModel>(
        this ISparseEditSession<TModel> session,
        ValidationMessageStore store,
        EditContext editContext,
        IEnumerable<SparseConflict> conflicts,
        SparseConflictValidationOptions? options = null
    )
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(editContext);
        ArgumentNullException.ThrowIfNull(conflicts);
        options ??= new SparseConflictValidationOptions();

        var model = GetSessionModel(session);
        if (!ReferenceEquals(editContext.Model, model))
        {
            throw new ArgumentException(
                "The EditContext must be bound to the session's model.",
                nameof(editContext)
            );
        }

        var added = 0;
        if (options.ClearStoreFirst)
        {
            store.Clear();
        }

        foreach (var conflict in conflicts)
        {
            ArgumentNullException.ThrowIfNull(conflict);
            var field = ResolveConflictField(model, conflict, options.Fallback);
            store.Add(field, options.GetMessage(conflict));
            added++;
        }

        if (added > 0 || options.ClearStoreFirst)
        {
            editContext.NotifyValidationStateChanged();
        }

        return added;
    }

    /// <summary>Clears a dedicated conflict store and notifies the context.</summary>
    /// <remarks>
    /// Only the supplied store is cleared; DataAnnotations messages in other stores stay intact.
    /// </remarks>
    /// <param name="session">The edit session owning the live model.</param>
    /// <param name="store">The dedicated conflict validation store.</param>
    /// <param name="editContext">The context the store was created from.</param>
    /// <typeparam name="TModel">The session model type.</typeparam>
    public static void ClearConflicts<TModel>(
        this ISparseEditSession<TModel> session,
        ValidationMessageStore store,
        EditContext editContext
    )
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(editContext);
        if (!ReferenceEquals(editContext.Model, GetSessionModel(session)))
        {
            throw new ArgumentException(
                "The EditContext must be bound to the session's model.",
                nameof(editContext)
            );
        }

        store.Clear();
        editContext.NotifyValidationStateChanged();
    }

    /// <summary>Creates a replacement context after adopting an authoritative server model.</summary>
    /// <remarks>
    /// Create a fresh session from the persisted model first, detach validation handlers from
    /// <paramref name="staleContext"/>, then call this on the fresh session. Recreate rendered
    /// inputs (for example by keying the <c>EditForm</c>) because <c>InputBase</c> owns its
    /// context instance and must not be reused across contexts. Transport and retries stay out.
    /// </remarks>
    /// <param name="freshSession">The session created from the authoritative model.</param>
    /// <param name="staleContext">The previous context being replaced.</param>
    /// <typeparam name="TModel">The session model type.</typeparam>
    /// <returns>A new context bound to the fresh session model.</returns>
    public static EditContext ReplaceEditContext<TModel>(
        this ISparseEditSession<TModel> freshSession,
        EditContext staleContext
    )
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(freshSession);
        ArgumentNullException.ThrowIfNull(staleContext);
        if (ReferenceEquals(staleContext.Model, GetSessionModel(freshSession)))
        {
            throw new ArgumentException(
                "The stale context already targets the fresh session model; replacement requires a new model instance.",
                nameof(staleContext)
            );
        }

        return SparseEditSessionExtensions.CreateEditContext(freshSession);
    }

    private static FieldIdentifier ResolveConflictField<TModel>(
        TModel model,
        SparseConflict conflict,
        SparseConflictFallbackMode fallback
    )
        where TModel : class
    {
        // Foreign-root paths never resolve against this graph; surface them at model level.
        if (conflict.Path.RootType != typeof(TModel))
        {
            return new FieldIdentifier(model, string.Empty);
        }

        if (conflict.Path.IsRoot)
        {
            return new FieldIdentifier(model, string.Empty);
        }

        try
        {
            return SparseFieldPathResolver.Resolve(model, conflict.Path);
        }
        catch (ArgumentException) when (fallback is SparseConflictFallbackMode.NearestParent)
        {
            // Walk toward the root so removed keys and missing nested models land on
            // the nearest surviving member instead of being dropped.
            var parent = conflict.Path.Parent;
            while (parent is not null && !parent.Value.IsRoot)
            {
                try
                {
                    return SparseFieldPathResolver.Resolve(model, parent.Value);
                }
                catch (ArgumentException)
                {
                    parent = parent.Value.Parent;
                }
            }

            return new FieldIdentifier(model, string.Empty);
        }
        catch (ArgumentException)
        {
            return new FieldIdentifier(model, string.Empty);
        }
    }

    private static TModel GetSessionModel<TModel>(ISparseEditSession<TModel> session)
        where TModel : class =>
        // Trusted framework access keeps the observable-change cache intact.
        session is ISparseEditSessionModelAccessor<TModel> accessor
            ? accessor.GetModelForFrameworkAccess()
            : session.Model;
}
