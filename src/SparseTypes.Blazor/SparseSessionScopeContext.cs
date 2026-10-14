using Microsoft.AspNetCore.Components.Forms;

namespace SparseTypes.Blazor;

/// <summary>Creates and owns typed child sessions for one <see cref="SparseSessionScope{TModel}"/>.</summary>
/// <typeparam name="TModel">The parent session model type.</typeparam>
public sealed class SparseSessionScopeContext<TModel>
    where TModel : class
{
    private sealed record ChildEntry(object Context, Action Dispose);

    private readonly ISparseEditSession<TModel> _session;
    private readonly Dictionary<SparsePath, ChildEntry> _children = new();
    private bool _disposed;

    internal SparseSessionScopeContext(ISparseEditSession<TModel> session)
    {
        _session = session;
    }

    /// <summary>Opens or reuses a typed child session owned by the scope.</summary>
    /// <remarks>
    /// The child is retained by its typed path until the scope is replaced or disposed.
    /// Its own history is enabled by the scope and released with the child. A successful
    /// accept merges the draft into the parent; discard, undo, and redo stay local to the
    /// child context.
    /// </remarks>
    /// <param name="parentSession">The exact parent session assigned to the owning scope.</param>
    /// <param name="path">A generated typed path to the child model.</param>
    /// <typeparam name="TChild">The child model type.</typeparam>
    /// <typeparam name="TChildPaths">The child model's generated path builder type.</typeparam>
    /// <typeparam name="TConflict">The generated structured conflict type.</typeparam>
    /// <typeparam name="TChildSession">The child model's generated edit-session type.</typeparam>
    /// <returns>The retained child context for this path.</returns>
    /// <exception cref="ArgumentException">The parent session differs from the owning scope session.</exception>
    /// <exception cref="ObjectDisposedException">The scope context has been disposed.</exception>
    public SparseChildEditContext<TChild, TChildPaths, TConflict, TChildSession> OpenChildSession<
        TChild,
        TChildPaths,
        TConflict,
        TChildSession
    >(
        ISparseEditSessionWithChildren<TModel, TConflict> parentSession,
        ISparseChildEditPath<TModel, TChild, TChildPaths, TChildSession> path
    )
        where TChild : class
        where TChildPaths : ISparseTypedPath<TChild, TChild>
        where TConflict : class
        where TChildSession : ISparseEditSessionWithPaths<TChild, TChildPaths>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(parentSession);
        ArgumentNullException.ThrowIfNull(path);
        if (!ReferenceEquals(parentSession, _session))
        {
            throw new ArgumentException(
                "The parent session must be the session assigned to this scope.",
                nameof(parentSession)
            );
        }

        var untypedPath = path.UntypedPath;
        if (_children.TryGetValue(untypedPath, out var entry))
        {
            if (
                entry.Context
                is SparseChildEditContext<TChild, TChildPaths, TConflict, TChildSession> retained
            )
            {
                return retained;
            }

            throw new InvalidOperationException(
                "A child session with this path was opened using incompatible generated types."
            );
        }

        var child = parentSession.OpenChildSession(path);
        try
        {
            SparseSessionInterop.EnableHistory(child.Session);
            var context = new SparseChildEditContext<TChild, TChildPaths, TConflict, TChildSession>(
                child,
                () => RemoveChild(untypedPath)
            );
            _children.Add(untypedPath, new ChildEntry(context, context.DisposeOwned));
            return context;
        }
        catch
        {
            child.Dispose();
            throw;
        }
    }

    internal void DisposeChildren()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        List<Exception>? failures = null;
        foreach (var entry in _children.Values)
        {
            try
            {
                entry.Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        _children.Clear();
        if (failures is not null)
        {
            throw new AggregateException(
                "One or more owned child sessions failed to dispose.",
                failures
            );
        }
    }

    private void RemoveChild(SparsePath path)
    {
        if (_children.Remove(path, out var entry))
        {
            entry.Dispose();
        }
    }
}

/// <summary>A typed child-session context owned by a <see cref="SparseSessionScopeContext{TModel}"/>.</summary>
/// <typeparam name="TModel">The child model type.</typeparam>
/// <typeparam name="TPaths">The generated path builder rooted at the child model.</typeparam>
/// <typeparam name="TConflict">The generated structured conflict type.</typeparam>
/// <typeparam name="TSession">The generated child-model edit-session type.</typeparam>
public sealed class SparseChildEditContext<TModel, TPaths, TConflict, TSession>
    where TModel : class
    where TPaths : ISparseTypedPath<TModel, TModel>
    where TConflict : class
    where TSession : ISparseEditSessionWithPaths<TModel, TPaths>
{
    private readonly ISparseEditChildSession<TModel, TPaths, TConflict, TSession> _child;
    private readonly Action _invalidate;
    private bool _disposed;

    internal SparseChildEditContext(
        ISparseEditChildSession<TModel, TPaths, TConflict, TSession> child,
        Action invalidate
    )
    {
        _child = child;
        _invalidate = invalidate;
        EditContext = child.Session.CreateEditContext();
    }

    /// <summary>Gets the generated edit session for the child model.</summary>
    public TSession Session => _child.Session;

    /// <summary>Gets the generated paths rooted at the child model.</summary>
    public TPaths Path => Session.Paths;

    /// <summary>Gets the Blazor edit context bound to the child model.</summary>
    public EditContext EditContext { get; }

    /// <summary>Gets whether this child session or its descendants have pending changes.</summary>
    public bool HasChanges => _child.HasChanges;

    /// <summary>Gets whether the child history can undo an edit.</summary>
    public bool CanUndo => SparseSessionInterop.TryGetHistory(Session)?.CanUndo == true;

    /// <summary>Gets whether the child history can redo an edit.</summary>
    public bool CanRedo => SparseSessionInterop.TryGetHistory(Session)?.CanRedo == true;

    /// <summary>Attempts to merge this child draft into its parent.</summary>
    /// <param name="conflicts">Structured conflicts when the merge fails.</param>
    /// <returns>True when the draft was accepted; otherwise false.</returns>
    public bool TryAcceptChanges(out IReadOnlyList<TConflict>? conflicts)
    {
        ThrowIfDisposed();
        var accepted = _child.TryAcceptChanges(out conflicts);
        if (!accepted && conflicts is null)
        {
            _invalidate();
        }

        return accepted;
    }

    /// <summary>Merges this child draft into its parent, throwing when the merge conflicts.</summary>
    public void AcceptChanges() => _child.AcceptChanges();

    /// <summary>Discards pending child edits while keeping the child session connected.</summary>
    public void DiscardChanges() => _child.DiscardChanges();

    /// <summary>Undoes the newest edit in this child session.</summary>
    public void Undo() => RequireHistory().Undo();

    /// <summary>Redoes the newest edit in this child session.</summary>
    public void Redo() => RequireHistory().Redo();

    internal void DisposeOwned()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            SparseSessionInterop.DisableHistory(Session);
        }
        finally
        {
            _child.Dispose();
        }
    }

    private ISparseEditHistory RequireHistory() =>
        SparseSessionInterop.TryGetHistory(Session)
        ?? throw new InvalidOperationException("The child session has no enabled edit history.");

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
