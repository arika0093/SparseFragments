using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchCloneBranch
{
    public int Value { get; set; }

    [SparseMerge(MergeMode.Append)]
    public List<BenchCloneBranch> Children { get; set; } = new();
}

[MemoryDiagnoser]
public class RecursiveCollectionCloneBenchmarks
{
    [Params(64, 1024)]
    public int NodeCount { get; set; }

    private BenchCloneBranch _tree = null!;
    private BenchCloneBranch _cycle = null!;

    [GlobalSetup]
    public void Setup()
    {
        var nodes = new BenchCloneBranch[NodeCount];
        for (var index = 0; index < nodes.Length; index++)
            nodes[index] = new BenchCloneBranch { Value = index };
        for (var index = 1; index < nodes.Length; index++)
            nodes[(index - 1) / 2].Children.Add(nodes[index]);
        _tree = nodes[0];
        _cycle = new BenchCloneBranch();
        _cycle.Children.Add(_cycle);
        for (var index = 1; index < NodeCount; index++)
            _cycle.Children.Add(_tree);

        var clone = _tree.DeepClone();
        var visited = new HashSet<BenchCloneBranch>();
        var pending = new Stack<BenchCloneBranch>();
        pending.Push(clone);
        while (pending.TryPop(out var node))
        {
            if (!visited.Add(node) || ReferenceEquals(node, nodes[node.Value]))
                throw new InvalidOperationException("Tree clone must copy each distinct node.");
            foreach (var child in node.Children)
                pending.Push(child);
        }
        if (visited.Count != NodeCount)
            throw new InvalidOperationException("Tree clone must retain every node.");
        var cycleClone = _cycle.DeepClone();
        if (
            !ReferenceEquals(cycleClone, cycleClone.Children[0])
            || ReferenceEquals(cycleClone.Children[1], _tree)
            || cycleClone
                .Children.Skip(1)
                .Any(child => !ReferenceEquals(child, cycleClone.Children[1]))
        )
            throw new InvalidOperationException("Graph clone must preserve cycles and aliases.");
    }

    [Benchmark]
    public BenchCloneBranch TreeDeepClone() => _tree.DeepClone();

    [Benchmark]
    public BenchCloneBranch CyclicGraphDeepClone() => _cycle.DeepClone();
}
