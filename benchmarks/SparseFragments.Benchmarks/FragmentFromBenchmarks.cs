using BenchmarkDotNet.Attributes;
using SparseFragments;
using SparseFragments.CompilerServices;

[SparseFragmentModel]
public partial class BenchWidgetNested
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; }

    public BenchWidgetNested? Child { get; set; }
}

[SparseFragmentModel]
public partial struct BenchLeafValue
{
    public int Count { get; set; }
    public string? Name { get; set; }
}

/// <summary>Separates leaf model conversion from recursive graph conversion.</summary>
[MemoryDiagnoser]
public class FragmentFromBenchmarks
{
    private BenchKeyedServer _leaf = null!;
    private BenchLeafValue _value;
    private BenchWidgetNested _nested = null!;

    [GlobalSetup]
    public void Setup()
    {
        _leaf = new BenchKeyedServer
        {
            Id = "server-1",
            Name = "leaf",
            Count = 42,
        };
        _value = new BenchLeafValue { Count = 42, Name = "value" };
        _nested = new BenchWidgetNested
        {
            Host = "parent",
            Port = 80,
            Child = new BenchWidgetNested { Host = "child", Port = 443 },
        };
        if (
            LeafClass().Count.Value != 42
            || LeafClass().Name.Value != "leaf"
            || LeafStruct().Count.Value != 42
            || LeafStruct().Name.Value != "value"
            || Nested().Child.Value!.Port.Value != 443
        )
        {
            throw new InvalidOperationException("From must preserve leaf and nested model values.");
        }

        var active = SparseFragmentRuntime.CreateFromCycleContext();
        active.Add(_leaf);
        var rejectedActiveAncestor = false;
        try
        {
            BenchKeyedServer.Fragment.From(
                _leaf,
                SparseFragmentRuntime.CreateCloneContext(),
                active,
                "Leaf"
            );
        }
        catch (NotSupportedException)
        {
            rejectedActiveAncestor = true;
        }

        if (!rejectedActiveAncestor)
        {
            throw new InvalidOperationException(
                "Internal From must reject an already active ancestor even for a leaf projection."
            );
        }

        var cyclic = new BenchWidgetNested();
        cyclic.Child = cyclic;
        try
        {
            BenchWidgetNested.Fragment.From(cyclic);
        }
        catch (NotSupportedException)
        {
            return;
        }

        throw new InvalidOperationException("Recursive From must reject cyclic model graphs.");
    }

    [Benchmark]
    public BenchKeyedServer.Fragment LeafClass() => BenchKeyedServer.Fragment.From(_leaf);

    [Benchmark]
    public BenchLeafValue.Fragment LeafStruct() => BenchLeafValue.Fragment.From(_value);

    [Benchmark]
    public BenchWidgetNested.Fragment Nested() => BenchWidgetNested.Fragment.From(_nested);
}
