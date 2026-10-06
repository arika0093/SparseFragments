using SparseFragments;

namespace SparseFragments.Tests;

// Plain (non-partial, non-fragment) element: atomic replace value, deep-cloned.
public sealed class ListChildItem
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

[SparseFragmentModel]
public partial class ClassListHolder
{
    public List<ListChildItem> Items { get; set; } = new();
}

[SparseFragmentModel]
public partial class NullableClassListHolder
{
    public List<ListChildItem>? Items { get; set; }
}

[SparseFragmentModel]
public partial class FragmentChild
{
    [SparseKey]
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

[SparseFragmentModel]
public partial class FragmentChildListHolder
{
    public List<FragmentChild> Items { get; set; } = new();
}

[SparseFragmentModel]
public partial class AppendClassListHolder
{
    [SparseMerge(MergeMode.Append)]
    public List<ListChildItem> Items { get; set; } = new();
}

public sealed class ListClassPatchTests
{
    private static ListChildItem Item(string name, int count = 1) =>
        new() { Name = name, Count = count };

    private static ClassListHolder Model(params ListChildItem[] items) =>
        new() { Items = items.ToList() };

    [Test]
    public void FromClonesListAndElements()
    {
        var item = Item("a");
        var model = Model(item);
        var fragment = ClassListHolder.Fragment.From(model);

        ReferenceEquals(fragment.Items.Value, model.Items).ShouldBeFalse();
        ReferenceEquals(fragment.Items.Value![0], item).ShouldBeFalse();

        model.Items[0].Name = "mutated";
        model.Items.Add(Item("b"));

        fragment.Items.Value!.Count.ShouldBe(1);
        fragment.Items.Value[0].Name.ShouldBe("a");
    }

    [Test]
    public void ToModelRoundTripClonesListAndElements()
    {
        var model = Model(Item("a"));
        var fragment = ClassListHolder.Fragment.From(model);
        var restored = fragment.ToModel();

        // From clones, so the round-tripped model shares neither the list
        // nor the elements with the source model.
        ReferenceEquals(restored.Items, model.Items).ShouldBeFalse();
        ReferenceEquals(restored.Items[0], model.Items[0]).ShouldBeFalse();

        restored.Items[0].Name = "mutated";
        restored.Items.Add(Item("b"));

        model.Items.Count.ShouldBe(1);
        model.Items[0].Name.ShouldBe("a");
    }

    [Test]
    public void DeepCloneModelIsIndependent()
    {
        var model = Model(Item("a"));
        var clone = model.DeepClone();

        ReferenceEquals(clone.Items, model.Items).ShouldBeFalse();
        ReferenceEquals(clone.Items[0], model.Items[0]).ShouldBeFalse();

        clone.Items[0].Name = "mutated";
        clone.Items.Add(Item("b"));

        model.Items.Count.ShouldBe(1);
        model.Items[0].Name.ShouldBe("a");
    }

    [Test]
    public void DeepCloneFragmentIsIndependent()
    {
        var fragment = ClassListHolder.Fragment.From(Model(Item("a")));
        var clone = fragment.DeepClone();

        clone.ShouldNotBeSameAs(fragment);
        ReferenceEquals(clone.Items.Value, fragment.Items.Value).ShouldBeFalse();
        ReferenceEquals(clone.Items.Value![0], fragment.Items.Value![0]).ShouldBeFalse();

        clone.Items.Value![0].Name = "mutated";
        fragment.Items.Value![0].Name.ShouldBe("a");
    }

