using BenchmarkDotNet.Attributes;
using SparseFragments;
using SparseFragments.CompilerServices;

[SparseFragmentModel]
public partial class BenchSequenceAppendRecord
{
    [SparseMerge(MergeMode.Append)]
    public IReadOnlyList<string> Items { get; set; } = [];
}

[SparseFragmentModel]
public partial class BenchSequenceSetUnionRecord
{
    [SparseMerge(MergeMode.SetUnion)]
    public IReadOnlyList<string> Values { get; set; } = [];
}

/// <summary>
/// Sequence rebase scale benchmarks (issue #59): generated Append and sequence
/// SetUnion rebase plus direct typed-helper calls across sizes 16/256/2048. Covers
/// no-op, local add, local removal, concurrent addition, conflict, many local
/// changes, and many concurrent changes, with scalar element types and a custom
/// comparer scenario for the typed helpers.
/// </summary>
[MemoryDiagnoser]
public class SequenceRebaseBenchmarks
{
    [Params(16, 256, 2048)]
    public int Size { get; set; }

    private List<string> _base = null!;

    private Optional<BenchSequenceAppendRecord.Fragment?> _appendBaseState;
    private BenchSequenceAppendRecord.Patch _appendLocalAdd = null!;
    private BenchSequenceAppendRecord.Patch _appendLocalRemoval = null!;
    private BenchSequenceAppendRecord.Patch _appendLocalMany = null!;
    private Optional<BenchSequenceAppendRecord.Fragment?> _appendCurrentClean;
    private Optional<BenchSequenceAppendRecord.Fragment?> _appendCurrentApplied;
    private Optional<BenchSequenceAppendRecord.Fragment?> _appendCurrentConflict;
    private Optional<BenchSequenceAppendRecord.Fragment?> _appendCurrentMany;

    private Optional<BenchSequenceSetUnionRecord.Fragment?> _setBaseState;
    private BenchSequenceSetUnionRecord.Patch _setLocalAdd = null!;
    private BenchSequenceSetUnionRecord.Patch _setLocalRemoval = null!;
    private BenchSequenceSetUnionRecord.Patch _setLocalMany = null!;
    private Optional<BenchSequenceSetUnionRecord.Fragment?> _setCurrentClean;
    private Optional<BenchSequenceSetUnionRecord.Fragment?> _setCurrentApplied;
    private Optional<BenchSequenceSetUnionRecord.Fragment?> _setCurrentChanged;
    private Optional<BenchSequenceSetUnionRecord.Fragment?> _setCurrentMany;

    private List<string> _directDesiredAdd = null!;
    private List<string> _directDesiredRemoval = null!;
    private List<string> _directDesiredMany = null!;
    private List<string> _directCurrentClean = null!;
    private List<string> _directCurrentApplied = null!;
    private List<string> _directCurrentConflict = null!;
    private List<string> _directCurrentMany = null!;

    private List<int> _intBase = null!;
    private List<int> _intDesiredAdd = null!;
    private List<int> _intCurrentClean = null!;

    private List<string> _ignoreCaseBase = null!;
    private List<string> _ignoreCaseDesiredAdd = null!;
    private List<string> _ignoreCaseCurrentClean = null!;
    private List<string> _ignoreCaseCurrentApplied = null!;

