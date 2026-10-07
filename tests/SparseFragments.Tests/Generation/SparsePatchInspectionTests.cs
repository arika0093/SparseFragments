using SparseFragments;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class MetaInspectScalar
{
    public string Title { get; set; } = string.Empty;

    public int Count { get; set; }

    public string? Note { get; set; }
}

[SparseFragmentModel]
public partial class MetaInspectNested
{
    public string Name { get; set; } = string.Empty;

    public MetaInspectScalar Child { get; set; } = new();
}

[SparseFragmentModel]
public partial class MetaInspectGroup
{
    [SparseKey]
    public string Name { get; set; } = string.Empty;

    public List<MetaKeyedElement> Members { get; set; } = new();
}

[SparseFragmentModel]
public partial class MetaInspectRoot
{
    public string Title { get; set; } = string.Empty;

    public MetaInspectGroup Group { get; set; } = new();
}

[SparseFragmentModel]
public partial class MetaInspectDict
{
    public Dictionary<string, int> Scores { get; set; } = new();

    public Dictionary<string, MetaKeyedElement> Servers { get; set; } = new();
}

[SparseFragmentModel]
public partial class MetaChangesCollision
{
    public string Changes { get; set; } = string.Empty;

    public string Other { get; set; } = string.Empty;
}

public sealed class SparsePatchInspectionTests
{
    private static Optional<MetaInspectScalar.Fragment?> State(MetaInspectScalar model) =>
        Optional<MetaInspectScalar.Fragment?>.Present(MetaInspectScalar.Fragment.From(model));

    [Test]
    public void UnchangedPatchHasNoChanges()
    {
        var patch = new MetaInspectScalar.Patch();
        patch.IsEmpty.ShouldBeTrue();
        patch.Changes.ShouldBeEmpty();
    }

    [Test]
    public void ScalarSetAndUnset()
    {
        var patch = new MetaInspectScalar.Patch { Title = "hello", Count = 42 };
        patch.Note = FragmentOperation<string?>.Unset;

        var byName = patch.Changes.ToDictionary(c => c.Property.Name);
        byName["Title"].Kind.ShouldBe(SparseChangeKind.Set);
        byName["Title"].Value.ShouldBe("hello");
        byName["Count"].Kind.ShouldBe(SparseChangeKind.Set);
        byName["Count"].Value.ShouldBe(42);
        byName["Note"].Kind.ShouldBe(SparseChangeKind.Unset);
        byName["Note"].Value.ShouldBeNull();
    }

    [Test]
    public void NullableSetNullIsSetWithNullValue()
    {
        var patch = new MetaInspectScalar.Patch { Note = (string?)null };
        var change = patch.Changes.Single();
        change.Property.Name.ShouldBe("Note");
        change.Kind.ShouldBe(SparseChangeKind.Set);
        change.Value.ShouldBeNull();
    }

    [Test]
    public void ChangePropertyIsSharedMetadataDescriptor()
    {
        var patch = new MetaInspectScalar.Patch { Title = "x" };
        var change = patch.Changes.Single(c => c.Property.Name == "Title");
        ReferenceEquals(
                change.Property,
                MetaInspectScalar.Sparse.Properties.Single(p => p.Name == "Title")
            )
            .ShouldBeTrue();
    }

    [Test]
    public void NestedStructuralEditIsRecursive()
    {
        var patch = new MetaInspectNested.Patch();
        patch.Child.Title = "nested";

        var change = patch.Changes.Single();
        change.Property.Name.ShouldBe("Child");
        change.Kind.ShouldBe(SparseChangeKind.Nested);
        var nested = change.NestedChanges;
        nested.Count.ShouldBe(1);
        nested[0].Property.Name.ShouldBe("Title");
        nested[0].Kind.ShouldBe(SparseChangeKind.Set);
        ReferenceEquals(
                nested[0].Property,
                MetaInspectScalar.Sparse.Properties.Single(p => p.Name == "Title")
            )
            .ShouldBeTrue();
    }

    [Test]
    public void NestedSetNullAndUnset()
    {
        var nullPatch = new MetaInspectNested.Patch();
        nullPatch.Child.SetNull();
        var nullChange = nullPatch.Changes.Single();
        nullChange.Property.Name.ShouldBe("Child");
        nullChange.Kind.ShouldBe(SparseChangeKind.Set);
        nullChange.Value.ShouldBeNull();

        var unsetPatch = new MetaInspectNested.Patch();
        unsetPatch.Child.Unset();
        var unsetChange = unsetPatch.Changes.Single();
        unsetChange.Kind.ShouldBe(SparseChangeKind.Unset);
    }

