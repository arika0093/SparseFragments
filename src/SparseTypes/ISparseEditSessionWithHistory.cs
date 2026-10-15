using System.ComponentModel;

namespace SparseTypes;

/// <summary>Framework-neutral contract for a session-owned edit history.</summary>
/// <remarks>Generated sessions implement this interface only when the owning generator binds a session-capability contract and emits owned-history members; Blazor and other hosts probe for it instead of reflecting over session members.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseEditSessionWithHistory
{
    /// <summary>Gets the session-owned history, or null when history is not enabled.</summary>
    /// <remarks>No history subscription or retained entries exist until <c>EnableHistory</c> runs.</remarks>
    ISparseEditHistory? History { get; }

    /// <summary>Raised after <c>EnableHistory</c> creates the history and after <c>DisableHistory</c> releases it.</summary>
    event System.Action? HistoryChanged;

    /// <summary>Enables the session-owned history used by hosts such as Blazor scopes.</summary>
    /// <param name="options">History retention options, or null for the defaults.</param>
    /// <returns>The session-owned history control.</returns>
    ISparseEditHistory EnableHistory(EditHistoryOptions? options = null);

    /// <summary>Releases session-owned history enabled for a host-owned session.</summary>
    void DisableHistory();
}
