using SparseFragments;

namespace SparseFragments.Playground.Models;

/// <summary>
/// Single source of truth for Playground Case 3: computes ONE
/// <see cref="PlaygroundRoster.Patch"/> per before/after state via
/// <c>Between</c> and reuses it for highlights, summary and snippets.
/// </summary>
public sealed class RosterCaseCoordinator
{
    private PlaygroundRoster.Patch? _patch;
    private RosterEditState? _before;
    private RosterEditState? _after;

    /// <summary>Gets the shared Patch, or null when the state is invalid.</summary>
    public PlaygroundRoster.Patch? Patch => IsInvalid ? null : _patch;

    /// <summary>Gets whether the last <see cref="Update"/> failed (e.g. duplicate keys).</summary>
    public bool IsInvalid { get; private set; } = true;

    /// <summary>
    /// Recomputes the shared Patch exactly once for the given state.
    /// Failures set <see cref="IsInvalid"/> and clear <see cref="Patch"/>.
    /// </summary>
    public void Update(RosterEditState before, RosterEditState after)
    {
        _before = before;
        _after = after;
        try
        {
            _patch = PlaygroundRoster.Patch.Between(
                Optional<PlaygroundRoster.Fragment?>.Present(before.BuildFragment()),
                Optional<PlaygroundRoster.Fragment?>.Present(after.BuildFragment())
            );
            IsInvalid = false;
        }
        catch
        {
            // Duplicate keys and friends surface through the diff tabs, not here.
            _patch = null;
            IsInvalid = true;
        }
    }

    /// <summary>Row highlights projected from the shared Patch.</summary>
    public (
        Dictionary<QuestRow, RosterRowHighlight> Before,
        Dictionary<QuestRow, RosterRowHighlight> After
    ) Highlights
    {
        get
        {
            if (IsInvalid || _patch is null || _before is null || _after is null)
            {
                return (
                    new Dictionary<QuestRow, RosterRowHighlight>(),
                    new Dictionary<QuestRow, RosterRowHighlight>()
                );
            }

            return RosterHighlight.BuildFromPatch(_patch, _before, _after);
        }
    }

    /// <summary>One-line diff summary projected from the shared Patch.</summary>
    public string Summary
    {
        get
        {
            if (IsInvalid || _patch is null || _after is null)
            {
                return "cannot diff: duplicate keys or invalid state";
            }

            var quests = RosterInspection.QuestsChange(_patch);
            if (quests is null)
            {
                return "no changes";
            }

            if (quests.Kind == SparseChangeKind.Set)
            {
                return $"whole list replaced: [{string.Join(", ", _after.Rows.Select(static r => r.Id))}]";
            }

            if (quests.Kind == SparseChangeKind.Unset || quests.Keyed is null)
            {
                return "whole list cleared";
            }

            var keyed = quests.Keyed;
            var added = keyed.Added.Select(static v => ((PlaygroundQuest)v!).Id).ToList();
            var removed = keyed.RemovedKeys.Select(static k => (string)k!).ToList();
            var edited = keyed.Edited.Select(static e => (string)e.Key!).ToList();
            var order = keyed.HasOrder
                ? keyed.KeyOrder.Select(static k => (string)k!).ToList()
                : _after.Rows.Select(static r => r.Id).ToList();
            return $"add: [{string.Join(", ", added)}] | remove: [{string.Join(", ", removed)}] | edit: [{string.Join(", ", edited)}] | order: [{string.Join("→", order)}]";
        }
    }

    /// <summary>Manual C# snippet projected from the shared Patch.</summary>
    public string ManualCSharp(string variableName = "patch")
    {
        if (IsInvalid || _patch is null || _after is null)
        {
            return "// Cannot diff: duplicate keys or invalid state. See the JSON Patch tab for the error.";
        }

        return PlaygroundSnippets.RosterManualPatchCSharp(variableName, _patch, _after);
    }

    /// <summary>
    /// Static model semantics enumerated directly from <c>T.Sparse.Properties</c> (#72).
    /// This is the compile-time SparseFragments reading of the Case 3 models, independent
    /// of any particular Patch instance.
    /// </summary>
    public string ModelMetadata => RosterInspection.DescribeModelMetadata();

    /// <summary>
    /// Returns the shared Patch or throws, so JSON export / applied preview
    /// reuse the same instance with a consistent invalid-state message.
    /// </summary>
    public PlaygroundRoster.Patch RequirePatch()
    {
        if (IsInvalid || _patch is null)
        {
            throw new InvalidOperationException("Cannot diff roster state.");
        }

        return _patch;
    }
}
