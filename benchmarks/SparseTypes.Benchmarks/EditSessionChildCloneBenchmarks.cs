using BenchmarkDotNet.Attributes;
using SparseTypes;

[SparseModel]
public partial class ScopeBenchServer
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}

[SparseModel]
public partial class ScopeBenchGroup
{
    [SparseKey]
    public string Name { get; set; } = string.Empty;

    public List<ScopeBenchServer> Servers { get; set; } = new();
}

[SparseModel]
public partial class ScopeBenchRoot
{
    public ScopeBenchGroup Target { get; set; } = new();

    public List<ScopeBenchServer> Siblings { get; set; } = new();
}

/// <summary>Child-session open cost matrix for opening a child session forks (snapshots plus deep-clones) the whole root.</summary>
[MemoryDiagnoser]
public class EditSessionChildCloneBenchmarks
{
    [Params(0, 1024, 8192)]
    public int SiblingCount { get; set; }

    [Params(1, 8)]
    public int ChildSessionCount { get; set; }

    private ScopeBenchRoot _prototype = null!;

    [GlobalSetup]
    public void Setup()
    {
        _prototype = new ScopeBenchRoot
        {
            Target = new ScopeBenchGroup
            {
                Name = "target",
                Servers = Enumerable
                    .Range(0, 4)
                    .Select(index => new ScopeBenchServer
                    {
                        Id = "target-" + index,
                        Name = "target-name-" + index,
                        Count = index,
                    })
                    .ToList(),
            },
            Siblings = Enumerable
                .Range(0, SiblingCount)
                .Select(index => new ScopeBenchServer
                {
                    Id = "sibling-" + index,
                    Name = "sibling-name-" + index,
                    Count = index,
                })
                .ToList(),
        };
        // Commits update the live model in place by design, so the shared
        // prototype must stay stationary while the checked-out copy advances.
        var check = _prototype.DeepClone();
        var session = check.CreateEditSession();
        using (var child = session.OpenChildSession(ScopeBenchRoot.SparsePath.Target))
        {
            if (child.Session.Model.Name != "target" || child.Session.Model.Servers.Count != 4)
                throw new InvalidOperationException(
                    "Child-session harness must resolve the small child subtree."
                );
            child.Session.Model.Name = "edited";
        }

        using (var child = session.OpenChildSession(ScopeBenchRoot.SparsePath.Target))
        {
            child.Session.Model.Name = "edited";
            if (!child.TryAcceptChanges(out var conflicts) || conflicts is not null)
                throw new InvalidOperationException("Child-session harness must accept cleanly.");
        }

        var observable =
            session.Observable
            ?? throw new InvalidOperationException(
                "Scope harness must expose an observable model."
            );
        if (
            observable.Target!.Name != "edited"
            || observable.Siblings.Count != SiblingCount
            || check.Target.Name != "edited"
            || _prototype.Target.Name != "target"
        )
            throw new InvalidOperationException(
                "Child-session harness must update the session while leaving the prototype stationary."
            );
    }

    [Benchmark]
    public int OpenSmallChildSession()
    {
        var session = _prototype.CreateEditSession();
        using var child = session.OpenChildSession(ScopeBenchRoot.SparsePath.Target);
        child.Session.Model.Servers[0].Count++;
        return child.Session.Model.Name.Length + child.Session.Model.Servers.Count;
    }

    [Benchmark]
    public int OpenSiblingChildSessionAndDiscard()
    {
        var session = _prototype.CreateEditSession();
        if (SiblingCount == 0)
        {
            using var target = session.OpenChildSession(ScopeBenchRoot.SparsePath.Target);
            return target.Session.Model.Name.Length;
        }

        var key = "sibling-" + (SiblingCount / 2);
        using var child = session.OpenChildSession(ScopeBenchRoot.SparsePath.Siblings.Key(key));
        return child.Session.Model.Name.Length + child.Session.Model.Count;
    }

    [Benchmark]
    public int OpenNestedChildSession()
    {
        var session = _prototype.CreateEditSession();
        using var group = session.OpenChildSession(ScopeBenchRoot.SparsePath.Target);
        using var server = group.Session.OpenChildSession(
            ScopeBenchGroup.SparsePath.Servers.Key("target-1")
        );
        server.Session.Model.Count++;
        return server.Session.Model.Name.Length + group.Session.Model.Servers.Count;
    }

    [Benchmark]
    public int OpenManyChildSessions()
    {
        var session = _prototype.CreateEditSession();
        var checksum = 0;
        for (var index = 0; index < ChildSessionCount; index++)
        {
            if (SiblingCount == 0)
            {
                using var target = session.OpenChildSession(ScopeBenchRoot.SparsePath.Target);
                checksum += target.Session.Model.Name.Length;
                continue;
            }

            var key = "sibling-" + ((index * 131) % SiblingCount);
            using var child = session.OpenChildSession(ScopeBenchRoot.SparsePath.Siblings.Key(key));
            checksum += child.Session.Model.Name.Length + child.Session.Model.Count;
        }

        return checksum;
    }
}
