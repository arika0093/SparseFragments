using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SparseTypes;

/// <summary>Coordinates acceptance of a child edit session into its parent session.</summary>
/// <typeparam name="TModel">The child model type.</typeparam>
/// <typeparam name="TPaths">The generated path builder rooted at the child model.</typeparam>
/// <typeparam name="TConflict">The product's structured conflict type.</typeparam>
/// <typeparam name="TSession">The generated child-model edit-session type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseEditChildSession<TModel, TPaths, TConflict, TSession>
    : ISparseEditChildSession<TModel, TPaths, TConflict, TSession>
    where TModel : class
    where TPaths : ISparseTypedPath<TModel, TModel>
    where TConflict : class
    where TSession : ISparseEditSessionWithPaths<TModel, TPaths>
{
    private readonly TPaths _rootPath;
    private readonly Func<TModel, TModel> _clone;
    private TModel _baseline;
    private readonly Func<
        TModel,
        (bool Accepted, TModel? Current, IReadOnlyList<TConflict>? Conflicts)
    > _tryAccept;
    private readonly Action _onDiscarded;
    private readonly Action _detach;
    private bool _isDisposed;

    /// <summary>Creates a child-session coordinator.</summary>
    /// <param name="session">The child model session.</param>
    /// <param name="rootPath">The typed root path used to synchronize accepted state.</param>
    /// <param name="baseline">The child model state to restore when changes are discarded.</param>
    /// <param name="clone">Creates an independent baseline snapshot.</param>
    /// <param name="tryAccept">Callback that merges the child model into its parent.</param>
    /// <param name="onDiscarded">Callback raised after restoring the child model.</param>
    /// <param name="detach">Callback that disconnects this child session from its parent.</param>
    public SparseEditChildSession(
        TSession session,
        TPaths rootPath,
        TModel baseline,
        Func<TModel, TModel> clone,
        Func<
            TModel,
            (bool Accepted, TModel? Current, IReadOnlyList<TConflict>? Conflicts)
        > tryAccept,
        Action onDiscarded,
        Action detach
    )
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        _rootPath = rootPath;
        _baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        _clone = clone ?? throw new ArgumentNullException(nameof(clone));
        _tryAccept = tryAccept ?? throw new ArgumentNullException(nameof(tryAccept));
        _onDiscarded = onDiscarded ?? throw new ArgumentNullException(nameof(onDiscarded));
        _detach = detach ?? throw new ArgumentNullException(nameof(detach));
    }

    /// <summary>Gets the child model edit session.</summary>
    public TSession Session { get; }

    /// <summary>Gets whether this child session or its descendants have pending changes.</summary>
    public bool HasChanges =>
        !_isDisposed
        && (
            Session.HasChanges
            || (
                Session is ISparseEditSessionWithChildren<TModel, TConflict> children
                && children.HasPendingChildSessions
            )
        );

    /// <summary>Attempts to merge the child session and its descendants into its parent.</summary>
    /// <param name="conflicts">Structured conflicts when the merge fails.</param>
    /// <returns>True when the child state was merged; otherwise false.</returns>
    public bool TryAcceptChanges(out IReadOnlyList<TConflict>? conflicts)
    {
        ThrowIfDisposed();
        conflicts = null;
        if (
            Session is ISparseEditSessionWithChildren<TModel, TConflict> children
            && !children.TryAcceptChildSessions(out conflicts)
        )
        {
            return false;
        }

        var result = _tryAccept(Session.Model);
        conflicts = result.Conflicts;
        if (!result.Accepted || result.Current is null)
        {
            if (conflicts is null)
            {
                Dispose();
            }

            return false;
        }

        _rootPath.WriteModelValueInPlace(Session.Model, result.Current);
        Session.AcceptChanges();
        _baseline = _clone(Session.Model);
        conflicts = null;
        return true;
    }

    /// <summary>Merges the child session into its parent, throwing when the merge conflicts.</summary>
    public void AcceptChanges()
    {
        if (!TryAcceptChanges(out var conflicts))
        {
            throw new InvalidOperationException(
                conflicts is null
                    ? "The child edit session target is no longer available."
                    : "The child edit session conflicts with changes in its parent session."
            );
        }
    }

    /// <summary>Discards pending child and descendant edits without disconnecting the child session.</summary>
    public void DiscardChanges()
    {
        ThrowIfDisposed();
        if (Session is ISparseEditSessionWithChildren<TModel, TConflict> children)
        {
            children.DiscardChildSessions();
        }

        _rootPath.WriteModelValueInPlace(Session.Model, _baseline);
        _onDiscarded();
        Session.AcceptChanges();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        if (Session is ISparseEditSessionWithChildren<TModel, TConflict> children)
        {
            children.DiscardChildSessions();
        }

        _isDisposed = true;
        _detach();
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(
                nameof(SparseEditChildSession<TModel, TPaths, TConflict, TSession>)
            );
        }
    }
}
