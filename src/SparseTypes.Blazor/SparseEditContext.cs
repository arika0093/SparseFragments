using Microsoft.AspNetCore.Components.Forms;

namespace SparseTypes.Blazor;

/// <summary>
/// The render-scoped template context provided by <see cref="SparseEdit{TModel, TPaths}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Exposes the concrete generated path builder (<see cref="Path"/>), the original-model
/// <see cref="EditContext"/>, live <see cref="HasChanges"/>, and the session-owned history
/// surface (<see cref="History"/>, <see cref="CanUndo"/>, <see cref="CanRedo"/>,
/// <see cref="Undo"/>, <see cref="Redo"/>). History is owned and configured by the session
/// (opt-in <c>EnableHistory</c>); this context never enables, reconfigures, or disposes it.
/// When the session has no enabled history, <see cref="History"/> is null,
/// <see cref="CanUndo"/> and <see cref="CanRedo"/> are false, <see cref="Undo"/> and
/// <see cref="Redo"/> throw, and the <c>Try</c> variants report failure.
/// </para>
/// <para>
/// The instance is recreated when the <c>Session</c> parameter is replaced, so a stale
/// context never observes the replacement session. Reads (<see cref="HasChanges"/>,
/// <see cref="CanUndo"/>, <see cref="CanRedo"/>) resolve live on every access and never
/// read the mutable model through paths that would disturb the session change cache.
/// </para>
/// </remarks>
/// <typeparam name="TModel">The live editable model type.</typeparam>
/// <typeparam name="TPaths">The concrete generated root path builder type.</typeparam>
public sealed class SparseEditContext<TModel, TPaths> : ISparseEditScope<TModel>
    where TModel : class
    where TPaths : ISparseTypedPath<TModel, TModel>
{
    internal SparseEditContext(
        ISparseEditSessionWithPaths<TModel, TPaths> session,
        TPaths path,
        EditContext editContext
    )
    {
        Session = session;
        Path = path;
        EditContext = editContext;
    }

    /// <summary>Gets the active edit session.</summary>
    public ISparseEditSessionWithPaths<TModel, TPaths> Session { get; }

    /// <inheritdoc />
    ISparseEditSession<TModel> ISparseEditScope<TModel>.Session => Session;

    /// <summary>Gets the concrete generated path navigation rooted at the session model.</summary>
    /// <remarks>
    /// Path-only access: reading this property never reads the mutable model or invalidates
    /// the session change cache. Navigate such as <c>Path.Customer.Name</c> or
    /// <c>Path.Lines.Key(id).Price</c>; every navigation keeps the root model type.
    /// </remarks>
    public TPaths Path { get; }

    /// <summary>Gets the original-model edit context bound to the session model.</summary>
    public EditContext EditContext { get; }

    /// <summary>Gets whether the session model differs from the retained baseline.</summary>
    public bool HasChanges => Session.HasChanges;

    /// <summary>Gets the session-owned history, or null when the session has none enabled.</summary>
    public ISparseEditHistory? History => SparseSessionInterop.TryGetHistory(Session);

    /// <summary>Gets whether an undo entry is available.</summary>
    public bool CanUndo => History?.CanUndo == true;

    /// <summary>Gets whether a redo entry is available.</summary>
    public bool CanRedo => History?.CanRedo == true;

    /// <summary>Undoes the newest history entry, throwing on conflict.</summary>
    /// <exception cref="InvalidOperationException">
    /// The session has no enabled history, or there is no entry, or the before-state conflicts.
    /// </exception>
    public void Undo() => RequireHistory().Undo();

    /// <summary>Attempts to undo the newest history entry.</summary>
    /// <param name="conflicts">Conflicts when the result is false.</param>
    /// <returns>True on success; otherwise false.</returns>
    public bool TryUndo(out IReadOnlyList<SparseConflict>? conflicts)
    {
        var history = History;
        if (history is null)
        {
            conflicts = null;
            return false;
        }

        return history.TryUndo(out conflicts);
    }

    /// <summary>Redoes the newest undone history entry, throwing on conflict.</summary>
    /// <exception cref="InvalidOperationException">
    /// The session has no enabled history, or there is no entry, or the before-state conflicts.
    /// </exception>
    public void Redo() => RequireHistory().Redo();

    /// <summary>Attempts to redo the newest undone history entry.</summary>
    /// <param name="conflicts">Conflicts when the result is false.</param>
    /// <returns>True on success; otherwise false.</returns>
    public bool TryRedo(out IReadOnlyList<SparseConflict>? conflicts)
    {
        var history = History;
        if (history is null)
        {
            conflicts = null;
            return false;
        }

        return history.TryRedo(out conflicts);
    }

    private ISparseEditHistory RequireHistory() =>
        History
        ?? throw new InvalidOperationException(
            "The session has no enabled history. Call EnableHistory on the session; "
                + "this component never enables history itself."
        );
}
