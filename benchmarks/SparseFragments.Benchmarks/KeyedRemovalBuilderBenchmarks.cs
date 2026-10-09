using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum KeyedRemovalBuilderShape
{
    Unique,
    Duplicate,
    RemoveAddRemove,
    Mixed,
}

[MemoryDiagnoser]
public class KeyedRemovalBuilderBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [ParamsAllValues]
    public KeyedRemovalBuilderShape Shape { get; set; }

    private BenchKeyedServer[] _servers = [];
    private BenchValueKeyedItem[] _values = [];

    [GlobalSetup]
    public void Setup()
    {
        _servers = Enumerable
            .Range(0, Size)
            .Select(index => new BenchKeyedServer
            {
                Id = "key-" + index,
                Name = "server",
                Count = index,
            })
            .ToArray();
        _values = Enumerable
            .Range(0, Size)
            .Select(index => new BenchValueKeyedItem { Id = index, Count = index })
            .ToArray();
        var referenceBase = Optional<BenchKeyedServerHolder.Fragment?>.Present(
            BenchKeyedServerHolder.Fragment.From(new() { Items = [.. _servers] })
        );
        var valueBase = Optional<BenchValueKeyedHolder.Fragment?>.Present(
            BenchValueKeyedHolder.Fragment.From(new() { Items = [.. _values] })
        );
        var referencePatch = ReferenceBuild();
        var valuePatch = ValueBuild();
        var referenceResult = referencePatch.Apply(referenceBase).Value!.Items.Value!;
        var valueResult = valuePatch.Apply(valueBase).Value!.Items.Value!;
        var cancel = Shape == KeyedRemovalBuilderShape.RemoveAddRemove;
        var mixed = Shape == KeyedRemovalBuilderShape.Mixed;
        var offset = mixed ? Size / 2 : 0;
        var expected =
            cancel ? Size
            : mixed ? Size - offset
            : 0;
        if (
            referencePatch.IsEmpty != cancel
            || valuePatch.IsEmpty != cancel
            || referenceResult.Count != expected
            || valueResult.Count != expected
            || referenceBase.Value!.Items.Value!.Count != Size
            || valueBase.Value!.Items.Value!.Count != Size
        )
        {
            throw new InvalidOperationException(
                "Keyed removal builders must retain cancellation and source collections."
            );
        }
        for (var index = 0; index < Size; index++)
        {
            if (
                _servers[index].Count != index
                || referenceBase.Value!.Items.Value![index].Count != index
                || valueBase.Value!.Items.Value![index].Count != index
            )
            {
                throw new InvalidOperationException(
                    "Building and applying keyed removals must preserve source values."
                );
            }
        }
        for (var index = 0; index < expected; index++)
        {
            var source = index + offset;
            var count = source + (mixed ? 1 : 0);
            if (
                referenceResult[index].Id != _servers[source].Id
                || valueResult[index].Id != source
                || referenceResult[index].Count != count
                || valueResult[index].Count != count
            )
            {
                throw new InvalidOperationException(
                    "Keyed removal builders must preserve order and retained edits."
                );
            }
        }
        var removedReference = new BenchKeyedServerHolder.Patch();
        var removedValue = new BenchValueKeyedHolder.Patch();
        foreach (var server in _servers)
        {
            removedReference.Items.Remove(server.Id);
        }
        foreach (var value in _values)
        {
            removedValue.Items.Remove(value.Id);
        }
        RequireRemovedRejection(() => removedReference.Items.Edit(_servers[0].Id));
        RequireRemovedRejection(() => removedValue.Items.Edit(_values[0].Id));
    }

    private static void RequireRemovedRejection(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException("A removed keyed item must reject edits.");
    }

    [Benchmark]
    public BenchKeyedServerHolder.Patch ReferenceBuild()
    {
        var patch = new BenchKeyedServerHolder.Patch();
        var removed = Shape == KeyedRemovalBuilderShape.Mixed ? Size / 2 : Size;
        for (var index = 0; index < removed; index++)
        {
            patch.Items.Remove(_servers[index].Id);
            if (Shape == KeyedRemovalBuilderShape.Duplicate)
            {
                patch.Items.Remove(_servers[index].Id);
            }
        }
        if (Shape == KeyedRemovalBuilderShape.RemoveAddRemove)
        {
            foreach (var server in _servers)
            {
                patch.Items.Add(server);
            }
            foreach (var server in _servers)
            {
                patch.Items.Remove(server.Id);
            }
        }
        if (Shape == KeyedRemovalBuilderShape.Mixed)
        {
            for (var index = removed; index < Size; index++)
            {
                patch.Items.Edit(_servers[index].Id).Count = index + 1;
            }
        }
        return patch;
    }

    [Benchmark]
    public BenchValueKeyedHolder.Patch ValueBuild()
    {
        var patch = new BenchValueKeyedHolder.Patch();
        var removed = Shape == KeyedRemovalBuilderShape.Mixed ? Size / 2 : Size;
        for (var index = 0; index < removed; index++)
        {
            patch.Items.Remove(_values[index].Id);
            if (Shape == KeyedRemovalBuilderShape.Duplicate)
            {
                patch.Items.Remove(_values[index].Id);
            }
        }
        if (Shape == KeyedRemovalBuilderShape.RemoveAddRemove)
        {
            foreach (var value in _values)
            {
                patch.Items.Add(value);
            }
            foreach (var value in _values)
            {
                patch.Items.Remove(value.Id);
            }
        }
        if (Shape == KeyedRemovalBuilderShape.Mixed)
        {
            for (var index = removed; index < Size; index++)
            {
                patch.Items.Edit(_values[index].Id).Count = index + 1;
            }
        }
        return patch;
    }
}