    [Test]
    public void ScalarCollectionReplacementIsSet()
    {
        var patch = new MetaCollections.Patch();
        patch.Tags = new List<string> { "a", "b" };

        var change = patch.Changes.Single(c => c.Property.Name == "Tags");
        change.Kind.ShouldBe(SparseChangeKind.Set);
        ((List<string>)change.Value!).ShouldBe(["a", "b"]);
        change.Property.CollectionSemantic.ShouldBe(SparseCollectionSemantic.ScalarSequence);
    }

    [Test]
    public void KeyedAddRemoveEdit()
    {
        var before = MetaKeyedHolder.Fragment.From(
            new MetaKeyedHolder
            {
                Items =
                [
                    new MetaKeyedElement { Id = "a", Name = "A" },
                    new MetaKeyedElement { Id = "b", Name = "B" },
                ],
            }
        );
        var after = MetaKeyedHolder.Fragment.From(
            new MetaKeyedHolder
            {
                Items =
                [
                    new MetaKeyedElement { Id = "b", Name = "B2" },
                    new MetaKeyedElement { Id = "c", Name = "C" },
                ],
            }
        );
        var patch = MetaKeyedHolder.Patch.Between(
            Optional<MetaKeyedHolder.Fragment?>.Present(before),
            Optional<MetaKeyedHolder.Fragment?>.Present(after)
        );

        var change = patch.Changes.Single(c => c.Property.Name == "Items");
        change.Kind.ShouldBe(SparseChangeKind.KeyedCollection);
        change.Property.ShouldBe(MetaKeyedHolder.Sparse.Properties.Single(p => p.Name == "Items"));
        var keyed = change.Keyed.ShouldNotBeNull();
        keyed.Added.Count.ShouldBe(1);
        ((MetaKeyedElement)keyed.Added[0]!).Id.ShouldBe("c");
        keyed.RemovedKeys.ShouldBe(["a"]);
        keyed.Edited.Count.ShouldBe(1);
        keyed.Edited[0].Key.ShouldBe("b");
        keyed.Edited[0].NestedChanges.Any(c => c.Property.Name == "Name").ShouldBeTrue();
    }

    [Test]
    public void ReorderOnlyReportsOrderWithoutAddsOrEdits()
    {
        MetaKeyedHolder Model(params string[] ids) =>
            new()
            {
                Items = ids.Select(id => new MetaKeyedElement { Id = id, Name = id }).ToList(),
            };
        var patch = MetaKeyedHolder.Patch.Between(
            Optional<MetaKeyedHolder.Fragment?>.Present(
                MetaKeyedHolder.Fragment.From(Model("a", "b"))
            ),
            Optional<MetaKeyedHolder.Fragment?>.Present(
                MetaKeyedHolder.Fragment.From(Model("b", "a"))
            )
        );

        var change = patch.Changes.Single(c => c.Property.Name == "Items");
        change.Kind.ShouldBe(SparseChangeKind.KeyedCollection);
        var keyed = change.Keyed.ShouldNotBeNull();
        keyed.Added.ShouldBeEmpty();
        keyed.RemovedKeys.ShouldBeEmpty();
        keyed.Edited.ShouldBeEmpty();
        keyed.HasOrder.ShouldBeTrue();
        keyed.KeyOrder.Select(k => (string)k!).ToArray().ShouldBe(["b", "a"]);
    }

    [Test]
    public void KeyChangeIsRemovePlusAdd()
    {
        var patch = MetaKeyedHolder.Patch.Between(
            Optional<MetaKeyedHolder.Fragment?>.Present(
                MetaKeyedHolder.Fragment.From(
                    new MetaKeyedHolder { Items = [new MetaKeyedElement { Id = "a", Name = "A" }] }
                )
            ),
            Optional<MetaKeyedHolder.Fragment?>.Present(
                MetaKeyedHolder.Fragment.From(
                    new MetaKeyedHolder { Items = [new MetaKeyedElement { Id = "b", Name = "A" }] }
                )
            )
        );

        var keyed = patch.Changes.Single(c => c.Property.Name == "Items").Keyed.ShouldNotBeNull();
        keyed.RemovedKeys.ShouldBe(["a"]);
        keyed.Added.Count.ShouldBe(1);
    }

