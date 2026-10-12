using System.Text.Json;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class ProjectionCacheCollisionHolder
{
    public string __sparse_projection_cache_1 { get; set; } = "";
    public List<KeyedServer> Items { get; set; } = [];
}

public sealed class KeyedProjectionOwnershipTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void WholeKeyedViewsReturnIndependentContainerSnapshots(bool wholeRoot)
    {
        var before = wholeRoot
            ? Optional<KeyedServerHolder.Fragment?>.Missing
            : Optional<KeyedServerHolder.Fragment?>.Present(new());
        var after = Optional<KeyedServerHolder.Fragment?>.Present(
            KeyedServerHolder.Fragment.From(new() { Items = [new() { Id = "server" }] })
        );
        var changes = KeyedServerHolder.ChangeSet.Between(before, after);
        var payload = JsonSerializer.Serialize(changes.ToPayload());

        changes.Items.After.Value!.Clear();

        changes.Items.After.Value!.Count.ShouldBe(1);
        changes.ToPatch().Apply(before).Value!.Items.Value!.Count.ShouldBe(1);
        JsonSerializer.Serialize(changes.ToPayload()).ShouldBe(payload);
    }

    [Test]
    public void SparseKeyedViewsExposeReadOnlyContainers()
    {
        var changes = KeyedServerHolder.ChangeSet.Between(
            new KeyedServerHolder { Items = [new() { Id = "server", Name = "before" }] },
            new KeyedServerHolder { Items = [new() { Id = "server", Name = "after" }] }
        );
        var payload = JsonSerializer.Serialize(changes.ToPayload());
        Should.Throw<NotSupportedException>(() =>
            ((IDictionary<string, KeyedServer.ChangeSet>)changes.Items.Edited).Clear()
        );
        Should.Throw<NotSupportedException>(() =>
            ((IList<string>)changes.Items.BeforeOrder).Clear()
        );
        changes.Items.GetChange("server").Edit.Name.After.Value.ShouldBe("after");
        changes.Items.Edited.Count.ShouldBe(1);
        JsonSerializer.Serialize(changes.ToPayload()).ShouldBe(payload);
    }

    [Test]
    public void ProjectionFieldsDoNotCollideWithModelMembers()
    {
        var changes = ProjectionCacheCollisionHolder.ChangeSet.Between(
            new ProjectionCacheCollisionHolder
            {
                __sparse_projection_cache_1 = "before",
                Items = [new() { Id = "server", Name = "before" }],
            },
            new ProjectionCacheCollisionHolder
            {
                __sparse_projection_cache_1 = "after",
                Items = [new() { Id = "server", Name = "after" }],
            }
        );
        changes.__sparse_projection_cache_1.After.Value.ShouldBe("after");
        changes.Items.GetChange("server").Edit.Name.After.Value.ShouldBe("after");
    }
}
