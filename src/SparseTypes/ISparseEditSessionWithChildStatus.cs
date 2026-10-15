using System.Collections.Generic;
using System.ComponentModel;

namespace SparseTypes;

/// <summary>Extends child-session coordination with parent-owned state and preflight queries.</summary>
/// <typeparam name="TModel">The parent model type.</typeparam>
/// <typeparam name="TConflict">The product's structured conflict type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseEditSessionWithChildStatus<TModel, TConflict>
    : ISparseEditSessionWithChildren<TModel, TConflict>
    where TModel : class
    where TConflict : class
{
    /// <summary>Gets whether this session has changes outside its registered child sessions.</summary>
    bool HasOwnChanges { get; }

    /// <summary>Gets the state of a child session registered for a typed path.</summary>
    /// <typeparam name="TChild">The addressed child model type.</typeparam>
    /// <typeparam name="TChildPaths">The path builder rooted at the child model.</typeparam>
    /// <typeparam name="TChildSession">The generated child-model edit-session type.</typeparam>
    /// <param name="path">The generated path to the child model.</param>
    /// <returns>The status of a registered child session for the path.</returns>
    SparseChildSessionStatus GetChildSessionStatus<TChild, TChildPaths, TChildSession>(
        ISparseChildEditPath<TModel, TChild, TChildPaths, TChildSession> path
    )
        where TChild : class
        where TChildPaths : ISparseTypedPath<TChild, TChild>
        where TChildSession : ISparseEditSessionWithPaths<TChild, TChildPaths>;

    /// <summary>Checks every pending child and descendant merge without changing session state.</summary>
    /// <remarks>
    /// Generated implementations validate a candidate containing all pending descendants before
    /// returning. A conflict leaves parent, child, and descendant values, baselines, and histories
    /// unchanged. This guarantee does not make the later commit transactional when arbitrary
    /// setters, notifications, or callbacks throw.
    /// </remarks>
    /// <param name="conflicts">Structured conflicts when a child merge cannot be prepared.</param>
    /// <returns>True when all pending merges are conflict-free; otherwise false.</returns>
    bool TryPreflightChildSessions(out IReadOnlyList<TConflict>? conflicts);

    /// <summary>Applies this session and its pending child sessions to a separate model candidate.</summary>
    /// <param name="current">The model state to rebase the pending changes onto.</param>
    /// <param name="updated">The candidate model on success; otherwise null.</param>
    /// <param name="conflicts">Structured conflicts when the pending changes cannot be applied.</param>
    /// <returns>True when the candidate includes all pending changes; otherwise false.</returns>
    bool TryApplyPendingState(
        TModel current,
        out TModel? updated,
        out IReadOnlyList<TConflict>? conflicts
    );
}
