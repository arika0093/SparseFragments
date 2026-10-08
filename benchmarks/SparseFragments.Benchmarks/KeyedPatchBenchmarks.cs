using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchKeyedServer
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}

[SparseFragmentModel]
public partial class BenchKeyedServerHolder
{
    public List<BenchKeyedServer> Items { get; set; } = new();
}

[SparseFragmentModel]
public partial class BenchKeyedGroup
{
    [SparseKey]
    public string Name { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public List<BenchKeyedServer> Members { get; set; } = new();
}

[SparseFragmentModel]
public partial class BenchKeyedGroupHolder
{
    public List<BenchKeyedGroup> Groups { get; set; } = new();
}

/// <summary>
/// Keyed structural sequence patch-algebra benchmarks (issue #56).
/// Covers scalar-payload elements (<see cref="BenchKeyedServer"/>) and
/// structural-payload elements with nested keyed collections
/// (<see cref="BenchKeyedGroup"/> via <see cref="BenchKeyedGroupHolder"/>)
/// at sizes 16 / 256 / 2048: no-op, 1 edit, many edits, add, remove, mixed,
/// reorder-only, reorder+content, plus <c>Between</c>, <c>Apply</c>,
/// <c>Compose</c>, <c>Invert</c>, and clean / already-applied / conflicting
/// <c>Rebase</c>.
/// </summary>
/// <remarks>
/// Expected asymptotics (N = collection size, K = touched keys):
/// <list type="bullet">
/// <item><c>Between</c>: O(N) — one pass builds the before/after key maps plus
/// the after-order list; removed/added/edited derivation is O(N); the final
/// order comparison is O(N). An 8x size step must scale ~8x, never ~64x.</item>
/// <item><c>Apply</c>: O(N + K) — one map build over the source plus O(1) edits
/// per touched key; result materialization is a single pass.</item>
/// <item><c>Compose</c>: O(K1 + K2) in the touched keys — net add/remove/edit
/// maps are dictionary-indexed; order filtering is linear in the order lists.</item>
/// <item><c>Invert</c>: same as <c>Between</c> (implemented as
/// <c>Between(Apply(baseline), baseline)</c>).</item>
/// <item><c>Rebase</c>: O(N + K) — base/current/desired maps are built once and
/// locally touched keys are pre-indexed into comparer-correct sets/maps, so
/// per-key merge decisions are O(1). The pre-index fix in the generated keyed
/// rebase removes the former O(N x K) <c>Exists</c> scans over the local
/// added/removed lists.</item>
/// </list>
/// </remarks>
[MemoryDiagnoser]
public class KeyedPatchBenchmarks
{
    [Params(16, 256, 2048)]
    public int Size { get; set; }

    private Optional<BenchKeyedServerHolder.Fragment?> _baseState = default!;
    private Optional<BenchKeyedServerHolder.Fragment?> _sameState = default!;
    private Optional<BenchKeyedServerHolder.Fragment?> _oneEditState = default!;
    private Optional<BenchKeyedServerHolder.Fragment?> _manyEditsState = default!;
    private Optional<BenchKeyedServerHolder.Fragment?> _oneAddState = default!;
    private Optional<BenchKeyedServerHolder.Fragment?> _oneRemoveState = default!;
    private Optional<BenchKeyedServerHolder.Fragment?> _mixedState = default!;
    private Optional<BenchKeyedServerHolder.Fragment?> _reorderState = default!;
    private Optional<BenchKeyedServerHolder.Fragment?> _reorderContentState = default!;
    private Optional<BenchKeyedServerHolder.Fragment?> _concurrentAddState = default!;
    private Optional<BenchKeyedServerHolder.Fragment?> _divergentState = default!;

