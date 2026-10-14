namespace SparseTypes.Blazor;

/// <summary>Exposes child-session actions without the generated path-builder type.</summary>
/// <typeparam name="TModel">The child model type.</typeparam>
/// <typeparam name="TConflict">The generated structured conflict type.</typeparam>
public interface ISparseChildEditContext<TModel, TConflict>
    where TModel : class
    where TConflict : class
{
    /// <summary>Gets the child edit session.</summary>
    ISparseEditSession<TModel> Session { get; }

    /// <summary>Gets whether this child session or its descendants have pending changes.</summary>
    bool HasChanges { get; }

    /// <summary>Gets whether the child history can undo an edit.</summary>
    bool CanUndo { get; }

    /// <summary>Gets whether the child history can redo an edit.</summary>
    bool CanRedo { get; }

    /// <summary>Attempts to merge this child draft into its parent.</summary>
    /// <param name="conflicts">Structured conflicts when the merge fails.</param>
    /// <returns>True when the draft was accepted; otherwise false.</returns>
    bool TryAcceptChanges(out IReadOnlyList<TConflict>? conflicts);

    /// <summary>Merges the child draft into its parent, throwing when the merge conflicts.</summary>
    void AcceptChanges();

    /// <summary>Discards pending child edits while keeping the child session connected.</summary>
    void DiscardChanges();

    /// <summary>Undoes the newest edit in this child session.</summary>
    void Undo();

    /// <summary>Redoes the newest undone edit in this child session.</summary>
    void Redo();
}