    [Test]
    public void WholeKeyedCollectionReplacementIsSet()
    {
        var patch = new MetaKeyedHolder.Patch();
        patch.Items.Set([new MetaKeyedElement { Id = "x", Name = "X" }]);

        var change = patch.Changes.Single(c => c.Property.Name == "Items");
        change.Kind.ShouldBe(SparseChangeKind.Set);
        change.Keyed.ShouldBeNull();
    }

    [Test]
    public void NestedKeyedCollectionsAreRecursive()
    {
        var patch = new MetaInspectRoot.Patch();
        patch.Group.Members.Add(new MetaKeyedElement { Id = "n", Name = "N" });

        var group = patch.Changes.Single();
        group.Property.Name.ShouldBe("Group");
        group.Kind.ShouldBe(SparseChangeKind.Nested);
        var members = group.NestedChanges.Single(c => c.Property.Name == "Members");
        members.Kind.ShouldBe(SparseChangeKind.KeyedCollection);
        members.Keyed.ShouldNotBeNull().Added.Count.ShouldBe(1);
    }

    [Test]
    public void DictionarySetRemoveAndScalarEdit()
    {
        var patch = new MetaInspectDict.Patch();
        patch.Scores.SetEntry("a", 1);
        patch.Scores.RemoveEntry("gone");

        var change = patch.Changes.Single(c => c.Property.Name == "Scores");
        change.Kind.ShouldBe(SparseChangeKind.Dictionary);
        var dict = change.Dictionary.ShouldNotBeNull();
        dict.SetEntries.Count.ShouldBe(1);
        dict.SetEntries[0].Key.ShouldBe("a");
        dict.SetEntries[0].Value.ShouldBe(1);
        dict.RemovedKeys.ShouldBe(["gone"]);
    }

    [Test]
    public void StructuralDictionaryEditExposesNestedChanges()
    {
        var before = MetaInspectDict.Fragment.From(
            new MetaInspectDict
            {
                Servers = new()
                {
                    ["web"] = new MetaKeyedElement { Id = "web", Name = "W" },
                },
            }
        );
        var after = MetaInspectDict.Fragment.From(
            new MetaInspectDict
            {
                Servers = new()
                {
                    ["web"] = new MetaKeyedElement { Id = "web", Name = "W2" },
                },
            }
        );
        var patch = MetaInspectDict.Patch.Between(
            Optional<MetaInspectDict.Fragment?>.Present(before),
            Optional<MetaInspectDict.Fragment?>.Present(after)
        );

        var change = patch.Changes.Single(c => c.Property.Name == "Servers");
        change.Kind.ShouldBe(SparseChangeKind.Dictionary);
        var edit = change.Dictionary.ShouldNotBeNull().Edited.Single();
        edit.Key.ShouldBe("web");
        edit.HasNestedChanges.ShouldBeTrue();
        edit.NestedChanges.Any(c => c.Property.Name == "Name").ShouldBeTrue();
    }

    [Test]
    public void ComposedAndInvertedPatchesAreInspectable()
    {
        var baseModel = new MetaInspectScalar { Title = "t", Count = 1 };
        var mid = new MetaInspectScalar { Title = "t2", Count = 1 };
        var final = new MetaInspectScalar { Title = "t2", Count = 3 };
        var first = MetaInspectScalar.Patch.Between(State(baseModel), State(mid));
        var second = MetaInspectScalar.Patch.Between(State(mid), State(final));
        var composed = first.Compose(second);
        composed.Changes.Select(c => c.Property.Name).ToArray().ShouldBe(["Count", "Title"]);

        var inverted = first.Invert(State(baseModel));
        inverted.Changes.Select(c => c.Property.Name).ShouldContain("Title");
    }

    [Test]
    public void ChangesNameCollisionUsesSparsePrefix()
    {
        var patch = new MetaChangesCollision.Patch { Other = "o" };
        patch.SparseChanges.Count.ShouldBe(1);
        patch.SparseChanges[0].Property.Name.ShouldBe("Other");
    }

    [Test]
    public void InspectionDoesNotMutateEmptyPatches()
    {
        var patch = new MetaKeyedHolder.Patch();
        patch.Changes.ShouldBeEmpty();
        patch.IsEmpty.ShouldBeTrue();
        // Reading Changes must not materialize lazy member patches.
        patch.Changes.ShouldBeEmpty();
    }
}
