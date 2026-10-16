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

    /// <summary>Gets the ownership mode used by this child context.</summary>
    SparseChildEditMode Mode { get; }

    /// <summary>Gets whether this child has changes under the active mode.</summary>
    bool HasChanges { get; }

    /// <summary>Gets structured conflicts from the most recent merge attempt.</summary>
    IReadOnlyList<TConflict>? Conflicts { get; }

    /// <summary>Occurs after this child draft is accepted or passed through to its parent.</summary>
    event Action? Accepted;

    /// <summary>Gets whether the active edit history can undo an edit.</summary>
    bool CanUndo { get; }

    /// <summary>Gets whether the active edit history can redo an edit.</summary>
    bool CanRedo { get; }

    /// <summary>Attempts to merge this child draft into its parent.</summary>
    /// <param name="conflicts">Structured conflicts when the merge fails.</param>
    /// <returns>True when the draft was accepted or no draft remains; otherwise false.</returns>
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
