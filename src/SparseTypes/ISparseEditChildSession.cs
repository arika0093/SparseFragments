using System.ComponentModel;

namespace SparseTypes;

/// <summary>A child edit session that can merge its accepted state into its parent session.</summary>
/// <typeparam name="TModel">The child model type.</typeparam>
/// <typeparam name="TPaths">The generated path builder rooted at the child model.</typeparam>
/// <typeparam name="TConflict">The product's structured conflict type.</typeparam>
/// <typeparam name="TSession">The generated child-model edit-session type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseEditChildSession<TModel, TPaths, TConflict, TSession> : System.IDisposable
    where TModel : class
    where TPaths : ISparseTypedPath<TModel, TModel>
    where TConflict : class
    where TSession : ISparseEditSessionWithPaths<TModel, TPaths>
{
    /// <summary>Gets whether this child session or its descendants have pending changes.</summary>
    bool HasChanges { get; }

    /// <summary>Gets the child model edit session.</summary>
    TSession Session { get; }

    /// <summary>Attempts to merge the child session into its parent.</summary>
    /// <param name="conflicts">Structured conflicts when the merge fails.</param>
    /// <returns>True when the child state was merged; otherwise false.</returns>
    bool TryAcceptChanges(out System.Collections.Generic.IReadOnlyList<TConflict>? conflicts);

    /// <summary>Merges the child session into its parent, throwing when the merge conflicts.</summary>
    void AcceptChanges();

    /// <summary>Discards pending child and descendant edits while keeping the child session connected.</summary>
    void DiscardChanges();
}
