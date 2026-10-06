using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchLargeNested
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; }
}

[SparseFragmentModel]
public partial class BenchLargeRecord
{
    public string? Label { get; set; }

    public int Counter { get; set; }

    public BenchLargeNested? Nested { get; set; }

    [SparseMerge(MergeMode.Append)]
    public IReadOnlyList<string> Items { get; set; } = [];

    [SparseMerge(MergeMode.SetUnion)]
    public ISet<string> Tags { get; set; } = new HashSet<string>(StringComparer.Ordinal);

    public Dictionary<string, int> Scores { get; set; } = new();
}

[SparseFragmentModel]
public partial class BenchChainNode
{
    public string Name { get; set; } = "";

    public BenchChainNode? Next { get; set; }
}

[SparseFragmentModel]
public partial class BenchCloneChild
{
    public int Value { get; set; }

    public List<int> Values { get; set; } = new();
}

[SparseFragmentModel]
public partial class BenchSharedListParent
{
    [SparseMerge(MergeMode.Append)]
    public List<BenchCloneChild> Children { get; set; } = new();
}

[SparseFragmentModel]
public partial class BenchCloneParent
{
    public BenchCloneChild? Left { get; set; }

    public BenchCloneChild? Right { get; set; }

    public BenchCloneChild? Alias { get; set; }
}

/// <summary>
/// Large-collection fragment and patch benchmarks: <c>Fragment.From</c>,
/// <c>Diff</c>, <c>DeepClone</c>, <c>Merge</c>, and patch apply over models
/// that grow from small to large, plus no-op, one-change, and many-change
/// diff shapes.
/// </summary>
[MemoryDiagnoser]
public class LargeFragmentBenchmarks
{
    [Params(64, 512, 2048)]
    public int Size { get; set; }

    private BenchLargeRecord _model = null!;
    private BenchLargeRecord _modelOneChange = null!;
    private BenchLargeRecord _modelManyChanges = null!;
    private BenchLargeRecord.Fragment _fragment = null!;
    private BenchLargeRecord.Fragment _fragmentLower = null!;
    private BenchLargeRecord.Fragment _fragmentHigher = null!;
    private BenchLargeRecord.Fragment _diffOneChange = null!;
    private BenchLargeRecord.Fragment _diffManyChanges = null!;
    private BenchLargeRecord.Patch _patchManyChanges = null!;
    private Optional<BenchLargeRecord.Fragment?> _fragmentState;

    private static BenchLargeRecord BuildModel(int size, int changes)
    {
        var items = new List<string>(size);
        var tags = new HashSet<string>(StringComparer.Ordinal);
        var scores = new Dictionary<string, int>(size);
        for (var index = 0; index < size; index++)
        {
            var name = "item-" + index;
            if (changes == 1 && index == size - 1)
            {
                name = "item-changed";
            }
            else if (changes > 1 && index % 2 == 1)
            {
                name = "changed-" + index;
            }

            items.Add(name);
            tags.Add(name + "-tag");
            scores["key-" + index] = changes > 1 && index % 2 == 1 ? -index : index;
        }

        return new BenchLargeRecord
        {
            Label = changes > 1 ? "many" : "bench",
            Counter = changes == 1 ? 42 : 7,
            Nested = new BenchLargeNested { Host = "db.local", Port = changes + 5432 },
            Items = items,
            Tags = tags,
            Scores = scores,
        };
    }

    [GlobalSetup]
    public void Setup()
    {
        _model = BuildModel(Size, 0);
        _modelOneChange = BuildModel(Size, 1);
        _modelManyChanges = BuildModel(Size, 2);

        _fragment = BenchLargeRecord.Fragment.From(_model);
        _fragmentState = Optional<BenchLargeRecord.Fragment?>.Present(_fragment);

        _fragmentLower = BenchLargeRecord.Fragment.From(_model);
        _fragmentHigher = new BenchLargeRecord.Fragment
        {
            Counter = Optional<int>.Present(99),
            Items = Optional<IReadOnlyList<string>>.Present(
                new List<string> { "extra-a", "extra-b" }
            ),
        };

        _diffOneChange = BenchLargeRecord.Fragment.Diff(_model, _modelOneChange);
        _diffManyChanges = BenchLargeRecord.Fragment.Diff(_model, _modelManyChanges);
        _patchManyChanges = BenchLargeRecord.Patch.Between(
            Optional<BenchLargeRecord.Fragment?>.Present(
                BenchLargeRecord.Fragment.From(_model)
            ),
            Optional<BenchLargeRecord.Fragment?>.Present(
                BenchLargeRecord.Fragment.From(_modelManyChanges)
            )
        );
    }

