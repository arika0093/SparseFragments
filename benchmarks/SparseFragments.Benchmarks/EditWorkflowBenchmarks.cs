using System.Text.Json;
using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum EditWorkflowShape
{
    Replay,
    Conflict,
}

[MemoryDiagnoser]
public class EditWorkflowBenchmarks
{
    [Params(64, 1024)]
    public int Size { get; set; }

    [Params(EditWorkflowShape.Replay, EditWorkflowShape.Conflict)]
    public EditWorkflowShape Shape { get; set; }

    private CertBenchKeyedHolder _prototype = null!;

    [GlobalSetup]
    public void Setup()
    {
        _prototype = new CertBenchKeyedHolder
        {
            Label = "base",
            Items = Enumerable
                .Range(0, Size)
                .Select(index => new CertBenchKeyedItem
                {
                    Id = "id-" + index,
                    Name = "name-" + index,
                    Count = index,
                })
                .ToList(),
        };
        for (var repeat = 0; repeat < 3; repeat++)
        {
            var result = ModelToApply();
            if (
                result.Label != "remote"
                || result.Items.Count != Size + 1
                || result.Items[^1].Id != "local-new"
                || result.Items[^1].Name != "added"
            )
                throw new InvalidOperationException(
                    "Workflow must retain remote state and local additions."
                );
            for (var index = 0; index < Size; index++)
            {
                var expected = index < 64 ? "edited-" + index : "name-" + index;
                if (index == 0 && Shape == EditWorkflowShape.Conflict)
                    expected = "remote-name";
                if (result.Items[index].Name != expected || result.Items[index].Count != index)
                    throw new InvalidOperationException(
                        "Workflow must apply edits and preserve conflict policy."
                    );
            }
            if (_prototype.Items[0].Name != "name-0" || _prototype.Items.Count != Size)
                throw new InvalidOperationException("Workflow input must remain stationary.");
        }
    }

    [Benchmark]
    public CertBenchKeyedHolder ModelToApply()
    {
        var local = _prototype.DeepClone();
        var session = local.CreateEditSession();
        session.BatchEdit(() =>
        {
            for (var index = 0; index < 64; index++)
                session.Observable.Items[index].Name = "edited-" + index;
            session.Observable.Items.AddModel(
                new CertBenchKeyedItem { Id = "local-new", Name = "added" }
            );
        });
        var changes = session.CreateChangeSet();
        var json = JsonSerializer.SerializeToUtf8Bytes(changes.ToPayload());
        var received = JsonSerializer
            .Deserialize<CertBenchKeyedHolder.ChangePayload>(json)!
            .ToChangeSet();
        var remote = _prototype.DeepClone();
        remote.Label = "remote";
        if (Shape == EditWorkflowShape.Conflict)
            remote.Items[0].Name = "remote-name";
        var rebased = received.RebaseOnto(
            Optional<CertBenchKeyedHolder.Fragment?>.Present(
                CertBenchKeyedHolder.Fragment.From(remote)
            )
        );
        if (rebased.HasConflicts != (Shape == EditWorkflowShape.Conflict))
            throw new InvalidOperationException("Unexpected workflow conflict result.");
        return rebased.Rebased.ToPatch().ApplyTo(remote);
    }
}
