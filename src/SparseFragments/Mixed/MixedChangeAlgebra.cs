using System.ComponentModel;

namespace SparseFragments;

/// <summary>Deterministic mixed-operation algebra over value-free descriptors (issue #119).</summary>
/// <remarks>
/// The rules decide composition shapes, rollback plans, and projection gates from
/// member paths and history kinds only. Value-level continuity checks stay with
/// the typed ChangeSet and Patch APIs, which hold the actual before and after
/// states. A merged operation regains complete history only when one side
/// supplied a real baseline; history is never inferred from redacted endpoints.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public static class MixedChangeAlgebra
{
    /// <summary>The path used for whole-root operations.</summary>
    public const string WholeRootPath = "$root";

    /// <summary>Composes two operations on the same path applied in order.</summary>
    /// <remarks>
    /// Transition then transition keeps continuity checks. Transition then blind
    /// set keeps the real baseline without further checks. Blind set then
    /// transition stays blind and still needs a value-level continuity check.
    /// Blind set then blind set stays blind. Whole-root operations absorb
    /// memberwise blind sets and overwrite through blind sets; anything needing
    /// value-level whole-state continuity fails with a typed reason.
    /// </remarks>
    public static MixedComposeResult Compose(
        MixedMemberOperation first,
        MixedMemberOperation second
    )
    {
        if (
            !first.IsWholeRoot
            && !second.IsWholeRoot
            && !string.Equals(first.Path, second.Path, StringComparison.Ordinal)
        )
        {
            return new MixedComposeResult(
                false,
                first.Path,
                MixedHistoryKind.Transition,
                first.After,
                false,
                false,
                "Mixed composition of '"
                    + first.Path
                    + "' and '"
                    + second.Path
                    + "' requires both operations to target the same path."
            );
        }

        var path = first.IsWholeRoot || second.IsWholeRoot ? WholeRootPath : first.Path;

        // A trailing blind whole-root set overwrites everything before it.
        if (second.IsWholeRoot && second.History == MixedHistoryKind.BlindSet)
        {
            return new MixedComposeResult(
                true,
                path,
                MixedHistoryKind.BlindSet,
                second.After,
                false,
                true,
                null
            );
        }

        // A leading blind whole-root set absorbs memberwise operations that follow.
        if (first.IsWholeRoot && first.History == MixedHistoryKind.BlindSet && !second.IsWholeRoot)
        {
            return new MixedComposeResult(
                true,
                path,
                MixedHistoryKind.BlindSet,
                first.After,
                false,
                true,
                null
            );
        }

        if (first.IsWholeRoot || second.IsWholeRoot)
        {
            return WholeNeedsValues(first, second, path);
        }

        if (
            first.History == MixedHistoryKind.Transition
            && second.History == MixedHistoryKind.Transition
        )
        {
            return new MixedComposeResult(
                true,
                path,
                MixedHistoryKind.Transition,
                second.After,
                true,
                false,
                null
            );
        }

        if (first.History == MixedHistoryKind.Transition)
        {
            return new MixedComposeResult(
                true,
                path,
                MixedHistoryKind.Transition,
                second.After,
                false,
                false,
                null
            );
        }

        if (second.History == MixedHistoryKind.Transition)
        {
            return new MixedComposeResult(
                true,
                path,
                MixedHistoryKind.BlindSet,
                second.After,
                true,
                false,
                null
            );
        }

        return new MixedComposeResult(
            true,
            path,
            MixedHistoryKind.BlindSet,
            second.After,
            false,
            false,
            null
        );
    }

    /// <summary>Composes two operation sequences applied in order.</summary>
    /// <remarks>
    /// Disjoint paths merge without checks. Overlapping paths follow
    /// <see cref="Compose(MixedMemberOperation, MixedMemberOperation)"/>.
    /// Output order is deterministic: first-sequence order, then paths seen only
    /// in the second sequence.
    /// </remarks>
    public static MixedSequenceComposition ComposeSequences(
        IEnumerable<MixedMemberOperation> first,
        IEnumerable<MixedMemberOperation> second
    )
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var firstByPath = IndexByPath(first);
        var secondByPath = IndexByPath(second);
        var composed = new List<MixedMemberOperation>(firstByPath.Count + secondByPath.Count);
        var failures = new List<MixedComposeResult>();

        foreach (var entry in firstByPath)
        {
            if (secondByPath.TryGetValue(entry.Key, out var following))
            {
                var outcome = Compose(entry.Value, following);
                if (outcome.Succeeded)
                {
                    composed.Add(
                        new MixedMemberOperation(
                            outcome.Path,
                            outcome.ResultHistory,
                            outcome.ResultAfter,
                            outcome.ResultIsWholeRoot
                        )
                    );
                }
                else
                {
                    failures.Add(outcome);
                }
            }
            else
            {
                composed.Add(entry.Value);
            }
        }

        foreach (var entry in secondByPath.Where(entry => !firstByPath.ContainsKey(entry.Key)))
        {
            composed.Add(entry.Value);
        }

        return new MixedSequenceComposition(failures.Count == 0, composed, failures);
    }

    /// <summary>Plans a rollback: transitions invert, blind sets are skipped and reported.</summary>
    public static MixedRollbackPlan CreateRollbackPlan(IEnumerable<MixedMemberOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var reversible = new List<string>();
        var skipped = new List<string>();
        foreach (var operation in IndexByPath(operations).Values)
        {
            var path = operation.IsWholeRoot ? WholeRootPath : operation.Path;
            if (operation.History == MixedHistoryKind.Transition)
            {
                reversible.Add(path);
            }
            else
            {
                skipped.Add(path);
            }
        }

        return new MixedRollbackPlan(reversible, skipped);
    }

    /// <summary>Whether the operations can form a baseline-aware change set.</summary>
    /// <remarks>
    /// Returns false and reports the blind paths when any operation lacks a
    /// before-state. Blind operations project through a baseline-free patch
    /// instead; they never regain history here.
    /// </remarks>
    public static bool CanFormChangeSet(
        IEnumerable<MixedMemberOperation> operations,
        out IReadOnlyList<string> blindPaths
    )
    {
        ArgumentNullException.ThrowIfNull(operations);

        var blind = IndexByPath(operations)
            .Values.Where(static operation => operation.History == MixedHistoryKind.BlindSet)
            .Select(static operation => operation.IsWholeRoot ? WholeRootPath : operation.Path)
            .ToList();

        blindPaths = Array.AsReadOnly(blind.ToArray());
        return blind.Count == 0;
    }

    /// <summary>Guards the rebase-policy seam for future strict handling (issue #120).</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for any policy besides pass-through.</exception>
    public static void EnsurePassthrough(RedactedBeforePolicy policy, string? parameterName = null)
    {
        if (policy != RedactedBeforePolicy.Passthrough)
        {
            throw new ArgumentOutOfRangeException(
                parameterName ?? nameof(policy),
                "Only redacted-before pass-through is supported. Strict rejection belongs to the rebase-policy follow-up."
            );
        }
    }

    private static MixedComposeResult WholeNeedsValues(
        MixedMemberOperation first,
        MixedMemberOperation second,
        string path
    )
    {
        // Whole-root transition then whole-root transition: history survives
        // once the value-level endpoints line up.
        if (
            first.IsWholeRoot
            && second.IsWholeRoot
            && first.History == MixedHistoryKind.Transition
            && second.History == MixedHistoryKind.Transition
        )
        {
            return new MixedComposeResult(
                true,
                path,
                MixedHistoryKind.Transition,
                second.After,
                true,
                true,
                null
            );
        }

        // Memberwise then whole-root transition: the whole before-state is real,
        // so the merged whole operation keeps history once values line up.
        if (
            !first.IsWholeRoot
            && second.IsWholeRoot
            && second.History == MixedHistoryKind.Transition
        )
        {
            return new MixedComposeResult(
                true,
                path,
                MixedHistoryKind.Transition,
                second.After,
                true,
                true,
                null
            );
        }

        // Whole-root transition then memberwise: the whole after-state kind survives.
        if (
            first.IsWholeRoot
            && first.History == MixedHistoryKind.Transition
            && !second.IsWholeRoot
        )
        {
            return new MixedComposeResult(
                true,
                path,
                MixedHistoryKind.Transition,
                first.After,
                true,
                true,
                null
            );
        }

        return new MixedComposeResult(
            false,
            path,
            MixedHistoryKind.Transition,
            second.After,
            false,
            true,
            "Whole-root transitions need value-level continuity. Compose them through the typed ChangeSet API."
        );
    }

    private static Dictionary<string, MixedMemberOperation> IndexByPath(
        IEnumerable<MixedMemberOperation> operations
    )
    {
        // Fold repeat paths in order through Compose so earlier history is not
        // silently discarded (issue #141). Uncomposable repeats are rejected
        // with an explicit reason instead of keeping only the last entry.
        var indexed = new Dictionary<string, MixedMemberOperation>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            var key = KeyOf(operation);
            if (!indexed.TryGetValue(key, out var existing))
            {
                indexed[key] = operation;
                continue;
            }

            var outcome = Compose(existing, operation);
            if (!outcome.Succeeded)
            {
                throw new ArgumentException(
                    "Duplicate operations on path '"
                        + key
                        + "' cannot be composed: "
                        + outcome.FailureReason,
                    nameof(operations)
                );
            }

            indexed[key] = new MixedMemberOperation(
                outcome.Path,
                outcome.ResultHistory,
                outcome.ResultAfter,
                outcome.ResultIsWholeRoot
            );
        }

        return indexed;
    }

    private static string KeyOf(MixedMemberOperation operation) =>
        operation.IsWholeRoot ? WholeRootPath : operation.Path;
}
