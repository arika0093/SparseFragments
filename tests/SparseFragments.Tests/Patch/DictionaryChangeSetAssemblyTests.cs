using System.Text.Json;

namespace SparseFragments.Tests;

public sealed class DictionaryChangeSetAssemblyTests
{
    [Test]
    public void LargeMixedLookupsCanAlternateKindsAndPreserveItemIdentity()
    {
        var before = new ScalarDictHolder
        {
            Scores = Enumerable
                .Range(0, 2048)
                .ToDictionary(static index => "remove-" + index, static index => index),
        };
        var after = new ScalarDictHolder
        {
            Scores = Enumerable
                .Range(0, 2048)
                .ToDictionary(static index => "add-" + index, static index => index),
        };
        var changes = ScalarDictHolder.ChangeSet.Between(before, after);
        var items = changes.Scores.ToDictionary(static item => item.Key);
        foreach (var key in new[] { "remove-0", "add-0", "remove-2047", "add-2047", "remove-0" })
            changes.Scores.GetChange(key).ShouldBeSameAs(items[key]);
        changes.Scores.GetChange("missing").IsEmpty.ShouldBeTrue();
        Should.Throw<ArgumentNullException>(() => changes.Scores.GetChange(null!));
    }

    [Test]
    public void LargeMixedLookupsKeepEditsAlongsideMembershipChanges()
    {
        var before = new ScalarDictHolder
        {
            Scores = Enumerable
                .Range(0, 2048)
                .ToDictionary(static index => "remove-" + index, static index => index),
        };
        before.Scores["edit"] = 1;
        var after = new ScalarDictHolder
        {
            Scores = Enumerable
                .Range(0, 2048)
                .ToDictionary(static index => "add-" + index, static index => index),
        };
        after.Scores["edit"] = 2;
        var changes = ScalarDictHolder.ChangeSet.Between(before, after);
        changes.Scores.GetChange("missing").IsEmpty.ShouldBeTrue();
        changes.Scores.GetChange("edit").Before.Value.ShouldBe(1);
        changes.Scores.GetChange("remove-0").IsRemoved.ShouldBeTrue();
        changes.Scores.GetChange("add-0").IsAdded.ShouldBeTrue();
        changes.Scores.GetChange("edit").After.Value.ShouldBe(2);
        var replayed = changes.ToPatch().ApplyTo(before);
        replayed.Scores.Count.ShouldBe(after.Scores.Count);
        foreach (var pair in after.Scores)
            replayed.Scores[pair.Key].ShouldBe(pair.Value);
    }

    [Test]
    public void MixedScalarChangesPreserveGroupedOrderAndReplay()
    {
        var before = Optional<ScalarDictHolder.Fragment?>.Present(
            ScalarDictHolder.Fragment.From(
                new()
                {
                    Scores = new()
                    {
                        ["remove1"] = 10,
                        ["edit"] = 1,
                        ["keep"] = 0,
                        ["remove2"] = 20,
                    },
                }
            )
        );
        var after = Optional<ScalarDictHolder.Fragment?>.Present(
            ScalarDictHolder.Fragment.From(
                new()
                {
                    Scores = new()
                    {
                        ["add1"] = 30,
                        ["edit"] = 2,
                        ["keep"] = 0,
                        ["add2"] = 40,
                    },
                }
            )
        );
        var changes = ScalarDictHolder.ChangeSet.Between(before, after);
        changes
            .Scores.Select(static item => item.Key)
            .ShouldBe(["add1", "add2", "remove1", "remove2", "edit"]);
        changes.Scores.GetChange("edit").Before.Value.ShouldBe(1);
        changes.Scores.GetChange("edit").After.Value.ShouldBe(2);
        changes.Scores.GetChange("keep").IsEmpty.ShouldBeTrue();

        var restored = JsonSerializer
            .Deserialize<ScalarDictHolder.ChangePayload>(
                JsonSerializer.Serialize(changes.ToPayload())
            )!
            .ToChangeSet();
        restored
            .Scores.Select(static item => item.Key)
            .ShouldBe(["add1", "add2", "remove1", "remove2", "edit"]);
        ScalarDictHolder
            .Patch.Between(restored.ToPatch().Apply(before), after)
            .IsEmpty.ShouldBeTrue();
        ScalarDictHolder
            .Patch.Between(restored.Invert().ToPatch().Apply(after), before)
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void MixedStructuralChangesPreserveGroupedOrderAndNestedEndpoints()
    {
        var before = Optional<StructuralDictHolder.Fragment?>.Present(
            StructuralDictHolder.Fragment.From(
                new()
                {
                    Servers = new()
                    {
                        ["remove1"] = new() { Id = "remove1", Name = "removed1" },
                        ["edit"] = new() { Id = "edit", Name = "before" },
                        ["keep"] = new() { Id = "keep", Name = "unchanged" },
                        ["remove2"] = new() { Id = "remove2", Name = "removed2" },
                    },
                }
            )
        );
        var after = Optional<StructuralDictHolder.Fragment?>.Present(
            StructuralDictHolder.Fragment.From(
                new()
                {
                    Servers = new()
                    {
                        ["add1"] = new() { Id = "add1", Name = "added1" },
                        ["edit"] = new() { Id = "edit", Name = "after" },
                        ["keep"] = new() { Id = "keep", Name = "unchanged" },
                        ["add2"] = new() { Id = "add2", Name = "added2" },
                    },
                }
            )
        );
        var changes = StructuralDictHolder.ChangeSet.Between(before, after);
        changes
            .Servers.Select(static item => item.Key)
            .ShouldBe(["add1", "add2", "remove1", "remove2", "edit"]);
        changes.Servers.GetChange("add1").Edit.Name.After.Value.ShouldBe("added1");
        changes.Servers.GetChange("remove1").Edit.Name.Before.Value.ShouldBe("removed1");
        changes.Servers.GetChange("edit").Edit.Name.Before.Value.ShouldBe("before");
        changes.Servers.GetChange("edit").Edit.Name.After.Value.ShouldBe("after");
        changes.Servers.GetChange("keep").IsEmpty.ShouldBeTrue();

        var restored = JsonSerializer
            .Deserialize<StructuralDictHolder.ChangePayload>(
                JsonSerializer.Serialize(changes.ToPayload())
            )!
            .ToChangeSet();
        restored
            .Servers.Select(static item => item.Key)
            .ShouldBe(["add1", "add2", "remove1", "remove2", "edit"]);
        StructuralDictHolder
            .Patch.Between(restored.ToPatch().Apply(before), after)
            .IsEmpty.ShouldBeTrue();
        StructuralDictHolder
            .Patch.Between(restored.Invert().ToPatch().Apply(after), before)
            .IsEmpty.ShouldBeTrue();
    }
}
