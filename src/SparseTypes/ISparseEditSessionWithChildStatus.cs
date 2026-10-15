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
    /// <param name="conflicts">Structured conflicts when a child merge cannot be prepared.</param>
    /// <returns>True when all pending merges are conflict-free; otherwise false.</returns>
    bool TryPreflightChildSessions(out IReadOnlyList<TConflict>? conflicts);
}