    private BenchKeyedServerHolder.Patch _patchOneEdit = null!;
    private BenchKeyedServerHolder.Patch _patchManyEdits = null!;
    private BenchKeyedServerHolder.Patch _patchOneAdd = null!;
    private BenchKeyedServerHolder.Patch _patchMixed = null!;
    private BenchKeyedServerHolder.Patch _patchReorder = null!;
    private BenchKeyedServerHolder.Patch _patchReorderContent = null!;
    private BenchKeyedServerHolder.Patch _composeFirst = null!;
    private BenchKeyedServerHolder.Patch _composeSecond = null!;
    private BenchKeyedServerHolder.Patch _invertPatch = null!;
    private BenchKeyedServerHolder.Patch _rebaseLocal = null!;

    private Optional<BenchKeyedGroupHolder.Fragment?> _groupBaseState = default!;
    private Optional<BenchKeyedGroupHolder.Fragment?> _groupNestedEditState = default!;
    private Optional<BenchKeyedGroupHolder.Fragment?> _groupManyNestedState = default!;
    private Optional<BenchKeyedGroupHolder.Fragment?> _groupConcurrentState = default!;
    private BenchKeyedGroupHolder.Patch _groupNestedPatch = null!;
    private BenchKeyedGroupHolder.Patch _groupManyNestedPatch = null!;
    private BenchKeyedGroupHolder.Patch _groupRebaseLocal = null!;

    private static BenchKeyedServer Server(int index) =>
        new()
        {
            Id = "srv-" + index,
            Name = "server-" + index,
            Count = index,
        };

    private static Optional<BenchKeyedServerHolder.Fragment?> StateOf(
        List<BenchKeyedServer> items
    ) =>
        Optional<BenchKeyedServerHolder.Fragment?>.Present(
            BenchKeyedServerHolder.Fragment.From(new BenchKeyedServerHolder { Items = items })
        );

    private static Optional<BenchKeyedGroupHolder.Fragment?> GroupStateOf(
        List<BenchKeyedGroup> groups
    ) =>
        Optional<BenchKeyedGroupHolder.Fragment?>.Present(
            BenchKeyedGroupHolder.Fragment.From(new BenchKeyedGroupHolder { Groups = groups })
        );