    [GlobalSetup]
    public void Setup()
    {
        _base = new List<string>(Size);
        for (var index = 0; index < Size; index++)
        {
            _base.Add("item-" + index);
        }

        var desiredAdd = new List<string>(_base) { "local-new" };
        var desiredRemoval = new List<string>(_base);
        if (desiredRemoval.Count > 0)
        {
            desiredRemoval.RemoveAt(0);
        }

        var desiredMany = new List<string>();
        for (var index = 0; index < Size; index++)
        {
            desiredMany.Add(index % 2 == 0 ? _base[index] : "local-" + index);
        }

        desiredMany.Add("local-extra-a");
        desiredMany.Add("local-extra-b");

        var currentClean = new List<string>(_base) { "concurrent-new" };
        var currentApplied = new List<string>(desiredAdd);
        var currentConflict = new List<string>(_base);
        if (currentConflict.Count > 0)
        {
            currentConflict[0] = "prefix-changed";
        }
        else
        {
            currentConflict.Add("prefix-changed");
        }

        var currentMany = new List<string>(_base);
        for (var index = 0; index < 8; index++)
        {
            currentMany.Add("concurrent-" + index);
        }

        var currentChangedMany = new List<string>();
        for (var index = 0; index < Size; index++)
        {
            currentChangedMany.Add(index % 3 == 0 ? "conc-" + index : _base[index]);
        }

        currentChangedMany.Add("conc-extra");

        _appendBaseState = AppendState(_base);
        _appendLocalAdd = new BenchSequenceAppendRecord.Patch { Items = desiredAdd };
        _appendLocalRemoval = new BenchSequenceAppendRecord.Patch { Items = desiredRemoval };
        _appendLocalMany = new BenchSequenceAppendRecord.Patch { Items = desiredMany };
        _appendCurrentClean = AppendState(currentClean);
        _appendCurrentApplied = AppendState(currentApplied);
        _appendCurrentConflict = AppendState(currentConflict);
        _appendCurrentMany = AppendState(currentMany);

        _setBaseState = SetState(_base);
        _setLocalAdd = new BenchSequenceSetUnionRecord.Patch { Values = desiredAdd };
        _setLocalRemoval = new BenchSequenceSetUnionRecord.Patch { Values = desiredRemoval };
        _setLocalMany = new BenchSequenceSetUnionRecord.Patch { Values = desiredMany };
        _setCurrentClean = SetState(currentClean);
        _setCurrentApplied = SetState(currentApplied);
        // A removal edit conflicts with any concurrent change.
        _setCurrentChanged = SetState(currentClean);
        _setCurrentMany = SetState(currentChangedMany);

        _directDesiredAdd = desiredAdd;
        _directDesiredRemoval = desiredRemoval;
        _directDesiredMany = desiredMany;
        _directCurrentClean = currentClean;
        _directCurrentApplied = currentApplied;
        _directCurrentConflict = currentConflict;
        _directCurrentMany = currentChangedMany;

        _intBase = new List<int>(Size);
        for (var index = 0; index < Size; index++)
        {
            _intBase.Add(index);
        }

        _intDesiredAdd = new List<int>(_intBase) { -1 };
        _intCurrentClean = new List<int>(_intBase) { -2 };

        _ignoreCaseBase = _base.Select(value => "ITEM-" + value["item-".Length..]).ToList();
        _ignoreCaseDesiredAdd = new List<string>(_ignoreCaseBase) { "LOCAL-NEW" };
        _ignoreCaseCurrentClean = new List<string>(_ignoreCaseBase) { "concurrent-new" };
        _ignoreCaseCurrentApplied = new List<string>(_ignoreCaseDesiredAdd);
    }

    [Benchmark(Description = "Sequence Append rebase (generated): already applied is a no-op")]
    public RebaseResult<BenchSequenceAppendRecord.Patch> Append_NoOp() =>
        BenchSequenceAppendRecord.Patch.Rebase(
            _appendBaseState,
            _appendLocalAdd,
            _appendCurrentApplied
        );

    [Benchmark(Description = "Sequence Append rebase (generated): local append only")]
    public RebaseResult<BenchSequenceAppendRecord.Patch> Append_LocalAdd() =>
        BenchSequenceAppendRecord.Patch.Rebase(_appendBaseState, _appendLocalAdd, _appendBaseState);

    [Benchmark(
        Description = "Sequence Append rebase (generated): local removal replays onto clean state"
    )]
    public RebaseResult<BenchSequenceAppendRecord.Patch> Append_LocalRemoval() =>
        BenchSequenceAppendRecord.Patch.Rebase(
            _appendBaseState,
            _appendLocalRemoval,
            _appendBaseState
        );

    [Benchmark(
        Description = "Sequence Append rebase (generated): clean replay beside concurrent suffix"
    )]
    public RebaseResult<BenchSequenceAppendRecord.Patch> Append_ConcurrentAddition() =>
        BenchSequenceAppendRecord.Patch.Rebase(
            _appendBaseState,
            _appendLocalAdd,
            _appendCurrentClean
        );

    [Benchmark(Description = "Sequence Append rebase (generated): conflict on changed prefix")]
    public RebaseResult<BenchSequenceAppendRecord.Patch> Append_Conflict() =>
        BenchSequenceAppendRecord.Patch.Rebase(
            _appendBaseState,
            _appendLocalAdd,
            _appendCurrentConflict
        );

    [Benchmark(Description = "Sequence Append rebase (generated): many local changes")]
    public RebaseResult<BenchSequenceAppendRecord.Patch> Append_ManyLocalChanges() =>
        BenchSequenceAppendRecord.Patch.Rebase(
            _appendBaseState,
            _appendLocalMany,
            _appendBaseState
        );

