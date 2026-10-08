using System.ComponentModel;

namespace SparseFragments;

/// <summary>The reason a rebased change could not be reconciled with a concurrent change.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum SparseConflictKind
{
    /// <summary>The whole contribution was changed concurrently.</summary>
    WholeContribution,

    /// <summary>A nested contribution was changed concurrently.</summary>
    Nested,

    /// <summary>A scalar member was changed concurrently.</summary>
    Scalar,

    /// <summary>An append-merged collection was changed concurrently.</summary>
    CollectionAppend,

    /// <summary>A set-union member was changed concurrently.</summary>
    CollectionSetUnion,

    /// <summary>A custom merge strategy reported a conflict.</summary>
    CustomStrategy,

    /// <summary>A write-only member was rebased without its before-state.</summary>
    /// <remarks>
    /// Secret-safe: the attached values carry no secret plaintext, and the
    /// reason never embeds member values.
    /// </remarks>
    RedactedBefore,
}

/// <summary>Structured, domain-neutral information about one rebase conflict.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseConflict
{
    /// <summary>Creates a conflict with the given details.</summary>
    public SparseConflict(
        IEnumerable<string> path,
        SparseConflictKind kind,
        Optional<object?> baseValue,
        Optional<object?> localValue,
        Optional<object?> currentValue,
        string? reason
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        Path = Array.AsReadOnly(path.ToArray());
        Kind = kind;
        BaseValue = baseValue;
        LocalValue = localValue;
        CurrentValue = currentValue;
        Reason = reason;
    }

    /// <summary>The member path from the root contribution.</summary>
    public IReadOnlyList<string> Path { get; }

    /// <summary>The dotted member path, or an empty string for the root contribution.</summary>
    public string PathText => string.Join(".", Path);

    /// <summary>The conflict kind.</summary>
    public SparseConflictKind Kind { get; }

    /// <summary>The baseline presence-aware value.</summary>
    public Optional<object?> BaseValue { get; }

    /// <summary>The desired (local) presence-aware value.</summary>
    public Optional<object?> LocalValue { get; }

    /// <summary>The current presence-aware value.</summary>
    public Optional<object?> CurrentValue { get; }

    /// <summary>An optional human-readable reason.</summary>
    public string? Reason { get; }

    /// <summary>Returns a copy of this conflict with one path segment prepended.</summary>
    public SparseConflict WithPathPrefix(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        var prefix = new string[Path.Count + 1];
        prefix[0] = segment;
        for (var index = 0; index < Path.Count; index++)
        {
            prefix[index + 1] = Path[index];
        }

        return new SparseConflict(prefix, Kind, BaseValue, LocalValue, CurrentValue, Reason);
    }
}

/// <summary>The result of rebasing a change onto a newer sparse state.</summary>
/// <typeparam name="TChange">The generated patch or change-set type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class RebaseResult<TChange>
{
    /// <summary>Creates a rebase result.</summary>
    public RebaseResult(TChange rebased, IEnumerable<SparseConflict> conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        Rebased = rebased;
        Conflicts = Array.AsReadOnly(conflicts.ToArray());
    }

    /// <summary>The rebased change, excluding conflicting members.</summary>
    public TChange Rebased { get; }

    /// <summary>The detected conflicts.</summary>
    public IReadOnlyList<SparseConflict> Conflicts { get; }

    /// <summary>Whether any conflict was detected.</summary>
    public bool HasConflicts => Conflicts.Count > 0;

    /// <summary>Creates a conflict-free rebase result.</summary>
    public static RebaseResult<TChange> Success(TChange rebased) => new(rebased, []);
}