    [Benchmark(Description = "Fragment.From: large record becomes present")]
    public BenchLargeRecord.Fragment FragmentFrom_Large() =>
        BenchLargeRecord.Fragment.From(_model);

    [Benchmark(Description = "Diff: large record one change")]
    public BenchLargeRecord.Fragment Diff_OneChange() =>
        BenchLargeRecord.Fragment.Diff(_model, _modelOneChange);

    [Benchmark(Description = "Diff: large record many changes")]
    public BenchLargeRecord.Fragment Diff_ManyChanges() =>
        BenchLargeRecord.Fragment.Diff(_model, _modelManyChanges);

    [Benchmark(Description = "DeepClone: large model graph")]
    public BenchLargeRecord ModelDeepClone_Large() => _model.DeepClone();

    [Benchmark(Description = "DeepClone: large fragment")]
    public BenchLargeRecord.Fragment FragmentDeepClone_Large() => _fragment.DeepClone();

    [Benchmark(Description = "Merge: large record plus appended collection")]
    public BenchLargeRecord.Fragment Merge_Large() => _fragmentLower.Merge(_fragmentHigher);

    [Benchmark(Description = "ApplyChanges: apply a large diff onto a fragment")]
    public BenchLargeRecord.Fragment ApplyChanges_Large() =>
        _fragment.ApplyChanges(_diffManyChanges);

    [Benchmark(Description = "Apply: apply a large typed patch onto a fragment")]
    public BenchLargeRecord.Fragment ApplyPatch_Large() => _fragment.Apply(_patchManyChanges);

    [Benchmark(Description = "Patch.Between: large fragment many changes")]
    public BenchLargeRecord.Patch PatchBetween_ManyChanges() =>
        BenchLargeRecord.Patch.Between(
            _fragmentState,
            Optional<BenchLargeRecord.Fragment?>.Present(
                BenchLargeRecord.Fragment.From(_modelManyChanges)
            )
        );
}

/// <summary>
/// Object-graph depth benchmarks: <c>Fragment.From</c>, <c>Diff</c>, and
/// <c>DeepClone</c> over a singly linked chain whose depth grows, pinning the
/// per-level cost of recursive fragment operations.
/// </summary>
[MemoryDiagnoser]
public class ChainDepthBenchmarks
{
    [Params(4, 32, 128)]
    public int Depth { get; set; }

    private BenchChainNode _root = null!;
    private BenchChainNode _rootChanged = null!;
    private BenchChainNode.Fragment _rootFragment = null!;

    private static BenchChainNode BuildChain(int depth, string prefix)
    {
        BenchChainNode? next = null;
        for (var level = depth; level >= 0; level--)
        {
            next = new BenchChainNode { Name = prefix + level, Next = next };
        }

        return next!;
    }

    private static BenchChainNode.Fragment BuildFragmentChain(int depth, string prefix)
    {
        BenchChainNode.Fragment? next = null;
        for (var level = depth; level >= 0; level--)
        {
            next = new BenchChainNode.Fragment
            {
                Name = Optional<string>.Present(prefix + level),
                Next =
                    next is null
                        ? Optional<BenchChainNode.Fragment?>.Missing
                        : Optional<BenchChainNode.Fragment?>.Present(next),
            };
        }

        return next!;
    }

    [GlobalSetup]
    public void Setup()
    {
        _root = BuildChain(Depth, "node-");
        _rootChanged = BuildChain(Depth, "node-");
        var tip = _rootChanged;
        while (tip.Next is not null)
        {
            tip = tip.Next;
        }

        tip.Name = "tip-changed";
        _rootFragment = BuildFragmentChain(Depth, "node-");
    }

