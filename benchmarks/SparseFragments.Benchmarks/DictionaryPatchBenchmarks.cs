using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchScalarDictHolder
{
    public Dictionary<string, int> Scores { get; set; } = new();
}

[SparseFragmentModel]
public partial class BenchStructuralDictHolder
{
    public Dictionary<string, BenchKeyedServer> Servers { get; set; } = new();
}

/// <summary>
/// Dictionary patch-algebra benchmarks (issue #56): scalar values
/// (<see cref="BenchScalarDictHolder"/>) and structural values with nested
/// patches (<see cref="BenchStructuralDictHolder"/>) at sizes 16 / 256 / 2048.
/// Covers no-op, 1 edit, many edits, add, remove, mixed, plus
/// <c>Between</c>, <c>Apply</c>, <c>Compose</c>, <c>Invert</c>, and clean /
/// already-applied / conflicting <c>Rebase</c>.
/// </summary>
/// <remarks>
/// Expected asymptotics (N = entry count, K = touched keys):
/// <list type="bullet">
/// <item><c>Between</c>: O(N) — snapshot copies of before/after plus one linear
/// scan each for removals and additions/edits.</item>
/// <item><c>Apply</c>: O(N + K) — one result copy plus O(1) dictionary updates
/// per touched key.</item>
/// <item><c>Compose</c>: O(K1 + K2) in the touched keys via dictionary-indexed
/// set/edit maps.</item>
/// <item><c>Invert</c>: same as <c>Between</c> (implemented as
/// <c>Between(Apply(baseline), baseline)</c>).</item>
/// <item><c>Rebase</c>: O(N + K) — base/current/desired snapshots plus O(1)
/// per-key merge decisions; local touches are resolved with dictionary
/// lookups, never linear scans of the removed list per key.</item>
/// </list>
/// </remarks>
[MemoryDiagnoser]
public class DictionaryPatchBenchmarks
{
    [Params(16, 256, 2048)]
    public int Size { get; set; }

    private Optional<BenchScalarDictHolder.Fragment?> _scalarBase = default!;
    private Optional<BenchScalarDictHolder.Fragment?> _scalarSame = default!;
    private Optional<BenchScalarDictHolder.Fragment?> _scalarOneEdit = default!;
    private Optional<BenchScalarDictHolder.Fragment?> _scalarManyEdits = default!;
    private Optional<BenchScalarDictHolder.Fragment?> _scalarOneAdd = default!;
    private Optional<BenchScalarDictHolder.Fragment?> _scalarOneRemove = default!;
    private Optional<BenchScalarDictHolder.Fragment?> _scalarMixed = default!;
    private Optional<BenchScalarDictHolder.Fragment?> _scalarConcurrentAdd = default!;
    private Optional<BenchScalarDictHolder.Fragment?> _scalarDivergent = default!;
    private BenchScalarDictHolder.Patch _scalarPatchOneEdit = null!;
    private BenchScalarDictHolder.Patch _scalarPatchManyEdits = null!;
    private BenchScalarDictHolder.Patch _scalarPatchMixed = null!;
    private BenchScalarDictHolder.Patch _scalarComposeFirst = null!;
    private BenchScalarDictHolder.Patch _scalarComposeSecond = null!;
    private BenchScalarDictHolder.Patch _scalarRebaseLocal = null!;

    private Optional<BenchStructuralDictHolder.Fragment?> _structuralBase = default!;
    private Optional<BenchStructuralDictHolder.Fragment?> _structuralSame = default!;
    private Optional<BenchStructuralDictHolder.Fragment?> _structuralOneEdit = default!;
    private Optional<BenchStructuralDictHolder.Fragment?> _structuralManyEdits = default!;
    private Optional<BenchStructuralDictHolder.Fragment?> _structuralOneAdd = default!;
    private Optional<BenchStructuralDictHolder.Fragment?> _structuralOneRemove = default!;
    private Optional<BenchStructuralDictHolder.Fragment?> _structuralMixed = default!;
    private Optional<BenchStructuralDictHolder.Fragment?> _structuralConcurrentAdd = default!;
    private Optional<BenchStructuralDictHolder.Fragment?> _structuralDivergent = default!;
    private BenchStructuralDictHolder.Patch _structuralPatchOneEdit = null!;
    private BenchStructuralDictHolder.Patch _structuralPatchManyEdits = null!;
    private BenchStructuralDictHolder.Patch _structuralPatchMixed = null!;
    private BenchStructuralDictHolder.Patch _structuralComposeFirst = null!;
    private BenchStructuralDictHolder.Patch _structuralComposeSecond = null!;
    private BenchStructuralDictHolder.Patch _structuralRebaseLocal = null!;

