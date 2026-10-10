using System.ComponentModel;

namespace SparseFragments;

/// <summary>Queryable resolution state of one conflict path.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum SparseConflictState
{
    /// <summary>No conflict exists at the queried path.</summary>
    NoConflict = 0,

    /// <summary>A conflict exists but has no covering decision yet.</summary>
    Unresolved = 1,

    /// <summary>The conflict resolves to the incoming (local) value.</summary>
    ResolvedUseIncoming = 2,

    /// <summary>The conflict resolves to the current value.</summary>
    ResolvedUseCurrent = 3,

    /// <summary>The conflict resolves to an explicit custom value.</summary>
    ResolvedCustom = 4,
}

/// <summary>One conflict paired with the value its resolution writes.</summary>
/// <remarks>Produced by <see cref="SparseRebaseResolution{TChange}"/> for generated builders.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class ResolvedChoice
{
    /// <summary>Creates a resolved choice.</summary>
    /// <param name="conflict">The conflict being resolved.</param>
    /// <param name="desired">The presence-aware value to write.</param>
    public ResolvedChoice(SparseConflict conflict, Optional<object?> desired)
    {
        Conflict = conflict ?? throw new ArgumentNullException(nameof(conflict));
        Desired = desired;
    }

    /// <summary>Gets the conflict being resolved.</summary>
    public SparseConflict Conflict { get; }

    /// <summary>Gets the presence-aware value to write.</summary>
    public Optional<object?> Desired { get; }
}

/// <summary>Why a resolution could not build.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class RebaseResolutionFailure
{
    /// <summary>Creates a resolution failure.</summary>
    /// <param name="unresolvedConflicts">Conflicts that remain unresolved.</param>
    /// <param name="reason">A human-readable reason carrying no member values.</param>
    public RebaseResolutionFailure(IEnumerable<SparseConflict> unresolvedConflicts, string reason)
    {
        ArgumentNullException.ThrowIfNull(unresolvedConflicts);
        ArgumentNullException.ThrowIfNull(reason);
        UnresolvedConflicts = Array.AsReadOnly(unresolvedConflicts.ToArray());
        Reason = reason;
    }

    /// <summary>Gets the conflicts that remain unresolved.</summary>
    public IReadOnlyList<SparseConflict> UnresolvedConflicts { get; }

    /// <summary>Gets a human-readable reason carrying no member values.</summary>
    public string Reason { get; }
}