    [Benchmark(Description = "Sequence Append rebase (generated): many concurrent changes")]
    public RebaseResult<BenchSequenceAppendRecord.Patch> Append_ManyConcurrentChanges() =>
        BenchSequenceAppendRecord.Patch.Rebase(
            _appendBaseState,
            _appendLocalAdd,
            _appendCurrentMany
        );

    [Benchmark(Description = "Sequence SetUnion rebase (generated): already applied is a no-op")]
    public RebaseResult<BenchSequenceSetUnionRecord.Patch> SetUnion_NoOp() =>
        BenchSequenceSetUnionRecord.Patch.Rebase(_setBaseState, _setLocalAdd, _setCurrentApplied);

    [Benchmark(Description = "Sequence SetUnion rebase (generated): local addition only")]
    public RebaseResult<BenchSequenceSetUnionRecord.Patch> SetUnion_LocalAdd() =>
        BenchSequenceSetUnionRecord.Patch.Rebase(_setBaseState, _setLocalAdd, _setBaseState);

    [Benchmark(
        Description = "Sequence SetUnion rebase (generated): local removal replays onto clean state"
    )]
    public RebaseResult<BenchSequenceSetUnionRecord.Patch> SetUnion_LocalRemoval() =>
        BenchSequenceSetUnionRecord.Patch.Rebase(_setBaseState, _setLocalRemoval, _setBaseState);

    [Benchmark(
        Description = "Sequence SetUnion rebase (generated): clean union beside concurrent addition"
    )]
    public RebaseResult<BenchSequenceSetUnionRecord.Patch> SetUnion_ConcurrentAddition() =>
        BenchSequenceSetUnionRecord.Patch.Rebase(_setBaseState, _setLocalAdd, _setCurrentClean);

    [Benchmark(
        Description = "Sequence SetUnion rebase (generated): removal conflicts with concurrent change"
    )]
    public RebaseResult<BenchSequenceSetUnionRecord.Patch> SetUnion_Conflict() =>
        BenchSequenceSetUnionRecord.Patch.Rebase(
            _setBaseState,
            _setLocalRemoval,
            _setCurrentChanged
        );

    [Benchmark(Description = "Sequence SetUnion rebase (generated): many local changes")]
    public RebaseResult<BenchSequenceSetUnionRecord.Patch> SetUnion_ManyLocalChanges() =>
        BenchSequenceSetUnionRecord.Patch.Rebase(_setBaseState, _setLocalMany, _setBaseState);

    [Benchmark(Description = "Sequence SetUnion rebase (generated): many concurrent changes")]
    public RebaseResult<BenchSequenceSetUnionRecord.Patch> SetUnion_ManyConcurrentChanges() =>
        BenchSequenceSetUnionRecord.Patch.Rebase(_setBaseState, _setLocalAdd, _setCurrentMany);

    [Benchmark(Description = "Sequence Append rebase (typed): already applied is a no-op")]
    public bool TypedAppend_NoOp() =>
        SparseFragmentRuntime.TryRebaseSequenceAppend(
            _base,
            _directDesiredAdd,
            _directCurrentApplied,
            null,
            out _,
            out _
        );

    [Benchmark(Description = "Sequence Append rebase (typed): local append only")]
    public bool TypedAppend_LocalAdd() =>
        SparseFragmentRuntime.TryRebaseSequenceAppend(
            _base,
            _directDesiredAdd,
            _base,
            null,
            out _,
            out _
        );

    [Benchmark(
        Description = "Sequence Append rebase (typed): clean replay beside concurrent suffix"
    )]
    public bool TypedAppend_ConcurrentAddition() =>
        SparseFragmentRuntime.TryRebaseSequenceAppend(
            _base,
            _directDesiredAdd,
            _directCurrentClean,
            null,
            out _,
            out _
        );

    [Benchmark(Description = "Sequence Append rebase (typed): conflict on changed prefix")]
    public bool TypedAppend_Conflict() =>
        SparseFragmentRuntime.TryRebaseSequenceAppend(
            _base,
            _directDesiredAdd,
            _directCurrentConflict,
            null,
            out _,
            out _
        );

    [Benchmark(Description = "Sequence Append rebase (typed): many concurrent changes")]
    public bool TypedAppend_ManyConcurrentChanges() =>
        SparseFragmentRuntime.TryRebaseSequenceAppend(
            _base,
            _directDesiredAdd,
            _directCurrentMany,
            null,
            out _,
            out _
        );

    [Benchmark(Description = "Sequence SetUnion rebase (typed): already applied is a no-op")]
    public bool TypedSetUnion_NoOp() =>
        SparseFragmentRuntime.TryRebaseSequenceSetUnion(
            _base,
            _directDesiredAdd,
            _directCurrentApplied,
            null,
            out _,
            out _
        );