    [GlobalSetup]
    public void Setup()
    {
        var baseItems = new List<BenchKeyedServer>(Size);
        for (var index = 0; index < Size; index++)
        {
            baseItems.Add(Server(index));
        }

        var sameItems = baseItems
            .Select(item => new BenchKeyedServer
            {
                Id = item.Id,
                Name = item.Name,
                Count = item.Count,
            })
            .ToList();
        var oneEditItems = baseItems
            .Select(item => new BenchKeyedServer
            {
                Id = item.Id,
                Name = item.Name,
                Count = item.Count,
            })
            .ToList();
        if (Size > 0)
        {
            oneEditItems[Size - 1].Name = "server-changed";
            oneEditItems[Size - 1].Count = -1;
        }

        var manyEditsItems = new List<BenchKeyedServer>(Size);
        for (var index = 0; index < Size; index++)
        {
            manyEditsItems.Add(
                index % 2 == 0
                    ? new BenchKeyedServer
                    {
                        Id = "srv-" + index,
                        Name = "server-" + index,
                        Count = index,
                    }
                    : new BenchKeyedServer
                    {
                        Id = "srv-" + index,
                        Name = "changed-" + index,
                        Count = -index,
                    }
            );
        }

        var oneAddItems = baseItems
            .Select(item => new BenchKeyedServer
            {
                Id = item.Id,
                Name = item.Name,
                Count = item.Count,
            })
            .ToList();
        oneAddItems.Add(
            new BenchKeyedServer
            {
                Id = "srv-new",
                Name = "new-server",
                Count = 1,
            }
        );

        var oneRemoveItems = baseItems
            .Skip(1)
            .Select(item => new BenchKeyedServer
            {
                Id = item.Id,
                Name = item.Name,
                Count = item.Count,
            })
            .ToList();

        var mixedItems = new List<BenchKeyedServer>(Size);
        for (var index = 2; index < Size; index++)
        {
            mixedItems.Add(
                index % 4 == 0
                    ? new BenchKeyedServer
                    {
                        Id = "srv-" + index,
                        Name = "changed-" + index,
                        Count = -index,
                    }
                    : new BenchKeyedServer
                    {
                        Id = "srv-" + index,
                        Name = "server-" + index,
                        Count = index,
                    }
            );
        }

        mixedItems.Add(
            new BenchKeyedServer
            {
                Id = "srv-new-a",
                Name = "new-a",
                Count = 1,
            }
        );
        mixedItems.Add(
            new BenchKeyedServer
            {
                Id = "srv-new-b",
                Name = "new-b",
                Count = 2,
            }
        );

        var reorderItems = baseItems
            .Select(item => new BenchKeyedServer
            {
                Id = item.Id,
                Name = item.Name,
                Count = item.Count,
            })
            .ToList();
        reorderItems.Reverse();

        var reorderContentItems = new List<BenchKeyedServer>(Size);
        for (var index = Size - 1; index >= 0; index--)
        {
            reorderContentItems.Add(
                index % 3 == 0
                    ? new BenchKeyedServer
                    {
                        Id = "srv-" + index,
                        Name = "changed-" + index,
                        Count = -index,
                    }
                    : new BenchKeyedServer
                    {
                        Id = "srv-" + index,
                        Name = "server-" + index,
                        Count = index,
                    }
            );
        }

        var concurrentAddItems = baseItems
            .Select(item => new BenchKeyedServer
            {
                Id = item.Id,
                Name = item.Name,
                Count = item.Count,
            })
            .ToList();
        concurrentAddItems.Add(
            new BenchKeyedServer
            {
                Id = "srv-concurrent",
                Name = "concurrent",
                Count = 7,
            }
        );

        var divergentItems = baseItems
            .Select(item => new BenchKeyedServer
            {
                Id = item.Id,
                Name = item.Name,
                Count = item.Count,
            })
            .ToList();
        if (Size > 0)
        {
            divergentItems[Size - 1].Name = "server-conflict";
            divergentItems[Size - 1].Count = -999;
        }

        _baseState = StateOf(baseItems);
        _sameState = StateOf(sameItems);
        _oneEditState = StateOf(oneEditItems);
        _manyEditsState = StateOf(manyEditsItems);
        _oneAddState = StateOf(oneAddItems);
        _oneRemoveState = StateOf(oneRemoveItems);
        _mixedState = StateOf(mixedItems);
        _reorderState = StateOf(reorderItems);
        _reorderContentState = StateOf(reorderContentItems);
        _concurrentAddState = StateOf(concurrentAddItems);
        _divergentState = StateOf(divergentItems);

        _patchOneEdit = BenchKeyedServerHolder.Patch.Between(_baseState, _oneEditState);
        _patchManyEdits = BenchKeyedServerHolder.Patch.Between(_baseState, _manyEditsState);
        _patchOneAdd = BenchKeyedServerHolder.Patch.Between(_baseState, _oneAddState);
        _patchMixed = BenchKeyedServerHolder.Patch.Between(_baseState, _mixedState);
        _patchReorder = BenchKeyedServerHolder.Patch.Between(_baseState, _reorderState);
        _patchReorderContent = BenchKeyedServerHolder.Patch.Between(
            _baseState,
            _reorderContentState
        );
        _composeFirst = BenchKeyedServerHolder.Patch.Between(_baseState, _oneEditState);
        _composeSecond = BenchKeyedServerHolder.Patch.Between(_oneEditState, _mixedState);
        _invertPatch = BenchKeyedServerHolder.Patch.Between(_baseState, _mixedState);
        _rebaseLocal = BenchKeyedServerHolder.Patch.Between(_baseState, _oneEditState);

        var groupCount = Math.Max(1, Size / 4);
        var groupBase = new List<BenchKeyedGroup>(groupCount);
        for (var group = 0; group < groupCount; group++)
        {
            var members = new List<BenchKeyedServer>(4);
            for (var member = 0; member < 4; member++)
            {
                var index = group * 4 + member;
                members.Add(
                    new BenchKeyedServer
                    {
                        Id = "srv-" + index,
                        Name = "server-" + index,
                        Count = index,
                    }
                );
            }

            groupBase.Add(
                new BenchKeyedGroup
                {
                    Name = "group-" + group,
                    Note = "note-" + group,
                    Members = members,
                }
            );
        }

        var groupNestedEdit = groupBase
            .Select(group => new BenchKeyedGroup
            {
                Name = group.Name,
                Note = group.Note,
                Members = group
                    .Members.Select(member => new BenchKeyedServer
                    {
                        Id = member.Id,
                        Name = member.Name,
                        Count = member.Count,
                    })
                    .ToList(),
            })
            .ToList();
        groupNestedEdit[0].Members[0].Name = "nested-changed";

        var groupManyNested = groupBase
            .Select(
                (group, groupIndex) =>
                    new BenchKeyedGroup
                    {
                        Name = group.Name,
                        Note = groupIndex % 2 == 0 ? "note-changed-" + groupIndex : group.Note,
                        Members = group
                            .Members.Select(
                                (member, memberIndex) =>
                                    new BenchKeyedServer
                                    {
                                        Id = member.Id,
                                        Name =
                                            memberIndex == 0 ? "changed-" + member.Id : member.Name,
                                        Count = memberIndex == 0 ? -member.Count : member.Count,
                                    }
                            )
                            .ToList(),
                    }
            )
            .ToList();

        var groupConcurrent = groupBase
            .Select(group => new BenchKeyedGroup
            {
                Name = group.Name,
                Note = group.Note,
                Members = group
                    .Members.Select(member => new BenchKeyedServer
                    {
                        Id = member.Id,
                        Name = member.Name,
                        Count = member.Count,
                    })
                    .ToList(),
            })
            .ToList();
        groupConcurrent.Add(
            new BenchKeyedGroup
            {
                Name = "group-concurrent",
                Note = "new",
                Members = new(),
            }
        );

        _groupBaseState = GroupStateOf(groupBase);
        _groupNestedEditState = GroupStateOf(groupNestedEdit);
        _groupManyNestedState = GroupStateOf(groupManyNested);
        _groupConcurrentState = GroupStateOf(groupConcurrent);
        _groupNestedPatch = BenchKeyedGroupHolder.Patch.Between(
            _groupBaseState,
            _groupNestedEditState
        );
        _groupManyNestedPatch = BenchKeyedGroupHolder.Patch.Between(
            _groupBaseState,
            _groupManyNestedState
        );
        _groupRebaseLocal = BenchKeyedGroupHolder.Patch.Between(
            _groupBaseState,
            _groupNestedEditState
        );
    }

