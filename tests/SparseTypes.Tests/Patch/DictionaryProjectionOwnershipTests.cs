using System.Text.Json;

namespace SparseTypes.Tests;

[SparseModel]
public partial class DictionaryProjectionCacheCollisionHolder
{
    public int __sparse_projection_cache_1 { get; set; }
    public Dictionary<string, int> Scores { get; set; } = new();
}

public sealed class DictionaryProjectionOwnershipTests
{
    [Test]
    public void ProjectionFieldsDoNotCollideWithDictionaryModelMembers()
    {
        var changes = DictionaryProjectionCacheCollisionHolder.ChangeSet.Between(
            new DictionaryProjectionCacheCollisionHolder
            {
                __sparse_projection_cache_1 = 1,
                Scores = new() { ["score"] = 1 },
            },
            new DictionaryProjectionCacheCollisionHolder
            {
                __sparse_projection_cache_1 = 2,
                Scores = new() { ["score"] = 2 },
            }
        );
        changes.__sparse_projection_cache_1.After.Value.ShouldBe(2);
        changes.Scores.GetChange("score").After.Value.ShouldBe(2);
        changes.Scores.Edited["score"].ShouldBe(2);
    }

    [Test]
    public void SparseStructuralDictionaryViewsExposeReadOnlyContainers()
    {
        var changes = StructuralDictHolder.ChangeSet.Between(
            new StructuralDictHolder
            {
                Servers = new()
                {
                    ["server"] = new() { Id = "server", Name = "before" },
                },
            },
            new StructuralDictHolder
            {
                Servers = new()
                {
                    ["server"] = new() { Id = "server", Name = "after" },
                },
            }
        );
        var payload = JsonSerializer.Serialize(changes.ToPayload());
        Should.Throw<NotSupportedException>(() =>
            ((IDictionary<string, KeyedServer.ChangeSet>)changes.Servers.Edited).Clear()
        );
        changes.Servers.GetChange("server").Edit.Name.After.Value.ShouldBe("after");
        changes.Servers.Edited["server"].Name.After.Value.ShouldBe("after");
        JsonSerializer.Serialize(changes.ToPayload()).ShouldBe(payload);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void WholeDictionaryViewsReturnIndependentContainerSnapshots(bool wholeRoot)
    {
        var before = wholeRoot
            ? Optional<ScalarDictHolder.Fragment?>.Missing
            : Optional<ScalarDictHolder.Fragment?>.Present(new());
        var after = Optional<ScalarDictHolder.Fragment?>.Present(
            ScalarDictHolder.Fragment.From(new() { Scores = new() { ["score"] = 1 } })
        );
        var changes = ScalarDictHolder.ChangeSet.Between(before, after);
        var payload = JsonSerializer.Serialize(changes.ToPayload());

        changes.Scores.After.Value!.Clear();

        changes.Scores.After.Value!.Count.ShouldBe(1);
        changes.ToPatch().Apply(before).Value!.Scores.Value!.Count.ShouldBe(1);
        JsonSerializer.Serialize(changes.ToPayload()).ShouldBe(payload);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void WholeStructuralDictionaryViewsReturnIndependentContainerSnapshots(bool wholeRoot)
    {
        var before = wholeRoot
            ? Optional<StructuralDictHolder.Fragment?>.Missing
            : Optional<StructuralDictHolder.Fragment?>.Present(new());
        var after = Optional<StructuralDictHolder.Fragment?>.Present(
            StructuralDictHolder.Fragment.From(
                new() { Servers = new() { ["server"] = new() { Id = "server" } } }
            )
        );
        var changes = StructuralDictHolder.ChangeSet.Between(before, after);
        var payload = JsonSerializer.Serialize(changes.ToPayload());

        changes.Servers.After.Value!.Clear();

        changes.Servers.After.Value!.Count.ShouldBe(1);
        changes.ToPatch().Apply(before).Value!.Servers.Value!.Count.ShouldBe(1);
        JsonSerializer.Serialize(changes.ToPayload()).ShouldBe(payload);
    }

    [Test]
    public void SparseDictionaryViewsExposeReadOnlyContainers()
    {
        var changes = ScalarDictHolder.ChangeSet.Between(
            new ScalarDictHolder { Scores = new() { ["score"] = 1 } },
            new ScalarDictHolder
            {
                Scores = new() { ["score"] = 2, ["added"] = 3 },
            }
        );
        var payload = JsonSerializer.Serialize(changes.ToPayload());
        Should.Throw<NotSupportedException>(() =>
            ((IDictionary<string, int>)changes.Scores.Edited).Clear()
        );
        Should.Throw<NotSupportedException>(() =>
            ((IDictionary<string, int>)changes.Scores.Added).Clear()
        );
        changes.Scores.GetChange("score").After.Value.ShouldBe(2);
        changes.Scores.GetChange("added").After.Value.ShouldBe(3);
        JsonSerializer.Serialize(changes.ToPayload()).ShouldBe(payload);
    }
}