    [Test]
    public void MissingEmptyAndPopulatedRemainDistinct()
    {
        var missing = new ClassListHolder.Fragment();
        var empty = new ClassListHolder.Fragment
        {
            Items = Optional<List<ListChildItem>>.Present(new List<ListChildItem>()),
        };
        var populated = ClassListHolder.Fragment.From(Model(Item("a")));

        missing.IsEmpty.ShouldBeTrue();
        empty.IsEmpty.ShouldBeFalse();
        empty.Items.IsPresent.ShouldBeTrue();
        empty.Items.Value.ShouldBeEmpty();

        ClassListHolder.Patch.Between(
            Optional<ClassListHolder.Fragment?>.Present(missing),
            Optional<ClassListHolder.Fragment?>.Present(empty)
        ).IsEmpty.ShouldBeFalse();

        // Distinct empty-list instances compare equal.
        ClassListHolder.Patch.Between(
            Optional<ClassListHolder.Fragment?>.Present(empty),
            Optional<ClassListHolder.Fragment?>.Present(new ClassListHolder.Fragment
            {
                Items = Optional<List<ListChildItem>>.Present(new List<ListChildItem>()),
            })
        ).IsEmpty.ShouldBeTrue();

        ClassListHolder.Patch.Between(
            Optional<ClassListHolder.Fragment?>.Present(empty),
            Optional<ClassListHolder.Fragment?>.Present(populated)
        ).IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void PatchReplacesWholeListAndPreservesOriginal()
    {
        var original = ClassListHolder.Fragment.From(Model(Item("a"), Item("b")));
        var patch = new ClassListHolder.Patch
        {
            Items = new List<ListChildItem> { Item("c", 9) },
        };

        patch.IsEmpty.ShouldBeFalse();
        new ClassListHolder.Patch().IsEmpty.ShouldBeTrue();

        var result = original.Apply(patch);

        result.Items.Value!.Count.ShouldBe(1);
        result.Items.Value[0].Name.ShouldBe("c");
        result.Items.Value[0].Count.ShouldBe(9);

        // Original is untouched and the patch value is cloned into the result.
        original.Items.Value!.Count.ShouldBe(2);
        original.Items.Value[0].Name.ShouldBe("a");
    }

    [Test]
    public void PatchUnsetDropsContribution()
    {
        var original = ClassListHolder.Fragment.From(Model(Item("a")));
        var patch = new ClassListHolder.Patch
        {
            Items = FragmentOperation<List<ListChildItem>>.Unset,
        };

        var result = original.Apply(patch);

        result.Items.IsPresent.ShouldBeFalse();
        original.Items.IsPresent.ShouldBeTrue();
    }

    [Test]
    public void NullableListDistinguishesNullFromUnsetAndEmpty()
    {
        var basis = NullableClassListHolder.Fragment.From(
            new NullableClassListHolder { Items = new List<ListChildItem> { Item("a") } }
        );

        var toNull = new NullableClassListHolder.Patch
        {
            Items = (List<ListChildItem>?)null,
        };
        var nulled = basis.Apply(toNull);
        nulled.Items.IsPresent.ShouldBeTrue();
        nulled.Items.Value.ShouldBeNull();

        var unset = new NullableClassListHolder.Patch
        {
            Items = FragmentOperation<List<ListChildItem>?>.Unset,
        };
        unset.Apply(Optional<NullableClassListHolder.Fragment?>.Present(nulled)).Value!.Items.IsPresent.ShouldBeFalse();
        nulled.Apply(unset).Items.IsPresent.ShouldBeFalse();

        var toEmpty = new NullableClassListHolder.Patch
        {
            Items = new List<ListChildItem>(),
        };
        var emptied = basis.Apply(toEmpty);
        emptied.Items.IsPresent.ShouldBeTrue();
        emptied.Items.Value.ShouldNotBeNull();
        emptied.Items.Value.ShouldBeEmpty();
    }

    [Test]
    public void DiffDetectsReplacementAndRoundTripsViaApplyChanges()
    {
        var before = Model(Item("a", 1));
        var after = Model(Item("b", 2), Item("c", 3));

        var diff = ClassListHolder.Fragment.Diff(before, after);

        diff.IsEmpty.ShouldBeFalse();
        diff.Items.IsPresent.ShouldBeTrue();

        var applied = ClassListHolder.Fragment.From(before).ApplyChanges(diff);
        applied.Items.Value!.Count.ShouldBe(2);
        applied.Items.Value[0].Name.ShouldBe("b");
        applied.Items.Value[1].Name.ShouldBe("c");
        applied.ToModel().Items[1].Count.ShouldBe(3);

        ClassListHolder.Fragment.Diff(before, before).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void DiffUsesIdentityEqualityForPlainClassElements()
    {
        var before = Model(Item("a", 1));
        // Same values, different element instances.
        var after = Model(Item("a", 1));

        // Plain classes without value equality compare by reference,
        // so distinct instances are a replacement even when contents match.
        ClassListHolder.Fragment.Diff(before, after).Items.IsPresent.ShouldBeTrue();
    }

    [Test]
    public void ReorderedListsAreReplacements()
    {
        var before = Model(Item("a"), Item("b"));
        var after = Model(Item("b"), Item("a"));

        var diff = ClassListHolder.Fragment.Diff(before, after);

        diff.Items.IsPresent.ShouldBeTrue();
        ClassListHolder.Fragment.From(before).ApplyChanges(diff).Items.Value!
            .Select(item => item.Name).ShouldBe(["b", "a"]);
    }

    [Test]
    public void MergeUsesReplaceSemantics()
    {
        var lower = ClassListHolder.Fragment.From(Model(Item("a")));
        var higher = ClassListHolder.Fragment.From(Model(Item("b")));
        var missing = new ClassListHolder.Fragment();

        lower.Merge(higher).Items.Value!.Single().Name.ShouldBe("b");
        lower.Merge(missing).Items.Value!.Single().Name.ShouldBe("a");
        missing.Merge(higher).Items.Value!.Single().Name.ShouldBe("b");
    }

    [Test]
    public void AppendMergeConcatenatesClassLists()
    {
        var lower = new AppendClassListHolder.Fragment
        {
            Items = Optional<List<ListChildItem>>.Present(new List<ListChildItem> { Item("a") }),
        };
        var higher = new AppendClassListHolder.Fragment
        {
            Items = Optional<List<ListChildItem>>.Present(new List<ListChildItem> { Item("b") }),
        };

        var merged = lower.Merge(higher).ToModel();

        merged.Items.Select(item => item.Name).ShouldBe(["a", "b"]);

        // Baseline projection also concatenates.
        var baseline = new AppendClassListHolder { Items = new List<ListChildItem> { Item("a") } };
        var patch = new AppendClassListHolder.Fragment
        {
            Items = Optional<List<ListChildItem>>.Present(new List<ListChildItem> { Item("b") }),
        };
        patch.ToModel(baseline).Items.Select(item => item.Name).ShouldBe(["a", "b"]);
    }

    [Test]
    public void PatchBetweenInvertComposeRoundTrip()
    {
        var before = Optional<ClassListHolder.Fragment?>.Present(
            ClassListHolder.Fragment.From(Model(Item("a")))
        );
        var after = Optional<ClassListHolder.Fragment?>.Present(
            ClassListHolder.Fragment.From(Model(Item("b"), Item("c")))
        );

        var patch = ClassListHolder.Patch.Between(before, after);
        patch.IsEmpty.ShouldBeFalse();

        SameFragment(after, patch.Apply(before)).ShouldBeTrue();
        SameFragment(before, patch.Invert(before).Apply(patch.Apply(before))).ShouldBeTrue();

        var first = ClassListHolder.Patch.Between(
            before,
            Optional<ClassListHolder.Fragment?>.Present(
                ClassListHolder.Fragment.From(Model(Item("x")))
            )
        );
        var second = new ClassListHolder.Patch
        {
            Items = new List<ListChildItem> { Item("y") },
        };
        SameFragment(second.Apply(first.Apply(before)), first.Compose(second).Apply(before))
            .ShouldBeTrue();

        ClassListHolder.Patch.Between(after, after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void FragmentModelElementsStillReplaceWholeList()
    {
        var original = FragmentChildListHolder.Fragment.From(
            new FragmentChildListHolder
            {
                Items = new List<FragmentChild>
                {
                    new() { Name = "a", Count = 1 },
                    new() { Name = "b", Count = 2 },
                },
            }
        );

        // Whole-list replacement: no per-element nested patch, just Set.
        var patch = new FragmentChildListHolder.Patch
        {
            Items = new List<FragmentChild> { new() { Name = "c", Count = 3 } },
        };

        var result = original.Apply(patch);
        result.Items.Value!.Count.ShouldBe(1);
        result.Items.Value[0].Name.ShouldBe("c");
        original.Items.Value!.Count.ShouldBe(2);

        var before = new FragmentChildListHolder
        {
            Items = new List<FragmentChild> { new() { Name = "a", Count = 1 } },
        };
        var after = new FragmentChildListHolder
        {
            Items = new List<FragmentChild> { new() { Name = "b", Count = 2 } },
        };
        var diff = FragmentChildListHolder.Fragment.Diff(before, after);
        diff.Items.IsPresent.ShouldBeTrue();
        FragmentChildListHolder.Fragment.From(before).ApplyChanges(diff)
            .Items.Value!.Single().Name.ShouldBe("b");

        // Elements are deep-cloned through From.
        var source = new FragmentChild { Name = "a", Count = 1 };
        var holder = new FragmentChildListHolder { Items = new List<FragmentChild> { source } };
        var fragment = FragmentChildListHolder.Fragment.From(holder);
        ReferenceEquals(fragment.Items.Value![0], source).ShouldBeFalse();
        source.Name = "mutated";
        fragment.Items.Value[0].Name.ShouldBe("a");
    }

    [Test]
    public void RebaseKeepsUnchangedPropagatesAppliedAndConflictsOnDivergence()
    {
        Optional<ClassListHolder.Fragment?> State(ClassListHolder model) =>
            Optional<ClassListHolder.Fragment?>.Present(ClassListHolder.Fragment.From(model));

        var baseState = State(Model(Item("a")));
        var local = new ClassListHolder.Patch
        {
            Items = new List<ListChildItem> { Item("b") },
        };

        // Current untouched (same sparse state): local edit survives.
        // NOTE: List<plain class> uses reference equality for elements, so a
        // newly built "equal by value" state would look like a concurrent edit.
        // Reuse the same state to mean "untouched".
        var kept = ClassListHolder.Patch.Rebase(baseState, local, baseState);
        kept.HasConflicts.ShouldBeFalse();
        kept.Patch.Apply(baseState).Value!.Items.Value!.Single().Name.ShouldBe("b");

        // Already applied: becomes a no-op.
        var desired = local.Apply(baseState);
        var alreadyApplied = ClassListHolder.Patch.Rebase(baseState, local, desired);
        alreadyApplied.HasConflicts.ShouldBeFalse();
        alreadyApplied.Patch.IsEmpty.ShouldBeTrue();

        // Concurrent divergent edit: scalar conflict on Items.
        var conflicted = ClassListHolder.Patch.Rebase(
            baseState,
            local,
            State(Model(Item("c")))
        );
        conflicted.HasConflicts.ShouldBeTrue();
        conflicted.Conflicts.Single().Path.ShouldBe(["Items"]);
        conflicted.Conflicts.Single().Kind.ShouldBe(SparsePatchConflictKind.Scalar);
    }

    private static bool SameFragment(
        Optional<ClassListHolder.Fragment?> left,
        Optional<ClassListHolder.Fragment?> right
    ) => ClassListHolder.Patch.Between(left, right).IsEmpty;
}
