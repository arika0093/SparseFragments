namespace SparseTypes.Playground.Pages.Case6;

public sealed class CatalogChildSessionCoordinator
{
    private const string WorkMasterPrefix = "work-master:";
    public const string OverviewId = "overview";
    public const string CommercialId = "commercial";
    public const string OperationsId = "operations";
    public const string WorkflowId = "workflow";

    private readonly List<ChildSessionParticipant> _participants = [];

    public static string WorkMasterId(string stepId) => $"{WorkMasterPrefix}{stepId}";

    public IDisposable Register(
        string id,
        Func<bool> hasChanges,
        Func<Task<bool>> accept,
        Action discard
    )
    {
        var participant = new ChildSessionParticipant(id, hasChanges, accept, discard);
        _participants.Add(participant);
        return new Registration(this, participant);
    }

    public bool HasChangesFor(string id) =>
        _participants.Any(participant => participant.Id == id && participant.HasChanges());

    public bool HasPendingChanges => _participants.Any(static participant => participant.HasChanges());

    public void DiscardAll()
    {
        foreach (var participant in _participants.ToArray())
        {
            participant.Discard();
        }
    }

    private void Unregister(ChildSessionParticipant participant) => _participants.Remove(participant);

    private sealed class ChildSessionParticipant
    {
        public ChildSessionParticipant(
            string id,
            Func<bool> hasChanges,
            Func<Task<bool>> accept,
            Action discard
        )
        {
            Id = id;
            HasChanges = hasChanges;
            Accept = accept;
            Discard = discard;
        }

        public string Id { get; }
        public Func<bool> HasChanges { get; }
        public Func<Task<bool>> Accept { get; }
        public Action Discard { get; }
    }

    private sealed class Registration : IDisposable
    {
        private CatalogChildSessionCoordinator? _owner;
        private readonly ChildSessionParticipant _participant;

        public Registration(CatalogChildSessionCoordinator owner, ChildSessionParticipant participant)
        {
            _owner = owner;
            _participant = participant;
        }

        public void Dispose()
        {
            _owner?.Unregister(_participant);
            _owner = null;
        }
    }
}

internal static class CatalogRowState
{
    public static string ClassFor<TItem>(
        IReadOnlyList<TItem> baseline,
        IReadOnlyList<TItem> current,
        TItem item,
        Func<TItem, string> key,
        bool detectReorder = false,
        Func<TItem, bool>? hasChanged = null
    ) => ClassFor(baseline, current, item, key, key, detectReorder, hasChanged);

    public static string ClassFor<TBaseline, TCurrent>(
        IReadOnlyList<TBaseline> baseline,
        IReadOnlyList<TCurrent> current,
        TCurrent item,
        Func<TBaseline, string> baselineKey,
        Func<TCurrent, string> key,
        bool detectReorder = false,
        Func<TCurrent, bool>? hasChanged = null
    )
    {
        var itemKey = key(item);
        if (!baseline.Any(candidate => baselineKey(candidate) == itemKey))
        {
            return "pg-roster-row pg-added";
        }

        if (detectReorder && IsReordered(baseline, current, itemKey, baselineKey, key))
        {
            return "pg-roster-row pg-moved";
        }

        return hasChanged?.Invoke(item) == true ? "pg-roster-row pg-changed" : "pg-roster-row";
    }

    private static bool IsReordered<TBaseline, TCurrent>(
        IReadOnlyList<TBaseline> baseline,
        IReadOnlyList<TCurrent> current,
        string itemKey,
        Func<TBaseline, string> baselineKey,
        Func<TCurrent, string> key
    )
    {
        var currentKeys = current.Select(key).ToHashSet(StringComparer.Ordinal);
        var baselineKeys = baseline.Select(baselineKey).ToHashSet(StringComparer.Ordinal);
        var baselineRank = Rank(baseline, itemKey, currentKeys, baselineKey);
        var currentRank = Rank(current, itemKey, baselineKeys, key);
        return baselineRank != currentRank;
    }

    private static int Rank<TItem>(
        IReadOnlyList<TItem> items,
        string itemKey,
        HashSet<string> includedKeys,
        Func<TItem, string> key
    )
    {
        var rank = 0;
        foreach (var item in items)
        {
            var currentKey = key(item);
            if (currentKey == itemKey)
            {
                return rank;
            }

            if (includedKeys.Contains(currentKey))
            {
                rank++;
            }
        }

        return rank;
    }
}