    [Benchmark(Description = "Keyed Between: no-op (semantically equal state)")]
    public BenchKeyedServerHolder.Patch Between_NoOp() =>
        BenchKeyedServerHolder.Patch.Between(_baseState, _sameState);

    [Benchmark(Description = "Keyed Between: one element edit")]
    public BenchKeyedServerHolder.Patch Between_OneEdit() =>
        BenchKeyedServerHolder.Patch.Between(_baseState, _oneEditState);

    [Benchmark(Description = "Keyed Between: many element edits")]
    public BenchKeyedServerHolder.Patch Between_ManyEdits() =>
        BenchKeyedServerHolder.Patch.Between(_baseState, _manyEditsState);

    [Benchmark(Description = "Keyed Between: one add")]
    public BenchKeyedServerHolder.Patch Between_OneAdd() =>
        BenchKeyedServerHolder.Patch.Between(_baseState, _oneAddState);

    [Benchmark(Description = "Keyed Between: one remove")]
    public BenchKeyedServerHolder.Patch Between_OneRemove() =>
        BenchKeyedServerHolder.Patch.Between(_baseState, _oneRemoveState);

    [Benchmark(Description = "Keyed Between: add/remove/edit mixed")]
    public BenchKeyedServerHolder.Patch Between_Mixed() =>
        BenchKeyedServerHolder.Patch.Between(_baseState, _mixedState);