/// <summary>
/// Framework-neutral, mutable resolution state for one <see cref="RebaseResult{TChange}"/>.
/// </summary>
/// <typeparam name="TChange">The generated change-set type.</typeparam>
/// <remarks>
/// <para>
/// Owns the original rebase result (including the nonconflicting
/// <see cref="RebaseResult{TChange}.Rebased"/> contributions) plus one explicit
/// decision per canonical <see cref="SparsePath"/>. The read-only
/// <see cref="RebaseResult{TChange}.Conflicts"/> snapshot stays untouched; the
/// enumerators below are live views over the same conflicts filtered by the
/// current decisions, each returned as a fresh snapshot array.
/// </para>
/// <para>
/// Paths are the canonical #202 identity (root type plus member, typed-key, and
/// index segments). Construct them with the generated <c>T.SparsePath</c> fluent
/// (for example <c>Order.SparsePath.Items.Key(id).Price</c>) or with
/// <see cref="SparsePath"/> factories; identity comparison is the path itself.
/// </para>
/// <para>
/// Overlap is deterministic, never silent: a decision at an ancestor path covers
/// every unresolved conflict below it (<c>UseIncoming</c>/<c>UseCurrent</c> fan out
/// per conflict), while an exact decision always wins over an ancestor. Custom
/// values must target an exact conflict path. Decisions on paths with no conflict
/// at or under them are rejected immediately.
/// </para>
/// <para>
/// Redaction safety: <see cref="SparseConflictKind.RedactedBefore"/> conflicts carry
/// no plaintext, so only <c>UseCurrent</c> (keep, a no-op) is accepted for them;
/// incoming and custom values are rejected rather than fabricated.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public abstract class SparseRebaseResolution<TChange>
{
    private enum DecisionKind
    {
        UseIncoming,
        UseCurrent,
        Custom,
    }

    private sealed record Decision
    {
        public DecisionKind Kind { get; init; }

        public Optional<object?> CustomValue { get; init; }
    }

    private readonly Dictionary<SparsePath, Decision> _decisions = new();

    /// <summary>Creates resolution state for a rebase result.</summary>
    /// <param name="rebase">The original rebase result to resolve.</param>
    protected SparseRebaseResolution(RebaseResult<TChange> rebase)
    {
        Rebase = rebase ?? throw new ArgumentNullException(nameof(rebase));
    }

    /// <summary>Gets the original rebase result being resolved.</summary>
    public RebaseResult<TChange> Rebase { get; }

    /// <summary>Gets the detected conflicts from the original result.</summary>
    public IReadOnlyList<SparseConflict> Conflicts => Rebase.Conflicts;

    /// <summary>Whether the original result detected any conflict.</summary>
    public bool HasConflicts => Rebase.HasConflicts;

    /// <summary>Gets the number of explicit path decisions stored.</summary>
    public int DecidedCount => _decisions.Count;

    /// <summary>Enumerates all conflicts in reported order.</summary>
    /// <returns>A snapshot of every conflict.</returns>
    public IReadOnlyList<SparseConflict> EnumerateConflicts() => Rebase.Conflicts;

    /// <summary>Enumerates conflicts with no covering decision yet.</summary>
    /// <returns>A live-filtered snapshot of unresolved conflicts.</returns>
    public IReadOnlyList<SparseConflict> EnumerateUnresolvedConflicts()
    {
        var unresolved = new List<SparseConflict>();
        foreach (var conflict in Rebase.Conflicts)
        {
            if (!TryGetEffectiveKind(conflict.Path, out _))
            {
                unresolved.Add(conflict);
            }
        }

        return Array.AsReadOnly(unresolved.ToArray());
    }

    /// <summary>Enumerates paths carrying an explicit decision.</summary>
    /// <returns>A snapshot of decided paths for generic consumers.</returns>
    public IReadOnlyList<SparsePath> EnumerateDecidedPaths() =>
        Array.AsReadOnly(_decisions.Keys.ToArray());

    /// <summary>Finds the conflict at an exact path.</summary>
    /// <param name="path">The canonical path to look up.</param>
    /// <returns>The conflict, or <see langword="null"/> when no conflict sits exactly there.</returns>
    public SparseConflict? FindConflict(SparsePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return FindExact(path);
    }

    /// <summary>Finds the conflict at an exact typed path.</summary>
    /// <typeparam name="TModel">The root model type tag.</typeparam>
    /// <typeparam name="TValue">The addressed value type tag.</typeparam>
    /// <param name="path">The canonical typed path to look up.</param>
    /// <returns>The conflict, or <see langword="null"/> when no conflict sits exactly there.</returns>
    public SparseConflict? FindConflict<TModel, TValue>(SparsePath<TModel, TValue> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return FindExact(path.Path);
    }

    /// <summary>Whether a conflict sits exactly at <paramref name="path"/>.</summary>
    /// <param name="path">The canonical path to test.</param>
    /// <returns><see langword="true"/> for an exact conflict match.</returns>
    public bool HasConflict(SparsePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return FindExact(path) is not null;
    }

    /// <summary>Whether a conflict sits exactly at a typed path.</summary>
    /// <typeparam name="TModel">The root model type tag.</typeparam>
    /// <typeparam name="TValue">The addressed value type tag.</typeparam>
    /// <param name="path">The canonical typed path to test.</param>
    /// <returns><see langword="true"/> for an exact conflict match.</returns>
    public bool HasConflict<TModel, TValue>(SparsePath<TModel, TValue> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return FindExact(path.Path) is not null;
    }

    /// <summary>Whether any conflict sits strictly below <paramref name="path"/>.</summary>
    /// <param name="path">The candidate ancestor path.</param>
    /// <returns><see langword="true"/> when at least one conflict descends from <paramref name="path"/>.</returns>
    public bool HasConflictsUnder(SparsePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Rebase.Conflicts.Any(conflict => path.IsAncestorOf(conflict.Path));
    }

    /// <summary>Whether any conflict sits strictly below a typed path.</summary>
    /// <typeparam name="TModel">The root model type tag.</typeparam>
    /// <typeparam name="TValue">The addressed value type tag.</typeparam>
    /// <param name="path">The candidate ancestor path.</param>
    /// <returns><see langword="true"/> when at least one conflict descends from <paramref name="path"/>.</returns>
    public bool HasConflictsUnder<TModel, TValue>(SparsePath<TModel, TValue> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return HasConflictsUnder(path.Path);
    }

    /// <summary>Whether any conflict affects <paramref name="path"/> (exact, ancestor, or descendant).</summary>
    /// <param name="path">The path whose value would change with a resolution.</param>
    /// <returns><see langword="true"/> when a conflict sits at, above, or below <paramref name="path"/>.</returns>
    public bool HasConflictsAffecting(SparsePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Rebase.Conflicts.Any(conflict =>
            conflict.Path.Equals(path)
            || conflict.Path.IsAncestorOf(path)
            || conflict.Path.IsDescendantOf(path)
        );
    }

    /// <summary>Whether any conflict affects a typed path (exact, ancestor, or descendant).</summary>
    /// <typeparam name="TModel">The root model type tag.</typeparam>
    /// <typeparam name="TValue">The addressed value type tag.</typeparam>
    /// <param name="path">The path whose value would change with a resolution.</param>
    /// <returns><see langword="true"/> when a conflict sits at, above, or below <paramref name="path"/>.</returns>
    public bool HasConflictsAffecting<TModel, TValue>(SparsePath<TModel, TValue> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return HasConflictsAffecting(path.Path);
    }

    /// <summary>Gets the lightweight resolution state of one path for generic consumers.</summary>
    /// <param name="path">The canonical path to query.</param>
    /// <returns>The effective state; no per-model members are generated.</returns>
    public SparseConflictState GetState(SparsePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (FindExact(path) is null)
        {
            return SparseConflictState.NoConflict;
        }

        if (!TryGetEffectiveKind(path, out var kind))
        {
            return SparseConflictState.Unresolved;
        }

        return kind switch
        {
            DecisionKind.UseIncoming => SparseConflictState.ResolvedUseIncoming,
            DecisionKind.UseCurrent => SparseConflictState.ResolvedUseCurrent,
            _ => SparseConflictState.ResolvedCustom,
        };
    }

    /// <summary>Gets the lightweight resolution state of one typed path.</summary>
    /// <typeparam name="TModel">The root model type tag.</typeparam>
    /// <typeparam name="TValue">The addressed value type tag.</typeparam>
    /// <param name="path">The canonical typed path to query.</param>
    /// <returns>The effective state; no per-model members are generated.</returns>
    public SparseConflictState GetState<TModel, TValue>(SparsePath<TModel, TValue> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return GetState(path.Path);
    }

    /// <summary>Resolves a path to the incoming (local) value.</summary>
    /// <param name="path">An exact conflict path or an ancestor covering conflicts below it.</param>
    public void UseIncoming(SparsePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        ValidateDecisionTarget(path, custom: false);
        RejectRedactedCoverage(path, incoming: true);
        _decisions[path] = new Decision { Kind = DecisionKind.UseIncoming };
    }

    /// <summary>Resolves a typed path to the incoming (local) value.</summary>
    /// <typeparam name="TModel">The root model type tag.</typeparam>
    /// <typeparam name="TValue">The addressed value type tag.</typeparam>
    /// <param name="path">An exact conflict path or an ancestor covering conflicts below it.</param>
    public void UseIncoming<TModel, TValue>(SparsePath<TModel, TValue> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        UseIncoming(path.Path);
    }

    /// <summary>Resolves a path to the current value (keep).</summary>
    /// <param name="path">An exact conflict path or an ancestor covering conflicts below it.</param>
    public void UseCurrent(SparsePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        ValidateDecisionTarget(path, custom: false);
        _decisions[path] = new Decision { Kind = DecisionKind.UseCurrent };
    }

    /// <summary>Resolves a typed path to the current value (keep).</summary>
    /// <typeparam name="TModel">The root model type tag.</typeparam>
    /// <typeparam name="TValue">The addressed value type tag.</typeparam>
    /// <param name="path">An exact conflict path or an ancestor covering conflicts below it.</param>
    public void UseCurrent<TModel, TValue>(SparsePath<TModel, TValue> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        UseCurrent(path.Path);
    }

    /// <summary>Resolves a typed path to a present custom value.</summary>
    /// <typeparam name="TModel">The root model type tag.</typeparam>
    /// <typeparam name="TValue">The addressed value type.</typeparam>
    /// <param name="path">An exact conflict path.</param>
    /// <param name="value">The custom value, including a present null.</param>
    public void SetValue<TModel, TValue>(SparsePath<TModel, TValue> path, TValue? value)
    {
        ArgumentNullException.ThrowIfNull(path);
        SetBoxedValue(path.Path, BoxValue(value));
    }

    /// <summary>Resolves a typed path to a presence-aware custom value.</summary>
    /// <typeparam name="TModel">The root model type tag.</typeparam>
    /// <typeparam name="TValue">The addressed value type.</typeparam>
    /// <param name="path">An exact conflict path.</param>
    /// <param name="value">The custom value; missing removes, present null clears.</param>
    public void SetValue<TModel, TValue>(SparsePath<TModel, TValue> path, Optional<TValue> value)
    {
        ArgumentNullException.ThrowIfNull(path);
        SetBoxedValue(path.Path, BoxValue(value));
    }

    /// <summary>Resolves a path to missing (removal).</summary>
    /// <param name="path">An exact conflict path.</param>
    public void Remove(SparsePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        SetBoxedValue(path, Optional<object?>.Missing);
    }

    /// <summary>Drops the explicit decision stored at a path.</summary>
    /// <param name="path">The decision path to clear.</param>
    /// <returns><see langword="true"/> when a decision was stored there.</returns>
    public bool ClearDecision(SparsePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return _decisions.Remove(path);
    }

    /// <summary>
    /// Builds a baseline-aware change set holding every clean rebased edit plus the chosen resolutions.
    /// </summary>
    /// <param name="resolved">The resolved change set, or <see langword="null"/> on failure.</param>
    /// <param name="failure">The failure details, or <see langword="null"/> on failure.</param>
    /// <returns><see langword="true"/> when every conflict resolved and the change set built.</returns>
    /// <remarks>Non-mutating: neither the original change set, the input models, nor the session change.</remarks>
    public bool TryBuild(out TChange? resolved, out RebaseResolutionFailure? failure)
    {
        var choices = new List<ResolvedChoice>();
        var unresolved = new List<SparseConflict>();
        foreach (var conflict in Rebase.Conflicts)
        {
            var path = conflict.Path;
            if (EffectiveDecision(path) is not { } decision)
            {
                unresolved.Add(conflict);
                continue;
            }

            if (conflict.Kind == SparseConflictKind.RedactedBefore)
            {
                // Only UseCurrent survives validation for redacted conflicts, and it
                // writes nothing: the member stays excluded, keeping current as-is.
                if (decision.Kind == DecisionKind.UseCurrent)
                {
                    continue;
                }

                unresolved.Add(conflict);
                continue;
            }

            choices.Add(new ResolvedChoice(conflict, DesiredFor(conflict, decision)));
        }

        if (unresolved.Count > 0)
        {
            resolved = default;
            failure = new RebaseResolutionFailure(
                unresolved,
                unresolved.Count
                    + " conflict(s) have no resolution decision. Resolve every conflict before building."
            );
            return false;
        }

        if (!TryCreateResolved(choices, out resolved, out var failureReason))
        {
            resolved = default;
            var failed = new List<SparseConflict>(choices.Count);
            foreach (var choice in choices)
            {
                failed.Add(choice.Conflict);
            }

            failure = new RebaseResolutionFailure(
                failed,
                failureReason ?? "The resolution could not be applied."
            );
            return false;
        }

        failure = null;
        return true;
    }

    /// <summary>Builds the resolved change set from per-conflict desired values.</summary>
    /// <param name="choices">Every conflict with the presence-aware value to write.</param>
    /// <param name="resolved">The resolved change set.</param>
    /// <param name="failureReason">A value-free reason when the build fails.</param>
    /// <returns><see langword="true"/> when the change set built.</returns>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected abstract bool TryCreateResolved(
        IReadOnlyList<ResolvedChoice> choices,
        out TChange? resolved,
        out string? failureReason
    );

    /// <summary>Stores a boxed custom value for an exact conflict path.</summary>
    /// <param name="path">An exact conflict path.</param>
    /// <param name="value">The presence-aware boxed value.</param>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected void SetBoxedValue(SparsePath path, Optional<object?> value)
    {
        ArgumentNullException.ThrowIfNull(path);
        ValidateDecisionTarget(path, custom: true);
        RejectRedactedCoverage(path, incoming: true);
        _decisions[path] = new Decision { Kind = DecisionKind.Custom, CustomValue = value };
    }

    /// <summary>Boxes a present value for storage.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value, including a present null.</param>
    /// <returns>The boxed presence-aware value.</returns>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected static Optional<object?> BoxValue<T>(T? value) =>
        Optional<object?>.Present((object?)value);

    /// <summary>Boxes a presence-aware value for storage.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The presence-aware value.</param>
    /// <returns>The boxed presence-aware value.</returns>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected static Optional<object?> BoxValue<T>(Optional<T> value) =>
        value.IsPresent
            ? Optional<object?>.Present((object?)value.Value)
            : Optional<object?>.Missing;

    private SparseConflict? FindExact(SparsePath path) =>
        Rebase.Conflicts.FirstOrDefault(conflict => conflict.Path.Equals(path));

    private void ValidateDecisionTarget(SparsePath path, bool custom)
    {
        if (FindExact(path) is not null)
        {
            return;
        }

        var covers = Rebase.Conflicts.Any(conflict => path.IsAncestorOf(conflict.Path));
        if (!covers)
        {
            throw new InvalidOperationException(
                "No conflict exists at or under '" + path.ToString() + "'."
            );
        }

        if (custom)
        {
            throw new InvalidOperationException(
                "Custom values require an exact conflict path; '"
                    + path.ToString()
                    + "' only covers conflicts below it. Use UseIncoming or UseCurrent to cover a subtree."
            );
        }
    }

    private void RejectRedactedCoverage(SparsePath path, bool incoming)
    {
        foreach (var conflict in Rebase.Conflicts)
        {
            var candidate = conflict.Path;
            if (
                conflict.Kind == SparseConflictKind.RedactedBefore
                && (candidate.Equals(path) || candidate.IsDescendantOf(path))
            )
            {
                throw new InvalidOperationException(
                    "Redacted-before conflicts carry no plaintext and cannot take "
                        + (incoming ? "incoming or custom values" : "custom values")
                        + "; use UseCurrent to keep the current value."
                );
            }
        }
    }

    private bool TryGetEffectiveKind(SparsePath conflictPath, out DecisionKind kind)
    {
        var decision = EffectiveDecision(conflictPath);
        if (decision is null)
        {
            kind = default;
            return false;
        }

        kind = decision.Kind;
        return true;
    }

    private Decision? EffectiveDecision(SparsePath conflictPath) =>
        NearestDecision(conflictPath) is { } nearest ? _decisions[nearest] : null;

    private static Optional<object?> DesiredFor(SparseConflict conflict, Decision decision) =>
        decision.Kind switch
        {
            DecisionKind.UseIncoming => conflict.LocalValue,
            DecisionKind.UseCurrent => conflict.CurrentValue,
            _ => decision.CustomValue,
        };

    private SparsePath? NearestDecision(SparsePath conflictPath)
    {
        SparsePath? nearest = null;
        foreach (var decided in _decisions.Keys)
        {
            if (!conflictPath.StartsWith(decided))
            {
                continue;
            }

            if (nearest is null || nearest.Depth < decided.Depth)
            {
                nearest = decided;
            }
        }

        return nearest;
    }
}