    private static Dictionary<string, int> BuildScalarBase(int size)
    {
        var values = new Dictionary<string, int>(size);
        for (var index = 0; index < size; index++)
        {
            values["key-" + index] = index;
        }

        return values;
    }

    private static Dictionary<string, BenchKeyedServer> BuildStructuralBase(int size)
    {
        var values = new Dictionary<string, BenchKeyedServer>(size);
        for (var index = 0; index < size; index++)
        {
            values["key-" + index] = new BenchKeyedServer
            {
                Id = "srv-" + index,
                Name = "server-" + index,
                Count = index,
            };
        }

        return values;
    }

    private static Optional<BenchScalarDictHolder.Fragment?> ScalarStateOf(
        Dictionary<string, int> values
    ) =>
        Optional<BenchScalarDictHolder.Fragment?>.Present(
            BenchScalarDictHolder.Fragment.From(new BenchScalarDictHolder { Scores = values })
        );

    private static Optional<BenchStructuralDictHolder.Fragment?> StructuralStateOf(
        Dictionary<string, BenchKeyedServer> values
    ) =>
        Optional<BenchStructuralDictHolder.Fragment?>.Present(
            BenchStructuralDictHolder.Fragment.From(
                new BenchStructuralDictHolder { Servers = values }
            )
        );

    [GlobalSetup]
    public void Setup()
    {
        var scalarBase = BuildScalarBase(Size);
        var scalarSame = new Dictionary<string, int>(scalarBase);
        var scalarOneEdit = new Dictionary<string, int>(scalarBase);
        if (Size > 0)
        {
            scalarOneEdit["key-" + (Size - 1)] = -1;
        }

        var scalarManyEdits = new Dictionary<string, int>(Size);
        for (var index = 0; index < Size; index++)
        {
            scalarManyEdits["key-" + index] = index % 2 == 0 ? index : -index;
        }

        var scalarOneAdd = new Dictionary<string, int>(scalarBase) { ["key-new"] = 1 };
        var scalarOneRemove = new Dictionary<string, int>(scalarBase);
        if (Size > 0)
        {
            scalarOneRemove.Remove("key-0");
        }

        var scalarMixed = new Dictionary<string, int>(Size);
        for (var index = 2; index < Size; index++)
        {
            scalarMixed["key-" + index] = index % 4 == 0 ? -index : index;
        }

        scalarMixed["key-new-a"] = 1;
        scalarMixed["key-new-b"] = 2;

        var scalarConcurrentAdd = new Dictionary<string, int>(scalarBase) { ["key-remote"] = 7 };
        var scalarDivergent = new Dictionary<string, int>(scalarBase);
        if (Size > 0)
        {
            scalarDivergent["key-" + (Size - 1)] = -999;
        }

        _scalarBase = ScalarStateOf(scalarBase);
        _scalarSame = ScalarStateOf(scalarSame);
        _scalarOneEdit = ScalarStateOf(scalarOneEdit);
        _scalarManyEdits = ScalarStateOf(scalarManyEdits);
        _scalarOneAdd = ScalarStateOf(scalarOneAdd);
        _scalarOneRemove = ScalarStateOf(scalarOneRemove);
        _scalarMixed = ScalarStateOf(scalarMixed);
        _scalarConcurrentAdd = ScalarStateOf(scalarConcurrentAdd);
        _scalarDivergent = ScalarStateOf(scalarDivergent);

        _scalarPatchOneEdit = BenchScalarDictHolder.Patch.Between(_scalarBase, _scalarOneEdit);
        _scalarPatchManyEdits = BenchScalarDictHolder.Patch.Between(_scalarBase, _scalarManyEdits);
        _scalarPatchMixed = BenchScalarDictHolder.Patch.Between(_scalarBase, _scalarMixed);
        _scalarComposeFirst = BenchScalarDictHolder.Patch.Between(_scalarBase, _scalarOneEdit);
        _scalarComposeSecond = BenchScalarDictHolder.Patch.Between(_scalarOneEdit, _scalarMixed);
        _scalarRebaseLocal = BenchScalarDictHolder.Patch.Between(_scalarBase, _scalarOneEdit);

        var structuralBase = BuildStructuralBase(Size);
        var structuralSame = structuralBase.ToDictionary(
            kv => kv.Key,
            kv => new BenchKeyedServer
            {
                Id = kv.Value.Id,
                Name = kv.Value.Name,
                Count = kv.Value.Count,
            }
        );
        var structuralOneEdit = structuralBase.ToDictionary(
            kv => kv.Key,
            kv => new BenchKeyedServer
            {
                Id = kv.Value.Id,
                Name = kv.Value.Name,
                Count = kv.Value.Count,
            }
        );
        if (Size > 0)
        {
            structuralOneEdit["key-" + (Size - 1)] = new BenchKeyedServer
            {
                Id = "srv-" + (Size - 1),
                Name = "server-changed",
                Count = -1,
            };
        }

        var structuralManyEdits = new Dictionary<string, BenchKeyedServer>(Size);
        for (var index = 0; index < Size; index++)
        {
            structuralManyEdits["key-" + index] =
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
                    };
        }

