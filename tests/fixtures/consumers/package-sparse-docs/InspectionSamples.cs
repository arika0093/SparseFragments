using SparseFragments;

// Canonical compile-checked mirror of docs/inspection.md (#75).
// Covers the single inspection workflow: T.Sparse.Properties static model
// semantics plus patch.Changes semantic change enumeration, including nested
// recursion and keyed add/remove/edit/order.
public static class InspectionSamples
{
    public static void Run()
    {
        FirstModelAndChanges();
        DescriptorKinds();
        NestedChanges();
        KeyedChanges();
    }

    private static void FirstModelAndChanges()
    {
        // sample: inspection-first
        var before = Roster.Fragment.From(new Roster
        {
            Quests = new() { new Quest { Id = "a", Title = "Old" } },
        });
        var after = Roster.Fragment.From(new Roster
        {
            Quests = new() { new Quest { Id = "a", Title = "New" } },
        });

        foreach (var property in Roster.Sparse.Properties)
        {
            Console.WriteLine(property.Name);
        }
        // Name
        // Quests

        var patch = Roster.Patch.Between(before, after);
        foreach (var change in patch.Changes)
        {
            Console.WriteLine(change.Property.Name);
        }
        // Quests
        // /sample
        DocsCheck.Require(
            Roster.Sparse.Properties.Select(static property => property.Name)
                .SequenceEqual(new[] { "Name", "Quests" }),
            "Properties enumerates Name then Quests");
        DocsCheck.Require(
            patch.Changes.Select(static change => change.Property.Name)
                .SequenceEqual(new[] { "Quests" }),
            "Changes lists the changed Quests member");
    }

    private static void DescriptorKinds()
    {
        // sample: inspection-kinds
        var byName = Roster.Sparse.Properties.ToDictionary(static property => property.Name);

        var quests = byName["Quests"];
        // quests.CollectionSemantic == SparseCollectionSemantic.KeyedSequence
        // quests.KeyKind == SparseKeyKind.Property
        // quests.KeyPropertyNames == ["Id"]

        var name = byName["Name"];
        // name.CollectionSemantic == SparseCollectionSemantic.None
        // name.IsNestedModel == false
        // /sample
        DocsCheck.Require(
            quests.CollectionSemantic == SparseCollectionSemantic.KeyedSequence,
            "Quest list is a keyed sequence");
        DocsCheck.Require(quests.KeyKind == SparseKeyKind.Property, "single property key");
        DocsCheck.Require(
            quests.KeyPropertyNames.SequenceEqual(new[] { "Id" }), "key property is Id");
        DocsCheck.Require(
            name.CollectionSemantic == SparseCollectionSemantic.None, "Name is not a collection");
        DocsCheck.Require(!name.IsNestedModel, "Name is not a nested model");
    }

    private static void NestedChanges()
    {
        // sample: inspection-nested
        var editBefore = Roster.Fragment.From(new Roster
        {
            Quests = new() { new Quest { Id = "a", Title = "Old" } },
        });
        var editAfter = Roster.Fragment.From(new Roster
        {
            Quests = new() { new Quest { Id = "a", Title = "New" } },
        });

        var editPatch = Roster.Patch.Between(editBefore, editAfter);
        var questsChange = editPatch.Changes.Single(static change => change.Property.Name == "Quests");
        var edit = questsChange.Keyed!.Edited.Single();
        foreach (var nested in edit.NestedChanges)
        {
            Console.WriteLine(nested.Property.Name);
        }
        // Title
        // /sample
        DocsCheck.Require(
            questsChange.Kind == SparseChangeKind.KeyedCollection,
            "quests change is a keyed collection change");
        DocsCheck.Require(
            edit.NestedChanges.Select(static change => change.Property.Name)
                .SequenceEqual(new[] { "Title" }),
            "keyed edit exposes nested Title change");
    }

    private static void KeyedChanges()
    {
        // sample: inspection-keyed
        var keyedBefore = Roster.Fragment.From(new Roster
        {
            Quests = new() { new Quest { Id = "a", Title = "A" }, new Quest { Id = "b", Title = "B" } },
        });
        var keyedAfter = Roster.Fragment.From(new Roster
        {
            Quests = new() { new Quest { Id = "b", Title = "B2" }, new Quest { Id = "c", Title = "C" } },
        });

        var keyedPatch = Roster.Patch.Between(keyedBefore, keyedAfter);
        var keyedChange = keyedPatch.Changes.Single(static change => change.Property.Name == "Quests");
        var keyed = keyedChange.Keyed!;
        foreach (var added in keyed.Added)
        {
            Console.WriteLine(((Quest)added!).Id);
        }
        // c
        foreach (var removed in keyed.RemovedKeys)
        {
            Console.WriteLine((string)removed!);
        }
        // a
        // keyed.Edited holds per-key nested changes (see above)
        // keyed.HasOrder reports whether the final order changed
        // keyed.KeyOrder holds the final key order when it did
        // /sample
        DocsCheck.Require(
            keyed.Added.Select(static element => ((Quest)element!).Id)
                .SequenceEqual(new[] { "c" }),
            "added element is c");
        DocsCheck.Require(
            keyed.RemovedKeys.Select(static key => (string)key!).SequenceEqual(new[] { "a" }),
            "removed key is a");
        DocsCheck.Require(
            keyed.Edited.Single().NestedChanges.Any(static change => change.Property.Name == "Title"),
            "edited element carries nested Title change");
        DocsCheck.Require(keyed.HasOrder, "changed membership reports final order");
        DocsCheck.Require(
            keyed.KeyOrder.Select(static key => (string)key!).SequenceEqual(new[] { "b", "c" }),
            "final key order is [b, c]");
    }
}

// sample: inspection-models
[SparseFragmentModel]
public partial class Roster
{
    public string Name { get; set; } = string.Empty;

    public List<Quest> Quests { get; set; } = new();
}

public partial class Quest
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
}
// /sample