    [Benchmark(Description = "Fragment.From: chain depth")]
    public BenchChainNode.Fragment ChainFrom() => BenchChainNode.Fragment.From(_root);

    [Benchmark(Description = "Diff: chain with changed tip")]
    public BenchChainNode.Fragment ChainDiff() =>
        BenchChainNode.Fragment.Diff(_root, _rootChanged);

    [Benchmark(Description = "DeepClone: chain model")]
    public BenchChainNode ChainModelDeepClone() => _root.DeepClone();

    [Benchmark(Description = "DeepClone: chain fragment")]
    public BenchChainNode.Fragment ChainFragmentDeepClone() => _rootFragment.DeepClone();
}

/// <summary>
/// Shared-reference density benchmarks: model and fragment deep clones with
/// either one shared child referenced many times or many distinct children,
/// plus <c>Fragment.From</c> over both shapes.
/// </summary>
[MemoryDiagnoser]
public class SharedReferenceBenchmarks
{
    [Params(64, 512, 2048)]
    public int Size { get; set; }

    private BenchSharedListParent _sharedModel = null!;
    private BenchSharedListParent _distinctModel = null!;
    private BenchCloneParent _sharedParentModel = null!;
    private BenchCloneParent.Fragment _aliasedFragment = null!;

    [GlobalSetup]
    public void Setup()
    {
        var sharedValues = new List<int>(16);
        for (var index = 0; index < 16; index++)
        {
            sharedValues.Add(index);
        }

        var sharedChild = new BenchCloneChild { Value = 1, Values = sharedValues };
        var sharedChildren = new List<BenchCloneChild>(Size);
        for (var index = 0; index < Size; index++)
        {
            sharedChildren.Add(sharedChild);
        }

        _sharedModel = new BenchSharedListParent { Children = sharedChildren };

        var distinctChildren = new List<BenchCloneChild>(Size);
        for (var index = 0; index < Size; index++)
        {
            distinctChildren.Add(new BenchCloneChild { Value = index, Values = new List<int>(sharedValues) });
        }

        _distinctModel = new BenchSharedListParent { Children = distinctChildren };

        var left = new BenchCloneChild { Value = 1, Values = sharedValues };
        var right = new BenchCloneChild { Value = 2, Values = sharedValues };
        _sharedParentModel = new BenchCloneParent
        {
            Left = left,
            Right = right,
            Alias = left,
        };

        var leftFragment = new BenchCloneChild.Fragment
        {
            Value = Optional<int>.Present(1),
            Values = Optional<List<int>>.Present(sharedValues),
        };
        var rightFragment = new BenchCloneChild.Fragment
        {
            Value = Optional<int>.Present(2),
            Values = Optional<List<int>>.Present(sharedValues),
        };
        _aliasedFragment = new BenchCloneParent.Fragment
        {
            Left = Optional<BenchCloneChild.Fragment?>.Present(leftFragment),
            Right = Optional<BenchCloneChild.Fragment?>.Present(rightFragment),
            Alias = Optional<BenchCloneChild.Fragment?>.Present(leftFragment),
        };
    }

    [Benchmark(Description = "Fragment.From: shared child referenced many times")]
    public BenchSharedListParent.Fragment SharedFrom() =>
        BenchSharedListParent.Fragment.From(_sharedModel);

    [Benchmark(Description = "Fragment.From: many distinct children")]
    public BenchSharedListParent.Fragment DistinctFrom() =>
        BenchSharedListParent.Fragment.From(_distinctModel);

    [Benchmark(Description = "DeepClone: shared child referenced many times")]
    public BenchSharedListParent SharedModelDeepClone() => _sharedModel.DeepClone();

    [Benchmark(Description = "DeepClone: many distinct children")]
    public BenchSharedListParent DistinctModelDeepClone() => _distinctModel.DeepClone();

    [Benchmark(Description = "DeepClone: aliased parent model")]
    public BenchCloneParent AliasedModelDeepClone() => _sharedParentModel.DeepClone();

    [Benchmark(Description = "DeepClone: aliased parent fragment")]
    public BenchCloneParent.Fragment AliasedFragmentDeepClone() => _aliasedFragment.DeepClone();
}
