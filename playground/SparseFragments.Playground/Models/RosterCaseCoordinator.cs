using SparseFragments;

namespace SparseFragments.Playground.Models;

/// <summary>
/// Single source of truth for Playground Case 3: computes ONE
/// <see cref="PlaygroundRoster.ChangeSet"/> per before/after state via
/// <c>Between</c> and reuses it for highlights, summary, snippets, JSON and
/// applied previews.
/// </summary>
public sealed class RosterCaseCoordinator
{
    private PlaygroundRoster.ChangeSet? _changeSet;
    private PlaygroundRoster.Patch? _patch;
    private RosterEditState? _before;
    private RosterEditState? _after;

    /// <summary>Gets the shared ChangeSet, or null when the state is invalid.</summary>
    public PlaygroundRoster.ChangeSet? ChangeSet => IsInvalid ? null : _changeSet;

    /// <summary>
    /// Gets the baseline-free Patch derived from the shared ChangeSet,
    /// or null when the state is invalid. Used only where a Patch is
    /// specifically needed (highlights, summary, applied preview).
    /// </summary>
    public PlaygroundRoster.Patch? Patch => IsInvalid ? null : _patch;

    /// <summary>Gets whether the last <see cref="Update"/> failed (e.g. duplicate keys).</summary>
    public bool IsInvalid { get; private set; } = true;

    /// <summary>
    /// Recomputes the shared ChangeSet exactly once for the given state.
    /// Failures set <see cref="IsInvalid"/> and clear <see cref="ChangeSet"/>.
    /// </summary>
    public void Update(RosterEditState before, RosterEditState after)
    {
        _before = before;
        _after = after;
        try
        {
            _changeSet = PlaygroundRoster.ChangeSet.Between(
                Optional<PlaygroundRoster.Fragment?>.Present(before.BuildFragment()),
                Optional<PlaygroundRoster.Fragment?>.Present(after.BuildFragment())
            );
            _patch = _changeSet.ToPatch();
            IsInvalid = false;
        }
        catch
        {
            // Duplicate keys and friends surface through the diff tabs, not here.
            _changeSet = null;
            _patch = null;
            IsInvalid = true;
        }
    }

    /// <summary>Row highlights projected from the shared ChangeSet.</summary>
    public (
        Dictionary<QuestRow, RosterRowHighlight> Before,
        Dictionary<QuestRow, RosterRowHighlight> After
    ) Highlights
    {
        get
        {
            if (IsInvalid || _changeSet is null || _before is null || _after is null)
            {
                return (
                    new Dictionary<QuestRow, RosterRowHighlight>(),
                    new Dictionary<QuestRow, RosterRowHighlight>()
                );
            }

            return RosterHighlight.BuildFromChangeSet(_changeSet, _before, _after);
        }
    }

    /// <summary>One-line diff summary projected from the shared ChangeSet.</summary>
    public string Summary
    {
        get
        {
            if (IsInvalid || _changeSet is null || _after is null)
            {
                return "cannot diff: duplicate keys or invalid state";
            }

            var quests = RosterInspection.QuestsTransition(_changeSet);
            if (quests.IsEmpty)
            {
                return "no changes";
            }

            var added = quests.Added.Select(static quest => quest.Id).ToList();
            var removed = quests.Removed.Select(static quest => quest.Id).ToList();
            var edited = quests.Edited.Select(static edit => edit.Key).ToList();
            var order = quests.OrderChanged
                ? quests.AfterOrder.ToList()
                : _after.Rows.Select(static r => r.Id).ToList();
            return $"add: [{string.Join(", ", added)}] | remove: [{string.Join(", ", removed)}] | edit: [{string.Join(", ", edited)}] | order: [{string.Join("→", order)}]";
        }
    }

    /// <summary>Manual C# snippet projected from the shared ChangeSet.</summary>
    public string ManualCSharp(string variableName = "patch")
    {
        if (IsInvalid || _changeSet is null || _after is null)
        {
            return "// Cannot diff: duplicate keys or invalid state. See the ChangeSet JSON tab for the error.";
        }

        return PlaygroundSnippets.RosterManualPatchCSharp(variableName, _changeSet, _after);
    }

    /// <summary>
    /// Static model semantics for the Case 3 models.
    /// This is the compile-time SparseFragments reading of the Case 3 models, independent
    /// of any particular ChangeSet instance.
    /// </summary>
    public string ModelMetadata => RosterInspection.DescribeModelMetadata();

    /// <summary>
    /// Returns the shared ChangeSet or throws, so JSON export / applied preview
    /// reuse the same instance with a consistent invalid-state message.
    /// </summary>
    public PlaygroundRoster.ChangeSet RequireChangeSet()
    {
        if (IsInvalid || _changeSet is null)
        {
            throw new InvalidOperationException("Cannot diff roster state.");
        }

        return _changeSet;
    }

    /// <summary>
    /// Returns the baseline-free Patch derived from the shared ChangeSet or
    /// throws, for applied previews that specifically need a Patch.
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
