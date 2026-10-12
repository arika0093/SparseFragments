using System.Text.Json;

namespace SparseFragments.Tests;

public sealed class DictionaryProjectionOwnershipTests
{
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