        var structuralOneAdd = structuralBase.ToDictionary(
            kv => kv.Key,
            kv => new BenchKeyedServer
            {
                Id = kv.Value.Id,
                Name = kv.Value.Name,
                Count = kv.Value.Count,
            }
        );
        structuralOneAdd["key-new"] = new BenchKeyedServer
        {
            Id = "srv-new",
            Name = "new-server",
            Count = 1,
        };

        var structuralOneRemove = structuralBase
            .Where(kv => kv.Key != "key-0")
            .ToDictionary(
                kv => kv.Key,
                kv => new BenchKeyedServer
                {
                    Id = kv.Value.Id,
                    Name = kv.Value.Name,
                    Count = kv.Value.Count,
                }
            );

        var structuralMixed = new Dictionary<string, BenchKeyedServer>(Size);
        for (var index = 2; index < Size; index++)
        {
            structuralMixed["key-" + index] =
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
                    };
        }

        structuralMixed["key-new-a"] = new BenchKeyedServer
        {
            Id = "srv-new-a",
            Name = "new-a",
            Count = 1,
        };
        structuralMixed["key-new-b"] = new BenchKeyedServer
        {
            Id = "srv-new-b",
            Name = "new-b",
            Count = 2,
        };

        var structuralConcurrentAdd = structuralBase.ToDictionary(
            kv => kv.Key,
            kv => new BenchKeyedServer
            {
                Id = kv.Value.Id,
                Name = kv.Value.Name,
                Count = kv.Value.Count,
            }
        );
        structuralConcurrentAdd["key-remote"] = new BenchKeyedServer
        {
            Id = "srv-remote",
            Name = "remote",
            Count = 7,
        };

        var structuralDivergent = structuralBase.ToDictionary(
            kv => kv.Key,
            kv => new BenchKeyedServer
            {
                Id = kv.Value.Id,
                Name = kv.Value.Name,
                Count = kv.Value.Count,
            }
        );
        if (Size > 0)
        {
            structuralDivergent["key-" + (Size - 1)] = new BenchKeyedServer
            {
                Id = "srv-" + (Size - 1),
                Name = "server-conflict",
                Count = -999,
            };
        }

        _structuralBase = StructuralStateOf(structuralBase);
        _structuralSame = StructuralStateOf(structuralSame);
        _structuralOneEdit = StructuralStateOf(structuralOneEdit);
        _structuralManyEdits = StructuralStateOf(structuralManyEdits);
        _structuralOneAdd = StructuralStateOf(structuralOneAdd);
        _structuralOneRemove = StructuralStateOf(structuralOneRemove);
        _structuralMixed = StructuralStateOf(structuralMixed);
        _structuralConcurrentAdd = StructuralStateOf(structuralConcurrentAdd);
        _structuralDivergent = StructuralStateOf(structuralDivergent);

        _structuralPatchOneEdit = BenchStructuralDictHolder.Patch.Between(
            _structuralBase,
            _structuralOneEdit
        );
        _structuralPatchManyEdits = BenchStructuralDictHolder.Patch.Between(
            _structuralBase,
            _structuralManyEdits
        );
        _structuralPatchMixed = BenchStructuralDictHolder.Patch.Between(
            _structuralBase,
            _structuralMixed
        );
        _structuralComposeFirst = BenchStructuralDictHolder.Patch.Between(
            _structuralBase,
            _structuralOneEdit
        );
        _structuralComposeSecond = BenchStructuralDictHolder.Patch.Between(
            _structuralOneEdit,
            _structuralMixed
        );
        _structuralRebaseLocal = BenchStructuralDictHolder.Patch.Between(
            _structuralBase,
            _structuralOneEdit
        );
    }

    [Benchmark(Description = "Scalar dict Between: no-op (semantically equal state)")]
    public BenchScalarDictHolder.Patch Scalar_Between_NoOp() =>
        BenchScalarDictHolder.Patch.Between(_scalarBase, _scalarSame);

    [Benchmark(Description = "Scalar dict Between: one value edit")]
    public BenchScalarDictHolder.Patch Scalar_Between_OneEdit() =>
        BenchScalarDictHolder.Patch.Between(_scalarBase, _scalarOneEdit);

    [Benchmark(Description = "Scalar dict Between: many value edits")]
    public BenchScalarDictHolder.Patch Scalar_Between_ManyEdits() =>
        BenchScalarDictHolder.Patch.Between(_scalarBase, _scalarManyEdits);

    [Benchmark(Description = "Scalar dict Between: one add")]
    public BenchScalarDictHolder.Patch Scalar_Between_OneAdd() =>
        BenchScalarDictHolder.Patch.Between(_scalarBase, _scalarOneAdd);

    [Benchmark(Description = "Scalar dict Between: one remove")]
    public BenchScalarDictHolder.Patch Scalar_Between_OneRemove() =>
        BenchScalarDictHolder.Patch.Between(_scalarBase, _scalarOneRemove);

    [Benchmark(Description = "Scalar dict Between: add/remove/edit mixed")]
    public BenchScalarDictHolder.Patch Scalar_Between_Mixed() =>
        BenchScalarDictHolder.Patch.Between(_scalarBase, _scalarMixed);

    [Benchmark(Description = "Scalar dict Apply: many value edits")]
    public Optional<BenchScalarDictHolder.Fragment?> Scalar_Apply_ManyEdits() =>
        _scalarPatchManyEdits.Apply(_scalarBase);

    [Benchmark(Description = "Scalar dict Apply: add/remove/edit mixed")]
    public Optional<BenchScalarDictHolder.Fragment?> Scalar_Apply_Mixed() =>
        _scalarPatchMixed.Apply(_scalarBase);

    [Benchmark(Description = "Scalar dict Compose: one edit followed by mixed changes")]
    public BenchScalarDictHolder.Patch Scalar_Compose() =>
        _scalarComposeFirst.Compose(_scalarComposeSecond);

    [Benchmark(Description = "Scalar dict Invert: mixed patch against its baseline")]
    public BenchScalarDictHolder.Patch Scalar_Invert() => _scalarPatchMixed.Invert(_scalarBase);

    [Benchmark(Description = "Scalar dict Rebase: clean replay beside a concurrent add")]
    public RebaseResult<BenchScalarDictHolder.Patch> Scalar_Rebase_Clean() =>
        BenchScalarDictHolder.Patch.Rebase(_scalarBase, _scalarRebaseLocal, _scalarConcurrentAdd);

    [Benchmark(Description = "Scalar dict Rebase: already applied is a no-op")]
    public RebaseResult<BenchScalarDictHolder.Patch> Scalar_Rebase_AlreadyApplied() =>
        BenchScalarDictHolder.Patch.Rebase(_scalarBase, _scalarRebaseLocal, _scalarOneEdit);

    [Benchmark(Description = "Scalar dict Rebase: conflicting concurrent edit")]
    public RebaseResult<BenchScalarDictHolder.Patch> Scalar_Rebase_Conflict() =>
        BenchScalarDictHolder.Patch.Rebase(_scalarBase, _scalarRebaseLocal, _scalarDivergent);

    [Benchmark(Description = "Structural dict Between: no-op (semantically equal state)")]
    public BenchStructuralDictHolder.Patch Structural_Between_NoOp() =>
        BenchStructuralDictHolder.Patch.Between(_structuralBase, _structuralSame);

    [Benchmark(Description = "Structural dict Between: one nested value edit")]
    public BenchStructuralDictHolder.Patch Structural_Between_OneEdit() =>
        BenchStructuralDictHolder.Patch.Between(_structuralBase, _structuralOneEdit);

    [Benchmark(Description = "Structural dict Between: many nested value edits")]
    public BenchStructuralDictHolder.Patch Structural_Between_ManyEdits() =>
        BenchStructuralDictHolder.Patch.Between(_structuralBase, _structuralManyEdits);

    [Benchmark(Description = "Structural dict Between: one add")]
    public BenchStructuralDictHolder.Patch Structural_Between_OneAdd() =>
        BenchStructuralDictHolder.Patch.Between(_structuralBase, _structuralOneAdd);

    [Benchmark(Description = "Structural dict Between: one remove")]
    public BenchStructuralDictHolder.Patch Structural_Between_OneRemove() =>
        BenchStructuralDictHolder.Patch.Between(_structuralBase, _structuralOneRemove);

    [Benchmark(Description = "Structural dict Between: add/remove/edit mixed")]
    public BenchStructuralDictHolder.Patch Structural_Between_Mixed() =>
        BenchStructuralDictHolder.Patch.Between(_structuralBase, _structuralMixed);

    [Benchmark(Description = "Structural dict Apply: one nested value edit")]
    public Optional<BenchStructuralDictHolder.Fragment?> Structural_Apply_OneEdit() =>
        _structuralPatchOneEdit.Apply(_structuralBase);

    [Benchmark(Description = "Structural dict Apply: many nested value edits")]
    public Optional<BenchStructuralDictHolder.Fragment?> Structural_Apply_ManyEdits() =>
        _structuralPatchManyEdits.Apply(_structuralBase);

    [Benchmark(Description = "Structural dict Compose: one edit followed by mixed changes")]
    public BenchStructuralDictHolder.Patch Structural_Compose() =>
        _structuralComposeFirst.Compose(_structuralComposeSecond);

    [Benchmark(Description = "Structural dict Invert: mixed patch against its baseline")]
    public BenchStructuralDictHolder.Patch Structural_Invert() =>
        _structuralPatchMixed.Invert(_structuralBase);

    [Benchmark(Description = "Structural dict Rebase: clean replay beside a concurrent add")]
    public RebaseResult<BenchStructuralDictHolder.Patch> Structural_Rebase_Clean() =>
        BenchStructuralDictHolder.Patch.Rebase(
            _structuralBase,
            _structuralRebaseLocal,
            _structuralConcurrentAdd
        );

    [Benchmark(Description = "Structural dict Rebase: already applied is a no-op")]
    public RebaseResult<BenchStructuralDictHolder.Patch> Structural_Rebase_AlreadyApplied() =>
        BenchStructuralDictHolder.Patch.Rebase(
            _structuralBase,
            _structuralRebaseLocal,
            _structuralOneEdit
        );

    [Benchmark(Description = "Structural dict Rebase: conflicting concurrent edit")]
    public RebaseResult<BenchStructuralDictHolder.Patch> Structural_Rebase_Conflict() =>
        BenchStructuralDictHolder.Patch.Rebase(
            _structuralBase,
            _structuralRebaseLocal,
            _structuralDivergent
        );
}
