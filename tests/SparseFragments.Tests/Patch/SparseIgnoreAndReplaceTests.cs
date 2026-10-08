using System.Text.Json;
using SparseFragments;

namespace SparseFragments.Tests;

public class SparseIgnoreBase
{
    [SparseIgnore]
    public string Secret { get; set; } = "base-secret";
}

[SparseFragmentModel]
public partial class SparseIgnoreModel : SparseIgnoreBase
{
    public int Value { get; set; }
}

public partial class SparseIgnoreChild
{
    [SparseIgnore]
    public string Token { get; set; } = "child-token";

    public int Count { get; set; }
}

[SparseFragmentModel]
public partial class SparseIgnoreChildHolder
{
    public SparseIgnoreChild Child { get; set; } = new();
}

public partial class ReplaceSequenceItem
{
    public string Name { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class ReplaceSequenceHolder
{
    [SparseMerge(MergeMode.Replace)]
    public List<ReplaceSequenceItem> Items { get; set; } = [];
}

[SparseFragmentModel]
public partial class ReplaceKeyedSequenceHolder
{
    [SparseMerge(MergeMode.Replace)]
    public List<KeyedServer> Items { get; set; } = [];
}

[SparseFragmentModel]
public partial class ReplaceDictionaryHolder
{
    [SparseMerge(MergeMode.Replace)]
    public Dictionary<string, KeyedServer> Items { get; set; } = [];
}

public sealed class SparseIgnoreAndReplaceTests
{
    [Test]
    public void SparseIgnoreRemovesPropertyFromGeneratedSurfacesAndPreservesApplyToValue()
    {
        var current = new SparseIgnoreModel { Secret = "current-secret", Value = 1 };
        var fragment = SparseIgnoreModel.Fragment.From(current);

        typeof(SparseIgnoreModel.Fragment).GetProperty(nameof(SparseIgnoreBase.Secret)).ShouldBeNull();
        typeof(SparseIgnoreModel.ChangeSet).GetProperty(nameof(SparseIgnoreBase.Secret)).ShouldBeNull();
        typeof(SparseIgnoreModel.Observable).GetProperty(nameof(SparseIgnoreBase.Secret)).ShouldBeNull();
        fragment.ToModel().Secret.ShouldBe("base-secret");

        var patch = SparseIgnoreModel.Patch.Between(
            SparseIgnoreModel.Fragment.From(new SparseIgnoreModel { Secret = "old", Value = 1 }),
            SparseIgnoreModel.Fragment.From(new SparseIgnoreModel { Secret = "new", Value = 2 })
        );
        patch.ApplyTo(current).Secret.ShouldBe("current-secret");
        patch.ApplyTo(current).Value.ShouldBe(2);

        var change = SparseIgnoreModel.ChangeSet.Between(
            SparseIgnoreModel.Fragment.From(new SparseIgnoreModel { Secret = "old", Value = 1 }),
            SparseIgnoreModel.Fragment.From(new SparseIgnoreModel { Secret = "new", Value = 3 })
        );
        change.TryApplyTo(current, out var updated).ShouldBeTrue();
        updated!.Secret.ShouldBe("current-secret");
        updated.Value.ShouldBe(3);

        JsonSerializer.Serialize(fragment).ShouldNotContain("Secret");
    }

    [Test]
    public void SparseIgnoreIsAppliedToPromotedModelsAndTheirApplyTo()
    {
        var current = new SparseIgnoreChild { Token = "current-token", Count = 1 };
        typeof(SparseIgnoreChild.Fragment).GetProperty(nameof(SparseIgnoreChild.Token)).ShouldBeNull();
        SparseIgnoreChild.Fragment.From(current).ToModel().Token.ShouldBe("child-token");

        var patch = SparseIgnoreChild.Patch.Between(
            SparseIgnoreChild.Fragment.From(new SparseIgnoreChild { Token = "old", Count = 1 }),
            SparseIgnoreChild.Fragment.From(new SparseIgnoreChild { Token = "desired", Count = 2 })
        );
        patch.ApplyTo(current).Token.ShouldBe("current-token");
    }

    [Test]
    public void ExplicitReplaceUsesWholeCollectionSemanticsForUnkeyedAndKeyedCollections()
    {
        var before = new ReplaceSequenceHolder { Items = [new() { Name = "before" }] };
        var after = new ReplaceSequenceHolder
        {
            Items = [new() { Name = "after" }, new() { Name = "second" }],
        };
        var replace = ReplaceSequenceHolder.Patch.Between(
            ReplaceSequenceHolder.Fragment.From(before),
            ReplaceSequenceHolder.Fragment.From(after)
        );
        replace.ApplyTo(before).Items.Select(item => item.Name).ShouldBe(["after", "second"]);
        typeof(ReplaceSequenceHolder.Patch).GetProperty("ItemsPatch").ShouldBeNull();
        var sequenceChanges = ReplaceSequenceHolder.ChangeSet.Between(
            ReplaceSequenceHolder.Fragment.From(before),
            ReplaceSequenceHolder.Fragment.From(after)
        );
        var sequenceJson = JsonSerializer.Serialize(sequenceChanges.ToPayload());
        var sequenceRoundTrip = JsonSerializer
            .Deserialize<ReplaceSequenceHolder.ChangeSetPayload>(sequenceJson)!
            .ToChangeSet();
        sequenceRoundTrip
            .ToPatch()
            .ApplyTo(before)
            .Items.Select(item => item.Name)
            .ShouldBe(["after", "second"]);

        var keyedBefore = new ReplaceKeyedSequenceHolder
        {
            Items = [new KeyedServer { Id = "a", Name = "old" }],
        };
        var keyedAfter = new ReplaceKeyedSequenceHolder
        {
            Items = [new KeyedServer { Id = "b", Name = "new" }],
        };
        var keyedPatch = ReplaceKeyedSequenceHolder.Patch.Between(
            ReplaceKeyedSequenceHolder.Fragment.From(keyedBefore),
            ReplaceKeyedSequenceHolder.Fragment.From(keyedAfter)
        );
        keyedPatch.ApplyTo(keyedBefore).Items.Select(item => item.Id).ShouldBe(["b"]);
        typeof(ReplaceKeyedSequenceHolder.Patch).GetProperty("ItemsPatch").ShouldBeNull();
        var keyedChange = ReplaceKeyedSequenceHolder.ChangeSet.Between(
            ReplaceKeyedSequenceHolder.Fragment.From(keyedBefore),
            ReplaceKeyedSequenceHolder.Fragment.From(keyedAfter)
        );
        var keyedJson = JsonSerializer.Serialize(keyedChange.ToPayload());
        var keyedRoundTrip = JsonSerializer
            .Deserialize<ReplaceKeyedSequenceHolder.ChangeSetPayload>(keyedJson)!
            .ToChangeSet();
        keyedRoundTrip
            .TryApplyTo(keyedBefore, out var keyedUpdated, out var keyedConflicts)
            .ShouldBeTrue(string.Join("; ", keyedConflicts ?? []));
        keyedUpdated!.Items.Select(item => item.Id).ShouldBe(["b"]);
        typeof(ReplaceKeyedSequenceHolder.ChangeSet)
            .GetProperty("Items")!
            .PropertyType.GetMethod("GetChange")
            .ShouldBeNull();

        var dictionaryBefore = new ReplaceDictionaryHolder
        {
            Items = new() { ["a"] = new KeyedServer { Id = "a", Name = "old" } },
        };
        var dictionaryAfter = new ReplaceDictionaryHolder
        {
            Items = new() { ["b"] = new KeyedServer { Id = "b", Name = "new" } },
        };
        var dictPatch = ReplaceDictionaryHolder.Patch.Between(
            ReplaceDictionaryHolder.Fragment.From(dictionaryBefore),
            ReplaceDictionaryHolder.Fragment.From(dictionaryAfter)
        );
        dictPatch.ApplyTo(dictionaryBefore).Items.Keys.ShouldBe(["b"]);
        typeof(ReplaceDictionaryHolder.Patch).GetProperty("ItemsPatch").ShouldBeNull();
        typeof(ReplaceDictionaryHolder.ChangeSet)
            .GetProperty("Items")!
            .PropertyType.GetMethod("GetChange")
            .ShouldBeNull();

        var dictChanges = ReplaceDictionaryHolder.ChangeSet.Between(
            ReplaceDictionaryHolder.Fragment.From(dictionaryBefore),
            ReplaceDictionaryHolder.Fragment.From(dictionaryAfter)
        );
        var serialized = JsonSerializer.Serialize(dictChanges.ToPayload());
        var roundTripped = JsonSerializer
            .Deserialize<ReplaceDictionaryHolder.ChangeSetPayload>(serialized)!
            .ToChangeSet();
        roundTripped
            .ToPatch()
            .ApplyTo(dictionaryBefore)
            .Items.Keys.ShouldBe(["b"]);
    }
}