    [Benchmark(Description = "Sequence SetUnion rebase (typed): local addition only")]
    public bool TypedSetUnion_LocalAdd() =>
        SparseFragmentRuntime.TryRebaseSequenceSetUnion(
            _base,
            _directDesiredAdd,
            _base,
            null,
            out _,
            out _
        );

    [Benchmark(
        Description = "Sequence SetUnion rebase (typed): local removal replays onto clean state"
    )]
    public bool TypedSetUnion_LocalRemoval() =>
        SparseFragmentRuntime.TryRebaseSequenceSetUnion(
            _base,
            _directDesiredRemoval,
            _base,
            null,
            out _,
            out _
        );

    [Benchmark(
        Description = "Sequence SetUnion rebase (typed): clean union beside concurrent addition"
    )]
    public bool TypedSetUnion_ConcurrentAddition() =>
        SparseFragmentRuntime.TryRebaseSequenceSetUnion(
            _base,
            _directDesiredAdd,
            _directCurrentClean,
            null,
            out _,
            out _
        );

    [Benchmark(
        Description = "Sequence SetUnion rebase (typed): removal conflicts with concurrent change"
    )]
    public bool TypedSetUnion_Conflict() =>
        SparseFragmentRuntime.TryRebaseSequenceSetUnion(
            _base,
            _directDesiredRemoval,
            _directCurrentClean,
            null,
            out _,
            out _
        );

    [Benchmark(Description = "Sequence SetUnion rebase (typed): many local changes")]
    public bool TypedSetUnion_ManyLocalChanges() =>
        SparseFragmentRuntime.TryRebaseSequenceSetUnion(
            _base,
            _directDesiredMany,
            _base,
            null,
            out _,
            out _
        );

    [Benchmark(Description = "Sequence SetUnion rebase (typed): many concurrent changes")]
    public bool TypedSetUnion_ManyConcurrentChanges() =>
        SparseFragmentRuntime.TryRebaseSequenceSetUnion(
            _base,
            _directDesiredAdd,
            _directCurrentMany,
            null,
            out _,
            out _
        );

    [Benchmark(
        Description = "Sequence SetUnion rebase (typed, int): clean union beside concurrent addition"
    )]
    public bool TypedSetUnion_Int_ConcurrentAddition() =>
        SparseFragmentRuntime.TryRebaseSequenceSetUnion(
            _intBase,
            _intDesiredAdd,
            _intCurrentClean,
            null,
            out _,
            out _
        );

    [Benchmark(Description = "Sequence SetUnion rebase (typed, int): already applied is a no-op")]
    public bool TypedSetUnion_Int_NoOp() =>
        SparseFragmentRuntime.TryRebaseSequenceSetUnion(
            _intBase,
            _intDesiredAdd,
            _intDesiredAdd,
            null,
            out _,
            out _
        );

    [Benchmark(Description = "Sequence SetUnion rebase (typed, OrdinalIgnoreCase): clean union")]
    public bool TypedSetUnion_CustomComparer_ConcurrentAddition() =>
        SparseFragmentRuntime.TryRebaseSequenceSetUnion(
            _ignoreCaseBase,
            _ignoreCaseDesiredAdd,
            _ignoreCaseCurrentClean,
            StringComparer.OrdinalIgnoreCase,
            out _,
            out _
        );

    [Benchmark(
        Description = "Sequence SetUnion rebase (typed, OrdinalIgnoreCase): already applied is a no-op"
    )]
    public bool TypedSetUnion_CustomComparer_NoOp() =>
        SparseFragmentRuntime.TryRebaseSequenceSetUnion(
            _ignoreCaseBase,
            _ignoreCaseDesiredAdd,
            _ignoreCaseCurrentApplied,
            StringComparer.OrdinalIgnoreCase,
            out _,
            out _
        );

    private static Optional<BenchSequenceAppendRecord.Fragment?> AppendState(List<string> items) =>
        Optional<BenchSequenceAppendRecord.Fragment?>.Present(
            new BenchSequenceAppendRecord.Fragment
            {
                Items = Optional<IReadOnlyList<string>>.Present(new List<string>(items)),
            }
        );

    private static Optional<BenchSequenceSetUnionRecord.Fragment?> SetState(List<string> items) =>
        Optional<BenchSequenceSetUnionRecord.Fragment?>.Present(
            new BenchSequenceSetUnionRecord.Fragment
            {
                Values = Optional<IReadOnlyList<string>>.Present(new List<string>(items)),
            }
        );
}