    [Benchmark(Description = "Keyed Between: reorder-only")]
    public BenchKeyedServerHolder.Patch Between_ReorderOnly() =>
        BenchKeyedServerHolder.Patch.Between(_baseState, _reorderState);

    [Benchmark(Description = "Keyed Between: reorder plus content changes")]
    public BenchKeyedServerHolder.Patch Between_ReorderPlusContent() =>
        BenchKeyedServerHolder.Patch.Between(_baseState, _reorderContentState);

    [Benchmark(Description = "Keyed Apply: one element edit")]
    public Optional<BenchKeyedServerHolder.Fragment?> Apply_OneEdit() =>
        _patchOneEdit.Apply(_baseState);

    [Benchmark(Description = "Keyed Apply: many element edits")]
    public Optional<BenchKeyedServerHolder.Fragment?> Apply_ManyEdits() =>
        _patchManyEdits.Apply(_baseState);

    [Benchmark(Description = "Keyed Apply: add/remove/edit mixed")]
    public Optional<BenchKeyedServerHolder.Fragment?> Apply_Mixed() =>
        _patchMixed.Apply(_baseState);

    [Benchmark(Description = "Keyed Apply: reorder-only")]
    public Optional<BenchKeyedServerHolder.Fragment?> Apply_ReorderOnly() =>
        _patchReorder.Apply(_baseState);

    [Benchmark(Description = "Keyed Apply: reorder plus content changes")]
    public Optional<BenchKeyedServerHolder.Fragment?> Apply_ReorderPlusContent() =>
        _patchReorderContent.Apply(_baseState);

    [Benchmark(Description = "Keyed Compose: one edit followed by mixed changes")]
    public BenchKeyedServerHolder.Patch Compose() => _composeFirst.Compose(_composeSecond);

    [Benchmark(Description = "Keyed Invert: mixed patch against its baseline")]
    public BenchKeyedServerHolder.Patch Invert() => _invertPatch.Invert(_baseState);

    [Benchmark(Description = "Keyed Rebase: clean replay beside a concurrent add")]
    public RebaseResult<BenchKeyedServerHolder.Patch> Rebase_Clean() =>
        BenchKeyedServerHolder.Patch.Rebase(_baseState, _rebaseLocal, _concurrentAddState);

    [Benchmark(Description = "Keyed Rebase: already applied is a no-op")]
    public RebaseResult<BenchKeyedServerHolder.Patch> Rebase_AlreadyApplied() =>
        BenchKeyedServerHolder.Patch.Rebase(_baseState, _rebaseLocal, _oneEditState);

    [Benchmark(Description = "Keyed Rebase: conflicting concurrent edit")]
    public RebaseResult<BenchKeyedServerHolder.Patch> Rebase_Conflict() =>
        BenchKeyedServerHolder.Patch.Rebase(_baseState, _rebaseLocal, _divergentState);

    [Benchmark(Description = "Keyed structural Between: one nested member edit")]
    public BenchKeyedGroupHolder.Patch Structural_Between_NestedEdit() =>
        BenchKeyedGroupHolder.Patch.Between(_groupBaseState, _groupNestedEditState);

    [Benchmark(Description = "Keyed structural Between: many nested edits")]
    public BenchKeyedGroupHolder.Patch Structural_Between_ManyNested() =>
        BenchKeyedGroupHolder.Patch.Between(_groupBaseState, _groupManyNestedState);

    [Benchmark(Description = "Keyed structural Apply: one nested member edit")]
    public Optional<BenchKeyedGroupHolder.Fragment?> Structural_Apply_NestedEdit() =>
        _groupNestedPatch.Apply(_groupBaseState);

