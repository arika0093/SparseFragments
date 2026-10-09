using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum KeyedRemovalBuilderShape
{
    Unique,
    Duplicate,
    RemoveAddRemove,
    Mixed,
    AddOnly,
    EditOnly,
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
            BenchKeyedServerHolder.Fragment.From(
                new() { Items = Shape == KeyedRemovalBuilderShape.AddOnly ? [] : [.. _servers] }
            )
        );
        var valueBase = Optional<BenchValueKeyedHolder.Fragment?>.Present(
            BenchValueKeyedHolder.Fragment.From(
                new() { Items = Shape == KeyedRemovalBuilderShape.AddOnly ? [] : [.. _values] }
            )
        );
        var referencePatch = ReferenceBuild();
        var valuePatch = ValueBuild();
        var referenceResult = referencePatch.Apply(referenceBase).Value!.Items.Value!;
        var valueResult = valuePatch.Apply(valueBase).Value!.Items.Value!;
        var cancel = Shape == KeyedRemovalBuilderShape.RemoveAddRemove;
        var mixed = Shape == KeyedRemovalBuilderShape.Mixed;
        var added = Shape == KeyedRemovalBuilderShape.AddOnly;
        var edited = mixed || Shape == KeyedRemovalBuilderShape.EditOnly;
        var offset = mixed ? Size / 2 : 0;
        var expected =
            cancel || added || Shape == KeyedRemovalBuilderShape.EditOnly ? Size
            : mixed ? Size - offset
            : 0;
        if (
            referencePatch.IsEmpty != cancel
            || valuePatch.IsEmpty != cancel
            || referenceResult.Count != expected
            || valueResult.Count != expected
            || referenceBase.Value!.Items.Value!.Count != (added ? 0 : Size)
            || valueBase.Value!.Items.Value!.Count != (added ? 0 : Size)
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
                || (!added && referenceBase.Value!.Items.Value![index].Count != index)
                || (!added && valueBase.Value!.Items.Value![index].Count != index)
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
            var count = source + (edited ? 1 : 0);
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
        ValidateRemovalReinsertion();
    }

    private static void ValidateRemovalReinsertion()
    {
        var reference = new BenchKeyedServerHolder.Patch();
        var value = new BenchValueKeyedHolder.Patch();
        var keys = new List<string>();
        for (var candidate = 0; keys.Count < 64; candidate++)
        {
            var key = "probe-" + candidate;
            if ((EqualityComparer<string>.Default.GetHashCode(key) & 127) == 0)
            {
                keys.Add(key);
            }
        }
        var servers = keys.Select((key, index) => new BenchKeyedServer { Id = key, Count = index })
            .ToList();
        var values = Enumerable
            .Range(0, 64)
            .Select(index => new BenchValueKeyedItem { Id = index * 128, Count = index })
            .ToList();
        for (var index = 0; index < 64; index++)
        {
            reference.Items.Remove(keys[index]);
            value.Items.Remove(values[index].Id);
        }
        foreach (var key in keys)
        {
            RequireRemovedRejection(() => reference.Items.Edit(key));
        }
        foreach (var item in values)
        {
            RequireRemovedRejection(() => value.Items.Edit(item.Id));
        }
        reference.Items.Add(servers[0]);
        value.Items.Add(values[0]);
        RequireRemovedRejection(() => reference.Items.Edit(keys[1]));
        RequireRemovedRejection(() => value.Items.Edit(values[1].Id));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            reference.Items.Remove(keys[0]);
            value.Items.Remove(values[0].Id);
        }
        RequireRemovedRejection(() => reference.Items.Edit(keys[0]));
        RequireRemovedRejection(() => value.Items.Edit(values[0].Id));
        var referenceBase = Optional<BenchKeyedServerHolder.Fragment?>.Present(
            BenchKeyedServerHolder.Fragment.From(new() { Items = servers })
        );
        var valueBase = Optional<BenchValueKeyedHolder.Fragment?>.Present(
            BenchValueKeyedHolder.Fragment.From(new() { Items = values })
        );
        if (
            reference.Apply(referenceBase).Value!.Items.Value!.Count != 0
            || value.Apply(valueBase).Value!.Items.Value!.Count != 0
            || servers.Count != 64
            || values.Count != 64
        )
        {
            throw new InvalidOperationException(
                "Colliding keyed removals must support cancellation, reinsertion and deduplication."
            );
        }
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
        var removed = RemovalCount;
        for (var index = 0; index < removed; index++)
        {
            patch.Items.Remove(_servers[index].Id);
            if (Shape == KeyedRemovalBuilderShape.Duplicate)
            {
                patch.Items.Remove(_servers[index].Id);
            }
        }
        if (Shape is KeyedRemovalBuilderShape.RemoveAddRemove or KeyedRemovalBuilderShape.AddOnly)
        {
            foreach (var server in _servers)
            {
                patch.Items.Add(server);
            }
        }
        if (Shape == KeyedRemovalBuilderShape.RemoveAddRemove)
        {
            foreach (var server in _servers)
            {
                patch.Items.Remove(server.Id);
            }
        }
        if (Shape is KeyedRemovalBuilderShape.Mixed or KeyedRemovalBuilderShape.EditOnly)
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
        var removed = RemovalCount;
        for (var index = 0; index < removed; index++)
        {
            patch.Items.Remove(_values[index].Id);
            if (Shape == KeyedRemovalBuilderShape.Duplicate)
            {
                patch.Items.Remove(_values[index].Id);
            }
        }
        if (Shape is KeyedRemovalBuilderShape.RemoveAddRemove or KeyedRemovalBuilderShape.AddOnly)
        {
            foreach (var value in _values)
            {
                patch.Items.Add(value);
            }
        }
        if (Shape == KeyedRemovalBuilderShape.RemoveAddRemove)
        {
            foreach (var value in _values)
            {
                patch.Items.Remove(value.Id);
            }
        }
        if (Shape is KeyedRemovalBuilderShape.Mixed or KeyedRemovalBuilderShape.EditOnly)
        {
            for (var index = removed; index < Size; index++)
            {
                patch.Items.Edit(_values[index].Id).Count = index + 1;
            }
        }
        return patch;
    }

    private int RemovalCount =>
        Shape switch
        {
            KeyedRemovalBuilderShape.Mixed => Size / 2,
            KeyedRemovalBuilderShape.AddOnly or KeyedRemovalBuilderShape.EditOnly => 0,
            _ => Size,
        };
}
