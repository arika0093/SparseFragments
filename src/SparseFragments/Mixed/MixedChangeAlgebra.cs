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
    /// A trailing blind whole-root operation supersedes prior memberwise
    /// operations, and a leading blind whole-root absorbs following memberwise
    /// operations (issue #129). Entries within one input sequence coexist;
    /// only cross-sequence overlap composes, so a root and its same-sequence
    /// members are retained together until the other sequence arrives.
    /// Whole-root transitions still require value-level continuity and fail
    /// with a typed reason when unjustified. Output order is deterministic:
    /// first-sequence order, then paths seen only in the second sequence,
    /// minus entries superseded by a whole root.
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
        var firstRoot = TakeRoot(firstByPath, out var firstMembers);
        var secondRoot = TakeRoot(secondByPath, out var secondMembers);

        // Trailing blind whole-root overwrites everything before it, including
        // same-sequence members that arrived alongside it.
        if (secondRoot.HasValue && secondRoot.Value.History == MixedHistoryKind.BlindSet)
        {
            MixedMemberOperation root = secondRoot.Value;
            var failures = new List<MixedComposeResult>();
            if (firstRoot.HasValue)
            {
                var outcome = Compose(firstRoot.Value, secondRoot.Value);
                if (!outcome.Succeeded)
                {
                    failures.Add(outcome);
                    return new MixedSequenceComposition(
                        false,
                        [.. firstMembers.Values, .. secondMembers.Values],
                        failures
                    );
                }

                root = new MixedMemberOperation(
                    outcome.Path,
                    outcome.ResultHistory,
                    outcome.ResultAfter,
                    outcome.ResultIsWholeRoot
                );
            }

            return new MixedSequenceComposition(true, [root], failures);
        }

        // Leading blind whole-root absorbs following memberwise operations.
        if (firstRoot.HasValue && firstRoot.Value.History == MixedHistoryKind.BlindSet)
        {
            if (secondRoot.HasValue)
            {
                var outcome = Compose(firstRoot.Value, secondRoot.Value);
                if (!outcome.Succeeded)
                {
                    return new MixedSequenceComposition(
                        false,
                        [.. firstMembers.Values, .. secondMembers.Values],
                        [outcome]
                    );
                }

                // Whole-whole blind composition stays a single blind root;
                // members on both sides are absorbed.
                var root = new MixedMemberOperation(
                    outcome.Path,
                    outcome.ResultHistory,
                    outcome.ResultAfter,
                    outcome.ResultIsWholeRoot
                );
                return new MixedSequenceComposition(true, [root, .. firstMembers.Values], []);
            }

            // Second-sequence members are absorbed; first-sequence entries stay.
            var retained = new List<MixedMemberOperation>(firstByPath.Count + secondByPath.Count);
            foreach (var entry in firstByPath)
            {
                retained.Add(entry.Value);
            }

            return new MixedSequenceComposition(true, retained, []);
        }

        // Any remaining whole-root transition overlaps every member path.
        if (firstRoot.HasValue || secondRoot.HasValue)
        {
            return ComposeWithWholeTransition(firstRoot, firstMembers, secondRoot, secondMembers);
        }

        return ComposeMemberSequences(firstMembers, secondMembers);
    }

    private static MixedMemberOperation? TakeRoot(
        Dictionary<string, MixedMemberOperation> indexed,
        out Dictionary<string, MixedMemberOperation> members
    )
    {
        members = new Dictionary<string, MixedMemberOperation>(StringComparer.Ordinal);
        MixedMemberOperation? root = null;
        foreach (var entry in indexed)
        {
            if (string.Equals(entry.Key, WholeRootPath, StringComparison.Ordinal))
            {
                root = entry.Value;
            }
            else
            {
                members[entry.Key] = entry.Value;
            }
        }

        return root;
    }

    private static MixedSequenceComposition ComposeWithWholeTransition(
        MixedMemberOperation? firstRoot,
        Dictionary<string, MixedMemberOperation> firstMembers,
        MixedMemberOperation? secondRoot,
        Dictionary<string, MixedMemberOperation> secondMembers
    )
    {
        var failures = new List<MixedComposeResult>();
        if (firstRoot.HasValue && secondRoot.HasValue)
        {
            var outcome = Compose(firstRoot.Value, secondRoot.Value);
            if (!outcome.Succeeded)
            {
                failures.Add(outcome);
                return new MixedSequenceComposition(
                    false,
                    [.. firstMembers.Values, .. secondMembers.Values],
                    failures
                );
            }

            // Members collapse into the whole-root transition; value continuity
            // is verified by the typed ChangeSet API.
            var root = new MixedMemberOperation(
                outcome.Path,
                outcome.ResultHistory,
                outcome.ResultAfter,
                outcome.ResultIsWholeRoot
            );
            return new MixedSequenceComposition(true, [root], failures);
        }

        // Exactly one whole-root transition: it consumes the other side's
        // members, while same-sequence members accompany their own root.
        if (secondRoot.HasValue)
        {
            if (firstMembers.Count == 0)
            {
                return new MixedSequenceComposition(
                    true,
                    [secondRoot.Value, .. secondMembers.Values],
                    failures
                );
            }

            var probe = Compose(firstMembers.Values.First(), secondRoot.Value);
            if (!probe.Succeeded)
            {
                failures.Add(probe);
                return new MixedSequenceComposition(
                    false,
                    [.. firstMembers.Values, .. secondMembers.Values],
                    failures
                );
            }

            var root = new MixedMemberOperation(
                probe.Path,
                probe.ResultHistory,
                probe.ResultAfter,
                probe.ResultIsWholeRoot
            );
            return new MixedSequenceComposition(true, [root], failures);
        }

        if (firstMembers.Count == 0)
        {
            return new MixedSequenceComposition(
                true,
                [firstRoot!.Value, .. secondMembers.Values],
                failures
            );
        }

        var firstProbe = Compose(firstRoot!.Value, secondMembers.Values.First());
        if (!firstProbe.Succeeded)
        {
            failures.Add(firstProbe);
            return new MixedSequenceComposition(
                false,
                [.. firstMembers.Values, .. secondMembers.Values],
                failures
            );
        }

        return new MixedSequenceComposition(
            true,
            [firstRoot.Value, .. firstMembers.Values],
            failures
        );
    }

    private static MixedSequenceComposition ComposeMemberSequences(
        Dictionary<string, MixedMemberOperation> firstMembers,
        Dictionary<string, MixedMemberOperation> secondMembers
    )
    {
        var composed = new List<MixedMemberOperation>(firstMembers.Count + secondMembers.Count);
        var failures = new List<MixedComposeResult>();
        var consumedSecond = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in firstMembers)
        {
            if (secondMembers.TryGetValue(entry.Key, out var following))
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

                consumedSecond.Add(entry.Key);
                continue;
            }

            // Segment-aware ancestor/descendant overlap (issue #130): a whole
            // member operation overlaps its nested children and keyed entries.
            // String-prefix matching is wrong ("A" does not overlap "AB").
            var overlap = FindOverlap(entry.Value, secondMembers, consumedSecond);
            if (overlap.HasValue)
            {
                consumedSecond.Add(overlap.Value.SecondKey);
                var resolution = ResolveAncestorOverlap(entry.Value, overlap.Value.SecondOp);
                if (resolution.Failure.HasValue)
                {
                    failures.Add(resolution.Failure.Value);
                }

                if (resolution.KeepFirst)
                {
                    composed.Add(entry.Value);
                }

                if (resolution.KeepSecond)
                {
                    composed.Add(overlap.Value.SecondOp);
                }

                continue;
            }

            composed.Add(entry.Value);
        }

        foreach (var entry in secondMembers.Where(entry => !firstMembers.ContainsKey(entry.Key)))
        {
            if (consumedSecond.Contains(entry.Key))
            {
                continue;
            }

            // Second-side entries overlapping an already-retained first entry
            // were resolved above; check against retained first-side ancestors
            // that were emitted without a same-key match.
            var overlap = FindOverlapInComposed(entry.Value, composed);
            if (overlap.HasValue)
            {
                var resolution = ResolveAncestorOverlap(overlap.Value, entry.Value);
                if (resolution.Failure.HasValue)
                {
                    failures.Add(resolution.Failure.Value);
                }

                if (resolution.KeepSecond)
                {
                    // Replace the retained ancestor when the trailing side wins.
                    if (!resolution.KeepFirst)
                    {
                        composed.Remove(overlap.Value);
                    }

                    composed.Add(entry.Value);
                }

                continue;
            }

            composed.Add(entry.Value);
        }

        return new MixedSequenceComposition(failures.Count == 0, composed, failures);
    }

    private static (string SecondKey, MixedMemberOperation SecondOp)? FindOverlap(
        MixedMemberOperation first,
        Dictionary<string, MixedMemberOperation> secondMembers,
        HashSet<string> consumedSecond
    )
    {
        foreach (var entry in secondMembers)
        {
            if (
                consumedSecond.Contains(entry.Key)
                || string.Equals(first.Path, entry.Key, StringComparison.Ordinal)
            )
            {
                continue;
            }

            if (IsAncestorOrDescendant(first.Path, entry.Key))
            {
                return (entry.Key, entry.Value);
            }
        }

        return null;
    }

    private static MixedMemberOperation? FindOverlapInComposed(
        MixedMemberOperation second,
        List<MixedMemberOperation> composed
    )
    {
        foreach (var existing in composed)
        {
            if (
                !existing.IsWholeRoot
                && !second.IsWholeRoot
                && !string.Equals(existing.Path, second.Path, StringComparison.Ordinal)
                && IsAncestorOrDescendant(existing.Path, second.Path)
            )
            {
                return existing;
            }
        }

        return null;
    }

    private sealed record OverlapResolution(
        bool KeepFirst,
        bool KeepSecond,
        MixedComposeResult? Failure
    );

    private static OverlapResolution ResolveAncestorOverlap(
        MixedMemberOperation first,
        MixedMemberOperation second
    )
    {
        var firstAncestor = IsStrictAncestor(first.Path, second.Path);
        // Any transition side needs value-level continuity that descriptors
        // cannot verify; never invent before values.
        if (
            first.History == MixedHistoryKind.Transition
            || second.History == MixedHistoryKind.Transition
        )
        {
            return new OverlapResolution(
                false,
                false,
                new MixedComposeResult(
                    false,
                    firstAncestor ? first.Path : second.Path,
                    MixedHistoryKind.Transition,
                    second.After,
                    false,
                    false,
                    "Overlapping operations on '"
                        + first.Path
                        + "' and '"
                        + second.Path
                        + "' need value-level continuity. Compose them through the typed ChangeSet API."
                )
            );
        }

        // Both blind: whole-member granularity wins. A trailing ancestor
        // overwrites the prior child; a leading ancestor absorbs the later
        // child edit, so no overwritten child or orphaned edit survives.
        if (firstAncestor)
        {
            return new OverlapResolution(true, false, null);
        }

        return new OverlapResolution(false, true, null);
    }

    /// <summary>Whether either path is a strict segment ancestor of the other.</summary>
    /// <remarks>
    /// Segments split on '.' with bracket suffixes kept on their element
    /// (so <c>Items["k"]</c> is one segment). <c>A</c> is not an ancestor of
    /// <c>AB</c>; <c>Nested</c> is an ancestor of <c>Nested.Host</c>.
    /// </remarks>
    public static bool IsAncestorOrDescendant(string first, string second) =>
        IsStrictAncestor(first, second) || IsStrictAncestor(second, first);

    private static bool IsStrictAncestor(string ancestor, string descendant)
    {
        var a = SplitPathSegments(ancestor);
        var d = SplitPathSegments(descendant);
        if (a.Length >= d.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (!string.Equals(a[i], d[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] SplitPathSegments(string path)
    {
        // Bracket keys never contain an unescaped '.' outside quotes in the
        // canonical form, so a '.' split with bracket-awareness suffices.
        var segments = new List<string>();
        var current = new System.Text.StringBuilder();
        var depth = 0;
        var inQuotes = false;
        for (var i = 0; i < path.Length; i++)
        {
            var c = path[i];
            if (c == '"' && (i == 0 || path[i - 1] != '\\'))
            {
                inQuotes = !inQuotes;
                current.Append(c);
                continue;
            }

            if (!inQuotes)
            {
                if (c == '[')
                {
                    depth++;
                }
                else if (c == ']')
                {
                    depth--;
                }
                else if (c == '.' && depth == 0)
                {
                    segments.Add(current.ToString());
                    current.Clear();
                    continue;
                }
            }

            current.Append(c);
        }

        segments.Add(current.ToString());
        return [.. segments];
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