    [Benchmark(Description = "Keyed structural Apply: many nested edits")]
    public Optional<BenchKeyedGroupHolder.Fragment?> Structural_Apply_ManyNested() =>
        _groupManyNestedPatch.Apply(_groupBaseState);

    [Benchmark(Description = "Keyed structural Compose: nested edits")]
    public BenchKeyedGroupHolder.Patch Structural_Compose() =>
        _groupNestedPatch.Compose(_groupManyNestedPatch);

    [Benchmark(Description = "Keyed structural Invert: nested edits against baseline")]
    public BenchKeyedGroupHolder.Patch Structural_Invert() =>
        _groupNestedPatch.Invert(_groupBaseState);

    [Benchmark(Description = "Keyed structural Rebase: clean replay beside a concurrent group add")]
    public RebaseResult<BenchKeyedGroupHolder.Patch> Structural_Rebase_Clean() =>
        BenchKeyedGroupHolder.Patch.Rebase(
            _groupBaseState,
            _groupRebaseLocal,
            _groupConcurrentState
        );

    [Benchmark(Description = "Keyed structural Rebase: already applied is a no-op")]
    public RebaseResult<BenchKeyedGroupHolder.Patch> Structural_Rebase_AlreadyApplied() =>
        BenchKeyedGroupHolder.Patch.Rebase(
            _groupBaseState,
            _groupRebaseLocal,
            _groupNestedEditState
        );
}

/// <summary>
/// Keyed rebase benchmarks with independent total size N and touched-key count K
/// (issue #56). The local patch edits exactly K distinct keys while the
/// collection holds N elements, so a regression from O(N + K) toward O(N x K)
/// shows up when K grows at fixed N (1 -&gt; 8 -&gt; 64 at N=2048 must stay
/// ~flat, never ~64x) and when N grows at fixed K (8x N step must scale ~8x).
/// Touched counts larger than N are clamped to N.
/// </summary>
[MemoryDiagnoser]
public class KeyedRebaseBenchmarks
{
    [Params(16, 256, 2048)]
    public int Size { get; set; }

    [Params(1, 8, 64)]
    public int Touched { get; set; }

    private Optional<BenchKeyedServerHolder.Fragment?> _baseState = default!;
    private Optional<BenchKeyedServerHolder.Fragment?> _desiredState = default!;
    private Optional<BenchKeyedServerHolder.Fragment?> _concurrentAddState = default!;
    private Optional<BenchKeyedServerHolder.Fragment?> _divergentState = default!;
    private BenchKeyedServerHolder.Patch _local = null!;

