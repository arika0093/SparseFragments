using System.Collections.Generic;
using System.ComponentModel;

namespace SparseTypes;

/// <summary>Coordinates pending nested edit sessions owned by a parent session.</summary>
/// <typeparam name="TModel">The parent model type.</typeparam>
/// <typeparam name="TConflict">The product's structured conflict type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseEditSessionWithChildren<TModel, TConflict>
    where TModel : class
    where TConflict : class
{
    /// <summary>Gets whether any registered child session or descendant has pending changes.</summary>
    bool HasPendingChildSessions { get; }

    /// <summary>Attempts to merge all pending child sessions into this session.</summary>
    /// <param name="conflicts">Structured conflicts when a child merge fails.</param>
    /// <returns>True when all pending child sessions were merged; otherwise false.</returns>
    /// <remarks>Generated sessions that also implement <see cref="ISparseEditSessionWithChildStatus{TModel, TConflict}"/> preflight every child and descendant before committing. Arbitrary setter or callback exceptions during commit are not transactional.</remarks>
    bool TryAcceptChildSessions(out IReadOnlyList<TConflict>? conflicts);

    /// <summary>Discards pending edits in every registered child session.</summary>
    void DiscardChildSessions();

    /// <summary>Opens a child model as an independent typed edit session.</summary>
    /// <typeparam name="TChild">The addressed child model type.</typeparam>
    /// <typeparam name="TChildPaths">The path builder rooted at the child model.</typeparam>
    /// <typeparam name="TChildSession">The generated child-model edit-session type.</typeparam>
    /// <param name="path">The generated path to the child model.</param>
    /// <returns>The child session connected to this session.</returns>
    ISparseEditChildSession<TChild, TChildPaths, TConflict, TChildSession> OpenChildSession<
        TChild,
        TChildPaths,
        TChildSession
    >(ISparseChildEditPath<TModel, TChild, TChildPaths, TChildSession> path)
        where TChild : class
        where TChildPaths : ISparseTypedPath<TChild, TChild>
        where TChildSession : ISparseEditSessionWithPaths<TChild, TChildPaths>;
}
