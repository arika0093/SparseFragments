using System;
using System.Collections.Generic;

namespace SparseTypes;

/// <summary>Framework-neutral undo/redo control over a session-owned edit history.</summary>
/// <remarks>It exposes availability, counts, undo/redo, and grouping without the typed change-set algebra, which stays inside the session.</remarks>
public interface ISparseEditHistory
{
    /// <summary>Gets whether an undo entry is available.</summary>
    bool CanUndo { get; }

    /// <summary>Gets whether a redo entry is available.</summary>
    bool CanRedo { get; }

    /// <summary>Gets the retained undo entry count.</summary>
    int UndoCount { get; }

    /// <summary>Gets the retained redo entry count.</summary>
    int RedoCount { get; }

    /// <summary>Gets the maximum retained undo entries.</summary>
    int MaxEntries { get; }

    /// <summary>Raised after undo/redo stack state changes.</summary>
    /// <remarks>Covers observed edits, grouped commits.</remarks>
    event Action? StateChanged;

    /// <summary>Undoes the newest entry, throwing on conflict.</summary>
    /// <exception cref="InvalidOperationException">No entry exists or the before-state conflicts.</exception>
    void Undo();

    /// <summary>Attempts to undo the newest entry.</summary>
    /// <param name="conflicts">Conflicts when the result is false.</param>
    /// <returns>True on success; otherwise false.</returns>
    bool TryUndo(out IReadOnlyList<SparseConflict>? conflicts);

    /// <summary>Redoes the newest undone entry, throwing on conflict.</summary>
    /// <exception cref="InvalidOperationException">No entry exists or the before-state conflicts.</exception>
    void Redo();

    /// <summary>Attempts to redo the newest undone entry.</summary>
    /// <param name="conflicts">Conflicts when the result is false.</param>
    /// <returns>True on success; otherwise false.</returns>
    bool TryRedo(out IReadOnlyList<SparseConflict>? conflicts);

    /// <summary>Begins an explicit group across multiple events.</summary>
    /// <returns>A scope that commits the group on dispose.</returns>
    IDisposable BeginGroup();

    /// <summary>Runs an action inside one explicit history group.</summary>
    /// <param name="action">The edits to group.</param>
    void Group(Action action);

    /// <summary>Clears undo entries, redo entries, and any buffered group.</summary>
    void Clear();
}
