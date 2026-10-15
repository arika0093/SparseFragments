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
    private readonly Func<(bool Accepted, IReadOnlyList<TConflict>? Conflicts)> _preflight;
    private readonly Func<TModel>? _confirmAccept;
    private readonly Func<bool>? _isTargetMissing;
    private readonly Func<bool>? _isDetached;
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
    /// <param name="preflight">Checks whether the child merge can succeed without changing the parent.</param>
    /// <param name="confirmAccept">Confirms a committed merge without rewriting the parent.</param>
    /// <param name="isTargetMissing">Reports whether the child path no longer resolves.</param>
    /// <param name="isDetached">Reports whether the underlying draft left the open state.</param>
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
        Action detach,
        Func<(bool Accepted, IReadOnlyList<TConflict>? Conflicts)>? preflight = null,
        Func<TModel>? confirmAccept = null,
        Func<bool>? isTargetMissing = null,
        Func<bool>? isDetached = null
    )
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        _rootPath = rootPath;
        _baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        _clone = clone ?? throw new ArgumentNullException(nameof(clone));
        _tryAccept = tryAccept ?? throw new ArgumentNullException(nameof(tryAccept));
        _preflight = preflight ?? (static () => (true, null));
        _confirmAccept = confirmAccept;
        _isTargetMissing = isTargetMissing;
        _isDetached = isDetached;
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

    /// <summary>Gets whether this child session left the open state.</summary>
    /// <remarks>Only an explicit dispose or parent invalidation detaches; a missing target keeps the draft open.</remarks>
    public bool IsDetached => _isDisposed || (_isDetached?.Invoke() == true);

    /// <summary>Gets whether the child path currently resolves to no model.</summary>
    /// <remarks>The draft and its pending edits are retained while the target is missing.</remarks>
    public bool IsTargetMissing
    {
        get
        {
            if (IsDetached || _isTargetMissing is null)
            {
                return false;
            }

            return _isTargetMissing();
        }
    }

    /// <summary>Attempts to merge the child session and its descendants into its parent.</summary>
    /// <param name="conflicts">Structured conflicts when the merge fails.</param>
    /// <returns>True when the child state was merged; otherwise false.</returns>
    /// <remarks>A missing target reports failure with null conflicts and preserves the draft; only an explicit discard or dispose abandons it.</remarks>
    public bool TryAcceptChanges(out IReadOnlyList<TConflict>? conflicts)
    {
        ThrowIfDisposed();
        conflicts = null;
        if (!TryPreflightAcceptChanges(out conflicts))
        {
            return false;
        }

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
            return false;
        }

        _rootPath.WriteModelValueInPlace(Session.Model, result.Current);
        Session.AcceptChanges();
        _baseline = _clone(Session.Model);
        conflicts = null;
        return true;
    }

    /// <summary>Acknowledges a committed merge without rewriting the parent.</summary>
    /// <remarks>Coordinator use only: generated parents call this after the commit write succeeded, parent-first so descendant confirmations observe committed state. Throws when the target cannot be confirmed; confirmed descendants stay confirmed, so callers may retry after restoring the target.</remarks>
    /// <exception cref="InvalidOperationException">The merge cannot be confirmed, or this coordinator was built without confirmation support.</exception>
    public void ConfirmAccept()
    {
        ThrowIfDisposed();
        if (_confirmAccept is null)
        {
            throw new InvalidOperationException(
                "The child edit session does not support staged acceptance."
            );
        }

        var draftModel = _confirmAccept();
        if (Session is ISparseEditSessionWithChildren<TModel, TConflict> children)
        {
            children.ConfirmChildSessions();
        }

        _rootPath.WriteModelValueInPlace(Session.Model, draftModel);
        Session.AcceptChanges();
        _baseline = _clone(Session.Model);
    }

    /// <inheritdoc />
    public bool TryApplyPendingState(
        TModel current,
        out TModel? updated,
        out IReadOnlyList<TConflict>? conflicts
    )
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(current);
        if (Session is not ISparseEditSessionWithChildStatus<TModel, TConflict> childStatus)
        {
            throw new InvalidOperationException(
                "The child edit session does not support pending-state projection."
            );
        }

        return childStatus.TryApplyPendingState(current, out updated, out conflicts);
    }

    /// <summary>Checks whether this child and its descendants can be merged into their parents.</summary>
    /// <param name="conflicts">Structured conflicts when the merge cannot be prepared.</param>
    /// <returns>True when the merge is conflict-free; otherwise false.</returns>
    /// <remarks>Never accepts, discards, or disposes; a missing target reports failure with null conflicts and preserves the draft.</remarks>
    public bool TryPreflightAcceptChanges(out IReadOnlyList<TConflict>? conflicts)
    {
        ThrowIfDisposed();
        if (
            Session is ISparseEditSessionWithChildStatus<TModel, TConflict> children
            && !children.TryPreflightChildSessions(out conflicts)
        )
        {
            return false;
        }

        var result = _preflight();
        conflicts = result.Conflicts;
        return result.Accepted;
    }

    /// <summary>Merges the child session into its parent, throwing when the merge conflicts.</summary>
    /// <remarks>A missing target throws without abandoning the draft; restore the target and retry, or discard or dispose explicitly.</remarks>
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
