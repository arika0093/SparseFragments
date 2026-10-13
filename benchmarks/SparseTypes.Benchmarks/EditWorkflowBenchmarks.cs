using System.Text.Json;
using BenchmarkDotNet.Attributes;
using SparseTypes;

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
    private CertBenchKeyedHolder.ChangeSet _changes = null!;
    private CertBenchKeyedHolder.ChangeSet _received = null!;
    private CertBenchKeyedHolder _remote = null!;

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
        _changes = CreateLocalChangeSet();
        _received = Transport(_changes);
        _remote = CreateRemote();
        for (var repeat = 0; repeat < 3; repeat++)
        {
            Validate(ModelToApply());
            Validate(RebaseAndApply());
        }
    }

    private void Validate(CertBenchKeyedHolder result)
    {
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
        var remoteName = Shape == EditWorkflowShape.Conflict ? "remote-name" : "name-0";
        if (_remote.Items.Count != Size || _remote.Items[0].Name != remoteName)
            throw new InvalidOperationException("Rebase and apply must retain the remote input.");
    }

    [Benchmark]
    public CertBenchKeyedHolder ModelToApply()
    {
        var received = Transport(CreateLocalChangeSet());
        return RebaseAndApply(received, CreateRemote());
    }

    [Benchmark]
    public CertBenchKeyedHolder.ChangeSet CreateLocalChangeSet()
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
        return session.CreateChangeSet();
    }

    [Benchmark]
    public CertBenchKeyedHolder.ChangeSet Transport() => Transport(_changes);

    private static CertBenchKeyedHolder.ChangeSet Transport(CertBenchKeyedHolder.ChangeSet changes)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(changes.ToPayload());
        return JsonSerializer.Deserialize<CertBenchKeyedHolder.ChangePayload>(json)!.ToChangeSet();
    }

    private CertBenchKeyedHolder CreateRemote()
    {
        var remote = _prototype.DeepClone();
        remote.Label = "remote";
        if (Shape == EditWorkflowShape.Conflict)
            remote.Items[0].Name = "remote-name";
        return remote;
    }

    [Benchmark]
    public CertBenchKeyedHolder RebaseAndApply() => RebaseAndApply(_received, _remote);

    private CertBenchKeyedHolder RebaseAndApply(
        CertBenchKeyedHolder.ChangeSet received,
        CertBenchKeyedHolder remote
    )
    {
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
