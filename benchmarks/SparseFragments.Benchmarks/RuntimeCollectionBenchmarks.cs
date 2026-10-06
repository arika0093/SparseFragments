using BenchmarkDotNet.Attributes;
using SparseFragments;
using SparseFragments.CompilerServices;

[SparseFragmentModel]
public partial class BenchAppendRecord
{
    [SparseMerge(MergeMode.Append)]
    public IReadOnlyList<string> Items { get; set; } = [];
}

[SparseFragmentModel]
public partial class BenchSetRecord
{
    [SparseMerge(MergeMode.SetUnion)]
    public ISet<string> Values { get; set; } = new HashSet<string>(StringComparer.Ordinal);
}

[SparseFragmentModel]
public partial class BenchDictionaryRecord
{
    public Dictionary<string, int> Values { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Small/medium/large runtime collection benchmarks: sequence, set, and
/// dictionary equality; Append and SetUnion merge; Append and SetUnion patch
/// rebase. Covers default and custom comparers plus no-op, one-change, and
/// many-change cases so algorithmic regressions show up as complexity changes.
/// </summary>
[MemoryDiagnoser]
public class RuntimeCollectionBenchmarks
{
    [Params(16, 256, 2048)]
    public int Size { get; set; }

    private List<string> _seqBase = null!;
    private List<string> _seqSame = null!;
    private List<string> _seqOneChange = null!;
    private List<string> _seqManyChanges = null!;

    private HashSet<string> _setBase = null!;
    private HashSet<string> _setSame = null!;
    private HashSet<string> _setOneChange = null!;
    private HashSet<string> _setManyChanges = null!;
    private HashSet<string> _setIgnoreCaseBase = null!;
    private HashSet<string> _setIgnoreCaseSame = null!;
    private HashSet<string> _setIgnoreCaseOneChange = null!;
    private HashSet<string> _setIgnoreCaseManyChanges = null!;

    private Dictionary<string, int> _dictBase = null!;
    private Dictionary<string, int> _dictSame = null!;
    private Dictionary<string, int> _dictOneChange = null!;
    private Dictionary<string, int> _dictManyChanges = null!;
    private Dictionary<string, int> _dictIgnoreCaseBase = null!;
    private Dictionary<string, int> _dictIgnoreCaseSame = null!;
    private Dictionary<string, int> _dictIgnoreCaseOneChange = null!;
    private Dictionary<string, int> _dictIgnoreCaseManyChanges = null!;

    private BenchAppendRecord.Fragment _appendLower = null!;
    private BenchAppendRecord.Fragment _appendHigher = null!;
    private BenchSetRecord.Fragment _setLower = null!;
    private BenchSetRecord.Fragment _setHigher = null!;
    private BenchSetRecord.Fragment _setCustomLower = null!;
    private BenchSetRecord.Fragment _setCustomHigher = null!;

    private Optional<BenchAppendRecord.Fragment?> _appendBaseState;
    private BenchAppendRecord.Patch _appendLocal = null!;
    private Optional<BenchAppendRecord.Fragment?> _appendCurrentClean;
    private Optional<BenchAppendRecord.Fragment?> _appendCurrentApplied;
    private Optional<BenchAppendRecord.Fragment?> _appendCurrentConflict;

    private Optional<BenchSetRecord.Fragment?> _setBaseState;
    private BenchSetRecord.Patch _setLocal = null!;
    private BenchSetRecord.Patch _setLocalRemoval = null!;
    private Optional<BenchSetRecord.Fragment?> _setCurrentClean;
    private Optional<BenchSetRecord.Fragment?> _setCurrentApplied;
    private Optional<BenchSetRecord.Fragment?> _setCurrentConflict;
    private Optional<BenchSetRecord.Fragment?> _setCustomBaseState;
    private BenchSetRecord.Patch _setCustomLocal = null!;
    private Optional<BenchSetRecord.Fragment?> _setCustomCurrentClean;
    private Optional<BenchSetRecord.Fragment?> _setCustomCurrentApplied;

    [GlobalSetup]
    public void Setup()
    {
        var items = new List<string>(Size);
        for (var index = 0; index < Size; index++)
        {
            items.Add("item-" + index);
        }

        _seqBase = items;
        _seqSame = new List<string>(items);
        _seqOneChange = new List<string>(items);
        if (Size > 0)
        {
            _seqOneChange[Size - 1] = "item-changed";
        }

        _seqManyChanges = new List<string>(Size);
        for (var index = 0; index < Size; index++)
        {
            _seqManyChanges.Add(index % 2 == 0 ? items[index] : "changed-" + index);
        }

        _setBase = new HashSet<string>(items, StringComparer.Ordinal);
        _setSame = new HashSet<string>(items, StringComparer.Ordinal);
        _setOneChange = new HashSet<string>(items, StringComparer.Ordinal);
        if (Size > 0)
        {
            _setOneChange.Remove("item-" + (Size - 1));
            _setOneChange.Add("item-changed");
        }

        _setManyChanges = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < Size; index++)
        {
            _setManyChanges.Add(index % 2 == 0 ? "item-" + index : "changed-" + index);
        }

        _setIgnoreCaseBase = new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);
        _setIgnoreCaseSame = new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);
        _setIgnoreCaseOneChange = new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);
        if (Size > 0)
        {
            _setIgnoreCaseOneChange.Remove("item-" + (Size - 1));
            _setIgnoreCaseOneChange.Add("ITEM-CHANGED");
        }

        _setIgnoreCaseManyChanges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < Size; index++)
        {
            _setIgnoreCaseManyChanges.Add(index % 2 == 0 ? "ITEM-" + index : "changed-" + index);
        }

        _dictBase = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < Size; index++)
        {
            _dictBase["key-" + index] = index;
        }

        _dictSame = new Dictionary<string, int>(_dictBase, StringComparer.Ordinal);
        _dictOneChange = new Dictionary<string, int>(_dictBase, StringComparer.Ordinal);
        if (Size > 0)
        {
            _dictOneChange["key-" + (Size - 1)] = -1;
        }

        _dictManyChanges = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < Size; index++)
        {
            _dictManyChanges["key-" + index] = index % 2 == 0 ? index : -1;
        }

        _dictIgnoreCaseBase = new Dictionary<string, int>(_dictBase, StringComparer.OrdinalIgnoreCase);
        _dictIgnoreCaseSame = new Dictionary<string, int>(_dictBase, StringComparer.OrdinalIgnoreCase);
        _dictIgnoreCaseOneChange = new Dictionary<string, int>(
            _dictBase,
            StringComparer.OrdinalIgnoreCase
        );
        if (Size > 0)
        {
            _dictIgnoreCaseOneChange["KEY-" + (Size - 1)] = -1;
        }

        _dictIgnoreCaseManyChanges = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase
        );
        for (var index = 0; index < Size; index++)
        {
            _dictIgnoreCaseManyChanges["KEY-" + index] = index % 2 == 0 ? index : -1;
        }

        var suffix = new List<string>
        {
            "extra-a",
            "extra-b",
            "extra-c",
            "extra-d",
            "extra-e",
            "extra-f",
            "extra-g",
            "extra-h",
        };
        _appendLower = new BenchAppendRecord.Fragment
        {
            Items = Optional<IReadOnlyList<string>>.Present(new List<string>(items)),
        };
        _appendHigher = new BenchAppendRecord.Fragment
        {
            Items = Optional<IReadOnlyList<string>>.Present(suffix),
        };

        var setExtra = new HashSet<string>(StringComparer.Ordinal)
        {
            "extra-a",
            "extra-b",
            "extra-c",
            "extra-d",
        };
        foreach (var item in items.Take(4))
        {
            setExtra.Add(item);
        }

        _setLower = new BenchSetRecord.Fragment
        {
            Values = Optional<ISet<string>>.Present(new HashSet<string>(_setBase)),
        };
        _setHigher = new BenchSetRecord.Fragment
        {
            Values = Optional<ISet<string>>.Present(setExtra),
        };

        var setCustomExtra = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "EXTRA-A",
            "extra-b",
            "Extra-C",
            "extra-d",
        };
        foreach (var item in items.Take(4))
        {
            setCustomExtra.Add(item);
        }

        _setCustomLower = new BenchSetRecord.Fragment
        {
            Values = Optional<ISet<string>>.Present(
                new HashSet<string>(_setIgnoreCaseBase, StringComparer.OrdinalIgnoreCase)
            ),
        };
        _setCustomHigher = new BenchSetRecord.Fragment
        {
            Values = Optional<ISet<string>>.Present(setCustomExtra),
        };

        var appendDesired = new List<string>(items) { "local-append" };
        _appendBaseState = Optional<BenchAppendRecord.Fragment?>.Present(
            new BenchAppendRecord.Fragment
            {
                Items = Optional<IReadOnlyList<string>>.Present(new List<string>(items)),
            }
        );
        _appendLocal = new BenchAppendRecord.Patch { Items = appendDesired };
        _appendCurrentClean = Optional<BenchAppendRecord.Fragment?>.Present(
            new BenchAppendRecord.Fragment
            {
                Items = Optional<IReadOnlyList<string>>.Present(
                    new List<string>(items) { "concurrent-append" }
                ),
            }
        );
        _appendCurrentApplied = Optional<BenchAppendRecord.Fragment?>.Present(
            new BenchAppendRecord.Fragment
            {
                Items = Optional<IReadOnlyList<string>>.Present(new List<string>(appendDesired)),
            }
        );
        var appendConflictItems = new List<string>(items);
        if (Size > 0)
        {
            appendConflictItems[0] = "prefix-changed";
        }
        else
        {
            appendConflictItems.Add("prefix-changed");
        }

        _appendCurrentConflict = Optional<BenchAppendRecord.Fragment?>.Present(
            new BenchAppendRecord.Fragment
            {
                Items = Optional<IReadOnlyList<string>>.Present(appendConflictItems),
            }
        );

        var setDesired = new HashSet<string>(_setBase, StringComparer.Ordinal) { "local-new" };
        var setRemoval = new HashSet<string>(_setBase, StringComparer.Ordinal);
        if (Size > 0)
        {
            setRemoval.Remove("item-0");
        }

        _setBaseState = Optional<BenchSetRecord.Fragment?>.Present(
            new BenchSetRecord.Fragment
            {
                Values = Optional<ISet<string>>.Present(new HashSet<string>(_setBase)),
            }
        );
        _setLocal = new BenchSetRecord.Patch { Values = setDesired };
        _setLocalRemoval = new BenchSetRecord.Patch { Values = setRemoval };
        _setCurrentClean = Optional<BenchSetRecord.Fragment?>.Present(
            new BenchSetRecord.Fragment
            {
                Values = Optional<ISet<string>>.Present(
                    new HashSet<string>(_setBase, StringComparer.Ordinal) { "concurrent-new" }
                ),
            }
        );
        _setCurrentApplied = Optional<BenchSetRecord.Fragment?>.Present(
            new BenchSetRecord.Fragment
            {
                Values = Optional<ISet<string>>.Present(
                    new HashSet<string>(setDesired, StringComparer.Ordinal)
                ),
            }
        );
        // A removal edit cannot rebase onto a concurrently changed set.
        _setCurrentConflict = _setCurrentClean;

        var setCustomDesired = new HashSet<string>(
            _setIgnoreCaseBase,
            StringComparer.OrdinalIgnoreCase
        )
        {
            "LOCAL-NEW",
        };
        _setCustomBaseState = Optional<BenchSetRecord.Fragment?>.Present(
            new BenchSetRecord.Fragment
            {
                Values = Optional<ISet<string>>.Present(
                    new HashSet<string>(_setIgnoreCaseBase, StringComparer.OrdinalIgnoreCase)
                ),
            }
        );
        _setCustomLocal = new BenchSetRecord.Patch { Values = setCustomDesired };
        _setCustomCurrentClean = Optional<BenchSetRecord.Fragment?>.Present(
            new BenchSetRecord.Fragment
            {
                Values = Optional<ISet<string>>.Present(
                    new HashSet<string>(_setIgnoreCaseBase, StringComparer.OrdinalIgnoreCase)
                    {
                        "CONCURRENT-NEW",
                    }
                ),
            }
        );
        _setCustomCurrentApplied = Optional<BenchSetRecord.Fragment?>.Present(
            new BenchSetRecord.Fragment
            {
                Values = Optional<ISet<string>>.Present(
                    new HashSet<string>(setCustomDesired, StringComparer.OrdinalIgnoreCase)
                ),
            }
        );
    }

    [Benchmark(Description = "Sequence equality: no-op (equal inputs)")]
    public bool SequenceEquality_NoChange() =>
        SparseFragmentRuntime.AreEqual(_seqBase, _seqSame);

    [Benchmark(Description = "Sequence equality: one trailing change")]
    public bool SequenceEquality_OneChange() =>
        SparseFragmentRuntime.AreEqual(_seqBase, _seqOneChange);

    [Benchmark(Description = "Sequence equality: many changes")]
    public bool SequenceEquality_ManyChanges() =>
        SparseFragmentRuntime.AreEqual(_seqBase, _seqManyChanges);

    [Benchmark(Description = "Set equality: no-op, default comparer")]
    public bool SetEquality_NoChange() =>
        SparseFragmentRuntime.AreSetEqual(_setBase, _setSame);

    [Benchmark(Description = "Set equality: one change, default comparer")]
    public bool SetEquality_OneChange() =>
        SparseFragmentRuntime.AreSetEqual(_setBase, _setOneChange);

    [Benchmark(Description = "Set equality: many changes, default comparer")]
    public bool SetEquality_ManyChanges() =>
        SparseFragmentRuntime.AreSetEqual(_setBase, _setManyChanges);

    [Benchmark(Description = "Set equality: no-op, custom comparer")]
    public bool SetEquality_NoChange_CustomComparer() =>
        SparseFragmentRuntime.AreSetEqual(_setIgnoreCaseBase, _setIgnoreCaseSame);

    [Benchmark(Description = "Set equality: one change, custom comparer")]
    public bool SetEquality_OneChange_CustomComparer() =>
        SparseFragmentRuntime.AreSetEqual(_setIgnoreCaseBase, _setIgnoreCaseOneChange);

    [Benchmark(Description = "Set equality: many changes, custom comparer")]
    public bool SetEquality_ManyChanges_CustomComparer() =>
        SparseFragmentRuntime.AreSetEqual(_setIgnoreCaseBase, _setIgnoreCaseManyChanges);

    [Benchmark(Description = "Dictionary equality: no-op, default comparer")]
    public bool DictionaryEquality_NoChange() =>
        SparseFragmentRuntime.AreDictionaryEqual(_dictBase, _dictSame);

    [Benchmark(Description = "Dictionary equality: one change, default comparer")]
    public bool DictionaryEquality_OneChange() =>
        SparseFragmentRuntime.AreDictionaryEqual(_dictBase, _dictOneChange);

    [Benchmark(Description = "Dictionary equality: many changes, default comparer")]
    public bool DictionaryEquality_ManyChanges() =>
        SparseFragmentRuntime.AreDictionaryEqual(_dictBase, _dictManyChanges);

    [Benchmark(Description = "Dictionary equality: no-op, custom comparer")]
    public bool DictionaryEquality_NoChange_CustomComparer() =>
        SparseFragmentRuntime.AreDictionaryEqual(_dictIgnoreCaseBase, _dictIgnoreCaseSame);

    [Benchmark(Description = "Dictionary equality: one change, custom comparer")]
    public bool DictionaryEquality_OneChange_CustomComparer() =>
        SparseFragmentRuntime.AreDictionaryEqual(_dictIgnoreCaseBase, _dictIgnoreCaseOneChange);

    [Benchmark(Description = "Dictionary equality: many changes, custom comparer")]
    public bool DictionaryEquality_ManyChanges_CustomComparer() =>
        SparseFragmentRuntime.AreDictionaryEqual(_dictIgnoreCaseBase, _dictIgnoreCaseManyChanges);

    [Benchmark(Description = "Append merge: lower plus suffix")]
    public BenchAppendRecord.Fragment AppendMerge() => _appendLower.Merge(_appendHigher);

    [Benchmark(Description = "SetUnion merge: default comparer")]
    public BenchSetRecord.Fragment SetUnionMerge() => _setLower.Merge(_setHigher);

    [Benchmark(Description = "SetUnion merge: custom comparer")]
    public BenchSetRecord.Fragment SetUnionMerge_CustomComparer() =>
        _setCustomLower.Merge(_setCustomHigher);

    [Benchmark(Description = "Append rebase: already applied is a no-op")]
    public RebaseResult<BenchAppendRecord.Patch> AppendRebase_NoOp() =>
        BenchAppendRecord.Patch.Rebase(_appendBaseState, _appendLocal, _appendCurrentApplied);

    [Benchmark(Description = "Append rebase: clean replay beside concurrent suffix")]
    public RebaseResult<BenchAppendRecord.Patch> AppendRebase_Clean() =>
        BenchAppendRecord.Patch.Rebase(_appendBaseState, _appendLocal, _appendCurrentClean);

    [Benchmark(Description = "Append rebase: conflict on changed prefix")]
    public RebaseResult<BenchAppendRecord.Patch> AppendRebase_Conflict() =>
        BenchAppendRecord.Patch.Rebase(_appendBaseState, _appendLocal, _appendCurrentConflict);

    [Benchmark(Description = "SetUnion rebase: already applied is a no-op")]
    public RebaseResult<BenchSetRecord.Patch> SetUnionRebase_NoOp() =>
        BenchSetRecord.Patch.Rebase(_setBaseState, _setLocal, _setCurrentApplied);

    [Benchmark(Description = "SetUnion rebase: clean union beside concurrent addition")]
    public RebaseResult<BenchSetRecord.Patch> SetUnionRebase_Clean() =>
        BenchSetRecord.Patch.Rebase(_setBaseState, _setLocal, _setCurrentClean);

    [Benchmark(Description = "SetUnion rebase: removal conflicts with concurrent change")]
    public RebaseResult<BenchSetRecord.Patch> SetUnionRebase_Conflict() =>
        BenchSetRecord.Patch.Rebase(_setBaseState, _setLocalRemoval, _setCurrentConflict);

    [Benchmark(Description = "SetUnion rebase: clean union, custom comparer")]
    public RebaseResult<BenchSetRecord.Patch> SetUnionRebase_Clean_CustomComparer() =>
        BenchSetRecord.Patch.Rebase(
            _setCustomBaseState,
            _setCustomLocal,
            _setCustomCurrentClean
        );

    [Benchmark(Description = "SetUnion rebase: already applied, custom comparer")]
    public RebaseResult<BenchSetRecord.Patch> SetUnionRebase_NoOp_CustomComparer() =>
        BenchSetRecord.Patch.Rebase(
            _setCustomBaseState,
            _setCustomLocal,
            _setCustomCurrentApplied
        );
}
