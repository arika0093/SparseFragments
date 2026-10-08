namespace SparseFragments.NativeAotSmoke;

public sealed class AotFragmentCoreTests
{
    [Test]
    public async Task OptionalDistinguishesMissingNullAndValue()
    {
        var missing = Optional<string?>.Missing;
        await Assert.That(!missing.IsPresent).IsTrue();

        var presentNull = Optional<string?>.Present(null);
        await Assert.That(presentNull.IsPresent && presentNull.Value is null).IsTrue();

        Optional<string?> implicitValue = "hello";
        await Assert.That(implicitValue.IsPresent && implicitValue.Value == "hello").IsTrue();
    }

    [Test]
    public async Task MergeKeepsExplicitNullOverLowerLayer()
    {
        var defaults = AotWidget.Fragment.From(
            new AotWidget
            {
                Name = "fallback",
                Nested = new AotNested { Host = "db.local" },
            }
        );
        var clearsName = new AotWidget.Fragment { Name = (string?)null };

        await Assert.That(defaults.Merge(clearsName).ToModel().Name is null).IsTrue();
    }

    [Test]
    public async Task MergeFallsThroughWhenMissing()
    {
        var defaults = AotWidget.Fragment.From(
            new AotWidget
            {
                Name = "fallback",
                Nested = new AotNested { Host = "db.local" },
            }
        );

        await Assert.That(defaults.Merge(new AotWidget.Fragment()).ToModel().Name).IsEqualTo("fallback");
    }

    [Test]
    public async Task MergePreservesLowerNestedMemberAndOverridesWithHigher()
    {
        var lower = new AotWidget.Fragment
        {
            Nested = Optional<AotNested.Fragment?>.Present(
                new AotNested.Fragment { Host = Optional<string>.Present("db.local") }
            ),
            Plugins = Optional<IReadOnlyList<string>>.Present(["base-plugin"]),
        };
        var higher = new AotWidget.Fragment
        {
            Nested = new AotNested.Fragment { Port = 9 },
            Plugins = Optional<IReadOnlyList<string>>.Present(["extra-plugin"]),
        };

        var merged = lower.Merge(higher).ToModel();

        await Assert.That(merged.Nested!.Host).IsEqualTo("db.local");
        await Assert.That(merged.Nested.Port).IsEqualTo(9);
        await Assert.That(merged.Plugins.SequenceEqual(["base-plugin", "extra-plugin"])).IsTrue();
    }

    [Test]
    public async Task DiffApplyChangesReplaysChangedMember()
    {
        var before = new AotWidget { Name = "before", Count = 1 };
        var after = new AotWidget { Name = "after", Count = 1 };
        var diff = AotWidget.Fragment.Diff(before, after);
        var replayed = AotWidget.Fragment.From(before).ApplyChanges(diff);

        await Assert.That(replayed.Name.Value).IsEqualTo("after");
        await Assert.That(replayed.Count.Value).IsEqualTo(1);
    }

    [Test]
    public async Task PatchAppliesExplicitNullAndNestedSet()
    {
        var original = AotWidget.Fragment.From(
            new AotWidget
            {
                Name = "original",
                Nested = new AotNested { Host = "keep", Port = 7 },
            }
        );
        var patch = new AotWidget.Patch { Name = (string?)null };
        patch.Nested.Port = 9;

        await Assert.That(!patch.IsEmpty).IsTrue();
        await Assert.That(new AotWidget.Patch().IsEmpty).IsTrue();

        var updated = original.Apply(patch);
        await Assert.That(updated.Name.IsPresent && updated.Name.Value is null).IsTrue();
        await Assert.That(
            updated.Nested.Value!.Host.Value == "keep" && updated.Nested.Value.Port.Value == 9
        ).IsTrue();
    }

    [Test]
    public async Task PatchApplyDoesNotMutateOriginal()
    {
        var original = AotWidget.Fragment.From(
            new AotWidget
            {
                Name = "original",
                Nested = new AotNested { Host = "keep", Port = 7 },
            }
        );
        var patch = new AotWidget.Patch { Name = (string?)null };
        patch.Nested.Port = 9;
        _ = original.Apply(patch);

        await Assert.That(original.Nested.Value!.Port.Value).IsEqualTo(7);
        await Assert.That(original.Name.Value).IsEqualTo("original");
    }

    [Test]
    public async Task PatchRemoveDropsContribution()
    {
        var original = AotWidget.Fragment.From(
            new AotWidget
            {
                Name = "original",
                Nested = new AotNested { Host = "keep", Port = 7 },
            }
        );
        var remove = new AotWidget.Patch();
        remove.Nested.Remove();

        await Assert.That(!original.Apply(remove).Nested.IsPresent).IsTrue();
    }

    [Test]
    public async Task PatchSetNullIsPresentNull()
    {
        var original = AotWidget.Fragment.From(
            new AotWidget
            {
                Name = "original",
                Nested = new AotNested { Host = "keep", Port = 7 },
            }
        );
        var toNull = new AotWidget.Patch();
        toNull.Nested.SetNull();

        var nulled = original.Apply(toNull);
        await Assert.That(nulled.Nested.IsPresent && nulled.Nested.Value is null).IsTrue();
    }

    [Test]
    public async Task BuilderDropsMember()
    {
        var original = AotWidget.Fragment.From(
            new AotWidget
            {
                Name = "original",
                Nested = new AotNested { Host = "keep", Port = 7 },
            }
        );
        var edited = original.ToBuilder();
        edited.Name = Optional<string?>.Missing;
        var rebuilt = edited.Build();

        await Assert.That(!rebuilt.Name.IsPresent && original.Name.IsPresent).IsTrue();
    }

    [Test]
    public async Task DeepCloneIsolatesNestedReferences()
    {
        var model = new AotWidget
        {
            Name = "m",
            Nested = new AotNested { Host = "h" },
        };
        var clone = model.DeepClone();
        clone.Nested!.Host = "changed";

        await Assert.That(model.Nested.Host).IsEqualTo("h");
        await Assert.That(clone.Nested.Host).IsEqualTo("changed");
    }
}
