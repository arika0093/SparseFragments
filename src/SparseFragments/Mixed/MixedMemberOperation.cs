using System.ComponentModel;

namespace SparseFragments;

/// <summary>One member operation inside a mixed request, without any values.</summary>
/// <remarks>
/// Descriptors identify member paths only. They never carry before, current, or
/// after plaintext, so composition and rollback plans built from them cannot leak
/// secrets into logging or diagnostics (issue #119).
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public readonly record struct MixedMemberOperation
{
    /// <summary>Creates a mixed member operation descriptor.</summary>
    public MixedMemberOperation(string path, MixedHistoryKind history, MixedAfterKind after)
        : this(path, history, after, false) { }

    /// <summary>Creates a mixed member operation descriptor.</summary>
    public MixedMemberOperation(
        string path,
        MixedHistoryKind history,
        MixedAfterKind after,
        bool isWholeRoot
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        Path = path;
        History = history;
        After = after;
        IsWholeRoot = isWholeRoot;
    }

    /// <summary>The member path from the root contribution ("$root" for whole-root operations).</summary>
    public string Path { get; init; }

    /// <summary>Whether the operation carries its before-state.</summary>
    public MixedHistoryKind History { get; init; }

    /// <summary>The requested after-state vocabulary.</summary>
    public MixedAfterKind After { get; init; }

    /// <summary>Whether the operation replaces the whole root contribution.</summary>
    public bool IsWholeRoot { get; init; }
}
