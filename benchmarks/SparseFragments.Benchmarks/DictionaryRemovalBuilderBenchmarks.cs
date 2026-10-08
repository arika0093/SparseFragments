using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum DictionaryRemovalBuilderShape
{
    Unique,
    Duplicate,
    RemoveSetRemove,
}

[MemoryDiagnoser]
public class DictionaryRemovalBuilderBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [ParamsAllValues]
    public DictionaryRemovalBuilderShape Shape { get; set; }

    private string[] _keys = [];
    private BenchKeyedServer[] _servers = [];
    private Optional<BenchScalarDictHolder.Fragment?> _scalarBase;
    private Optional<BenchStructuralDictHolder.Fragment?> _structuralBase;

    [GlobalSetup]
    public void Setup()
    {
        _keys = Enumerable.Range(0, Size).Select(index => "key-" + index).ToArray();
        _servers = Enumerable
            .Range(0, Size)
            .Select(index => new BenchKeyedServer
            {
                Id = "server-" + index,
                Name = "server",
                Count = index,
            })
            .ToArray();
        _scalarBase = Optional<BenchScalarDictHolder.Fragment?>.Present(
            BenchScalarDictHolder.Fragment.From(
                new()
                {
                    Scores = Enumerable
                        .Range(0, Size)
                        .ToDictionary(index => _keys[index], index => index),
                }
            )
        );
        _structuralBase = Optional<BenchStructuralDictHolder.Fragment?>.Present(
            BenchStructuralDictHolder.Fragment.From(
                new()
                {
                    Servers = Enumerable
                        .Range(0, Size)
                        .ToDictionary(index => _keys[index], index => _servers[index]),
                }
            )
        );
        var scalar = ScalarBuild();
        var structural = StructuralBuild();
        var cancel = Shape == DictionaryRemovalBuilderShape.RemoveSetRemove;
        var scalarResult = scalar.Apply(_scalarBase).Value!;
        var structuralResult = structural.Apply(_structuralBase).Value!;
        if (
            scalar.IsEmpty != cancel
            || structural.IsEmpty != cancel
            || scalarResult.Scores.Value!.Count != (cancel ? Size : 0)
            || structuralResult.Servers.Value!.Count != (cancel ? Size : 0)
            || _scalarBase.Value!.Scores.Value!.Count != Size
            || _structuralBase.Value!.Servers.Value!.Count != Size
            || Enumerable
                .Range(0, Size)
                .Any(index =>
                    _scalarBase.Value!.Scores.Value![_keys[index]] != index
                    || _servers[index].Count != index
                    || (
                        cancel
                        && (
                            scalarResult.Scores.Value![_keys[index]] != index
                            || structuralResult.Servers.Value![_keys[index]].Count != index
                        )
                    )
                )
        )
        {
            throw new InvalidOperationException(
                "Dictionary removal builders must deduplicate removals, retain set cancellation, and preserve sources."
            );
        }
    }

    [Benchmark]
    public BenchScalarDictHolder.Patch ScalarBuild()
    {
        var patch = new BenchScalarDictHolder.Patch();
        foreach (var key in _keys)
        {
            patch.Scores.RemoveEntry(key);
            if (Shape == DictionaryRemovalBuilderShape.Duplicate)
            {
                patch.Scores.RemoveEntry(key);
            }
        }
        if (Shape == DictionaryRemovalBuilderShape.RemoveSetRemove)
        {
            for (var index = 0; index < Size; index++)
            {
                patch.Scores.SetEntry(_keys[index], index);
            }
            foreach (var key in _keys)
            {
                patch.Scores.RemoveEntry(key);
            }
        }
        return patch;
    }

    [Benchmark]
    public BenchStructuralDictHolder.Patch StructuralBuild()
    {
        var patch = new BenchStructuralDictHolder.Patch();
        foreach (var key in _keys)
        {
            patch.Servers.RemoveEntry(key);
            if (Shape == DictionaryRemovalBuilderShape.Duplicate)
            {
                patch.Servers.RemoveEntry(key);
            }
        }
        if (Shape == DictionaryRemovalBuilderShape.RemoveSetRemove)
        {
            for (var index = 0; index < Size; index++)
            {
                patch.Servers.SetEntry(_keys[index], _servers[index]);
            }
            foreach (var key in _keys)
            {
                patch.Servers.RemoveEntry(key);
            }
        }
        return patch;
    }
}