    [GlobalSetup]
    public void Setup()
    {
        var touched = Math.Min(Touched, Size);
        var baseItems = new List<BenchKeyedServer>(Size);
        for (var index = 0; index < Size; index++)
        {
            baseItems.Add(
                new BenchKeyedServer
                {
                    Id = "srv-" + index,
                    Name = "server-" + index,
                    Count = index,
                }
            );
        }

        var desiredItems = baseItems
            .Select(
                (item, index) =>
                    index < touched
                        ? new BenchKeyedServer
                        {
                            Id = item.Id,
                            Name = "local-" + index,
                            Count = -index,
                        }
                        : new BenchKeyedServer
                        {
                            Id = item.Id,
                            Name = item.Name,
                            Count = item.Count,
                        }
            )
            .ToList();

        var concurrentAddItems = baseItems
            .Select(item => new BenchKeyedServer
            {
                Id = item.Id,
                Name = item.Name,
                Count = item.Count,
            })
            .ToList();
        concurrentAddItems.Add(
            new BenchKeyedServer
            {
                Id = "srv-remote",
                Name = "remote",
                Count = 1,
            }
        );

        var divergentItems = baseItems
            .Select(
                (item, index) =>
                    index < touched
                        ? new BenchKeyedServer
                        {
                            Id = item.Id,
                            Name = "remote-" + index,
                            Count = 1000 + index,
                        }
                        : new BenchKeyedServer
                        {
                            Id = item.Id,
                            Name = item.Name,
                            Count = item.Count,
                        }
            )
            .ToList();

        _baseState = Optional<BenchKeyedServerHolder.Fragment?>.Present(
            BenchKeyedServerHolder.Fragment.From(new BenchKeyedServerHolder { Items = baseItems })
        );
        _desiredState = Optional<BenchKeyedServerHolder.Fragment?>.Present(
            BenchKeyedServerHolder.Fragment.From(
                new BenchKeyedServerHolder { Items = desiredItems }
            )
        );
        _concurrentAddState = Optional<BenchKeyedServerHolder.Fragment?>.Present(
            BenchKeyedServerHolder.Fragment.From(
                new BenchKeyedServerHolder { Items = concurrentAddItems }
            )
        );
        _divergentState = Optional<BenchKeyedServerHolder.Fragment?>.Present(
            BenchKeyedServerHolder.Fragment.From(
                new BenchKeyedServerHolder { Items = divergentItems }
            )
        );
        _local = BenchKeyedServerHolder.Patch.Between(_baseState, _desiredState);
        var clean = Rebase_Clean();
        var alreadyApplied = Rebase_AlreadyApplied();
        var conflict = Rebase_Conflict();
        var cleanItems = clean.Rebased.Apply(_concurrentAddState).Value!.Items.Value!;
        var appliedItems = alreadyApplied.Rebased.Apply(_desiredState).Value!.Items.Value!;
        var conflictItems = conflict.Rebased.Apply(_divergentState).Value!.Items.Value!;
        if (
            clean.HasConflicts
            || alreadyApplied.HasConflicts
            || !conflict.HasConflicts
            || cleanItems.Count != Size + 1
            || appliedItems.Count != Size
            || conflictItems.Count != Size
            || cleanItems[^1].Id != "srv-remote"
            || cleanItems[^1].Name != "remote"
        )
        {
            throw new InvalidOperationException(
                "Keyed Rebase must preserve concurrent additions and detect conflicts."
            );
        }

        for (var index = 0; index < Size; index++)
        {
            var localName = index < touched ? "local-" + index : "server-" + index;
            var localCount = index < touched ? -index : index;
            if (
                cleanItems[index].Name != localName
                || cleanItems[index].Count != localCount
                || appliedItems[index].Name != localName
                || appliedItems[index].Count != localCount
                || conflictItems[index].Name
                    != (index < touched ? "remote-" + index : "server-" + index)
                || conflictItems[index].Count != (index < touched ? 1000 + index : index)
                || _baseState.Value!.Items.Value![index].Name != "server-" + index
                || _desiredState.Value!.Items.Value![index].Name != localName
            )
            {
                throw new InvalidOperationException(
                    "Keyed Rebase must preserve local edits, untouched elements, and source states."
                );
            }
        }
    }

    [Benchmark(Description = "Keyed Rebase[N,K]: clean replay beside a concurrent add")]
    public RebaseResult<BenchKeyedServerHolder.Patch> Rebase_Clean() =>
        BenchKeyedServerHolder.Patch.Rebase(_baseState, _local, _concurrentAddState);

    [Benchmark(Description = "Keyed Rebase[N,K]: already applied is a no-op")]
    public RebaseResult<BenchKeyedServerHolder.Patch> Rebase_AlreadyApplied() =>
        BenchKeyedServerHolder.Patch.Rebase(_baseState, _local, _desiredState);

    [Benchmark(Description = "Keyed Rebase[N,K]: conflicting concurrent edits on touched keys")]
    public RebaseResult<BenchKeyedServerHolder.Patch> Rebase_Conflict() =>
        BenchKeyedServerHolder.Patch.Rebase(_baseState, _local, _divergentState);
}
