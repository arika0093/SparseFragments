using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class ScopeBenchServer
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}

[SparseFragmentModel]
public partial class ScopeBenchGroup
{
    [SparseKey]
    public string Name { get; set; } = string.Empty;

    public List<ScopeBenchServer> Servers { get; set; } = new();
}

[SparseFragmentModel]
public partial class ScopeBenchRoot
{
    public ScopeBenchGroup Target { get; set; } = new();

    public List<ScopeBenchServer> Siblings { get; set; } = new();
}

/// <summary>
/// Edit-scope open cost matrix for issue #239: opening a child draft forks
/// (snapshots plus deep-clones) the whole root, so the cost scales with the
/// root graph rather than the edited subtree. The benchmarks vary root size
/// independently of child size, plus simultaneous-scope count and nesting.
/// Benchmarks return checksums so the measured call cannot be eliminated;
/// allocations are recorded by <c>MemoryDiagnoser</c>. Machine-dependent
/// timings are reported in docs/benchmarks/edit-scope-clone.md, never
/// asserted.
/// </summary>
[MemoryDiagnoser]
public class EditScopeCloneBenchmarks
{
    [Params(0, 1024, 8192)]
    public int SiblingCount { get; set; }

    [Params(1, 8)]
    public int ScopeCount { get; set; }

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
        // Validate the harness on a clone: commits update the live model
        // in place by design, so the shared prototype must stay stationary
        // while the checked-out copy advances.
        var check = _prototype.DeepClone();
        var session = check.CreateEditSession();
        using (var scope = session.OpenScope(ScopeBenchRoot.SparsePath.Target))
        {
            if (scope.Model.Name != "target" || scope.Model.Servers.Count != 4)
                throw new InvalidOperationException(
                    "Scope harness must resolve the small child subtree."
                );
            scope.Model.Name = "edited";
        }

        using (var scope = session.OpenScope(ScopeBenchRoot.SparsePath.Target))
        {
            scope.Model.Name = "edited";
            if (!scope.TryCommit(out var conflicts) || conflicts is not null)
                throw new InvalidOperationException("Scope harness must commit cleanly.");
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
                "Scope harness must commit into the session while leaving the prototype stationary."
            );
    }

    [Benchmark]
    public int OpenSmallChildScope()
    {
        var session = _prototype.CreateEditSession();
        using var scope = session.OpenScope(ScopeBenchRoot.SparsePath.Target);
        scope.Model.Servers[0].Count++;
        return scope.Model.Name.Length + scope.Model.Servers.Count;
    }

    [Benchmark]
    public int OpenSiblingScopeAndCancel()
    {
        var session = _prototype.CreateEditSession();
        if (SiblingCount == 0)
        {
            using var target = session.OpenScope(ScopeBenchRoot.SparsePath.Target);
            return target.Model.Name.Length;
        }

        var key = "sibling-" + (SiblingCount / 2);
        using var scope = session.OpenScope(ScopeBenchRoot.SparsePath.Siblings.Key(key));
        return scope.Model.Name.Length + scope.Model.Count;
    }

    [Benchmark]
    public int OpenNestedScope()
    {
        var session = _prototype.CreateEditSession();
        using var group = session.OpenScope(ScopeBenchRoot.SparsePath.Target);
        using var server = group.OpenScope(ScopeBenchGroup.SparsePath.Servers.Key("target-1"));
        server.Model.Count++;
        return server.Model.Name.Length + group.Model.Servers.Count;
    }

    [Benchmark]
    public int OpenManyScopes()
    {
        var session = _prototype.CreateEditSession();
        var checksum = 0;
        for (var index = 0; index < ScopeCount; index++)
        {
            if (SiblingCount == 0)
            {
                using var target = session.OpenScope(ScopeBenchRoot.SparsePath.Target);
                checksum += target.Model.Name.Length;
                continue;
            }

            var key = "sibling-" + ((index * 131) % SiblingCount);
            using var scope = session.OpenScope(ScopeBenchRoot.SparsePath.Siblings.Key(key));
            checksum += scope.Model.Name.Length + scope.Model.Count;
        }

        return checksum;
    }
}
