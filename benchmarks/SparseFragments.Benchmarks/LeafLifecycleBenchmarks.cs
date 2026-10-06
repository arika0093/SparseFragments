using BenchmarkDotNet.Attributes;
using SparseFragments;
using SparseFragments.CompilerServices;

public enum LeafDiffScenario
{
    Equal,
    Changed,
    SameReference,
}

/// <summary>Separates leaf diffs from recursive diffs and reference short circuits.</summary>
[MemoryDiagnoser]
public class LeafDiffBenchmarks
{
    [Params(LeafDiffScenario.Equal, LeafDiffScenario.Changed, LeafDiffScenario.SameReference)]
    public LeafDiffScenario Scenario { get; set; }

    private BenchKeyedServer _before = null!;
    private BenchKeyedServer _after = null!;
    private BenchLeafValue _valueBefore;
    private BenchLeafValue _valueAfter;
    private BenchWidgetNested _nestedBefore = null!;
    private BenchWidgetNested _nestedAfter = null!;

    [GlobalSetup]
    public void Setup()
    {
        var changed = Scenario == LeafDiffScenario.Changed;
        _before = new BenchKeyedServer
        {
            Id = "server",
            Name = "leaf",
            Count = 42,
        };
        _after =
            Scenario == LeafDiffScenario.SameReference
                ? _before
                : new BenchKeyedServer
                {
                    Id = "server",
                    Name = "leaf",
                    Count = changed ? 43 : 42,
                };
        _valueBefore = new BenchLeafValue { Name = "value", Count = 42 };
        _valueAfter = new BenchLeafValue { Name = "value", Count = changed ? 43 : 42 };
        _nestedBefore = new BenchWidgetNested
        {
            Host = "parent",
            Port = 80,
            Child = new BenchWidgetNested { Host = "child", Port = 443 },
        };
        _nestedAfter =
            Scenario == LeafDiffScenario.SameReference
                ? _nestedBefore
                : new BenchWidgetNested
                {
                    Host = "parent",
                    Port = 80,
                    Child = new BenchWidgetNested { Host = "child", Port = changed ? 444 : 443 },
                };
        var leaf = LeafClass();
        var value = LeafStruct();
        var nested = Nested();
        if (
            leaf.IsEmpty == changed
            || value.IsEmpty == changed
            || nested.IsEmpty == changed
            || (
                changed
                && (
                    leaf.Count.Value != 43
                    || value.Count.Value != 43
                    || nested.Child.Value!.Port.Value != 444
                )
            )
        )
        {
            throw new InvalidOperationException(
                "Diff must preserve changed values and empty results."
            );
        }

        var other = new BenchKeyedServer { Id = "other" };
        var active = SparseFragmentRuntime.CreateDiffCycleContext();
        active.Add(new KeyValuePair<object, object>(_before, other));
        try
        {
            BenchKeyedServer.Fragment.Diff(_before, other, active, "Leaf");
        }
        catch (NotSupportedException)
        {
            return;
        }

        throw new InvalidOperationException(
            "Internal leaf Diff must reject an active ancestor pair."
        );
    }

    [Benchmark]
    public BenchKeyedServer.Fragment LeafClass() => BenchKeyedServer.Fragment.Diff(_before, _after);

    [Benchmark]
    public BenchLeafValue.Fragment LeafStruct() =>
        BenchLeafValue.Fragment.Diff(_valueBefore, _valueAfter);

    [Benchmark]
    public BenchWidgetNested.Fragment Nested() =>
        BenchWidgetNested.Fragment.Diff(_nestedBefore, _nestedAfter);
}

/// <summary>Measures clone bookkeeping separately from copies of leaf values.</summary>
[MemoryDiagnoser]
public class LeafCloneBenchmarks
{
    private BenchKeyedServer _leaf = null!;
    private BenchLeafValue _value;
    private BenchKeyedServer.Fragment _fragment = null!;
    private BenchLeafValue.Fragment _valueFragment = null!;
    private BenchWidgetNested _nested = null!;
    private BenchWidgetNested.Fragment _nestedFragment = null!;

    [GlobalSetup]
    public void Setup()
    {
        _leaf = new BenchKeyedServer
        {
            Id = "server",
            Name = "leaf",
            Count = 42,
        };
        _value = new BenchLeafValue { Name = "value", Count = 42 };
        _fragment = new BenchKeyedServer.Fragment { Count = Optional<int>.Present(42) };
        _valueFragment = new BenchLeafValue.Fragment
        {
            Name = Optional<string?>.Present(null),
            Count = Optional<int>.Present(42),
        };
        _nested = new BenchWidgetNested { Host = "parent", Port = 80 };
        _nested.Child = _nested;
        _nestedFragment = new BenchWidgetNested.Fragment { Port = Optional<int>.Present(80) };
        // Seed a cycle through the init-only fragment property outside timed operations.
        typeof(BenchWidgetNested.Fragment)
            .GetProperty(nameof(BenchWidgetNested.Fragment.Child))!
            .SetValue(
                _nestedFragment,
                Optional<BenchWidgetNested.Fragment?>.Present(_nestedFragment)
            );
        var leafClone = LeafModel();
        var fragmentClone = LeafFragment();
        var valueClone = ValueFragment();
        var nestedClone = NestedModel();
        var nestedFragmentClone = NestedFragment();
        if (
            ReferenceEquals(_leaf, leafClone)
            || leafClone.Count != 42
            || ValueModel().Count != 42
            || ReferenceEquals(_fragment, fragmentClone)
            || fragmentClone.Count.Value != 42
            || fragmentClone.Name.IsPresent
            || !valueClone.Name.IsPresent
            || valueClone.Name.Value is not null
            || valueClone.Count.Value != 42
            || ReferenceEquals(_nested, nestedClone)
            || !ReferenceEquals(nestedClone.Child, nestedClone)
            || ReferenceEquals(_nestedFragment, nestedFragmentClone)
            || !ReferenceEquals(nestedFragmentClone.Child.Value, nestedFragmentClone)
        )
        {
            throw new InvalidOperationException(
                "Clones must preserve values, presence, and recursive identity."
            );
        }

        var context = SparseFragmentRuntime.CreateCloneContext();
        context.Add(_leaf, leafClone);
        context.Add(_fragment, fragmentClone);
        if (
            !ReferenceEquals(_leaf.DeepClone(context), leafClone)
            || !ReferenceEquals(_fragment.DeepClone(context), fragmentClone)
        )
        {
            throw new InvalidOperationException(
                "Supplied clone contexts must preserve existing mappings."
            );
        }
    }

    [Benchmark]
    public BenchKeyedServer LeafModel() => _leaf.DeepClone();

    [Benchmark]
    public BenchLeafValue ValueModel() => _value.DeepClone();

    [Benchmark]
    public BenchKeyedServer.Fragment LeafFragment() => _fragment.DeepClone();

    [Benchmark]
    public BenchLeafValue.Fragment ValueFragment() => _valueFragment.DeepClone();

    [Benchmark]
    public BenchWidgetNested NestedModel() => _nested.DeepClone();

    [Benchmark]
    public BenchWidgetNested.Fragment NestedFragment() => _nestedFragment.DeepClone();
}
