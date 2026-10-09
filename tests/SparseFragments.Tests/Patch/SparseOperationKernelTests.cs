using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests;

/// <summary>Model-independent operation kernel regressions (issue #188).</summary>
public sealed class SparseOperationKernelTests
{
    [Test]
    public void PresenceCompose_CoversSequentialEquivalence()
    {
        // Absent -> present -> absent composes to empty.
        SparseOperationKernels
            .TryComposePresence(false, true, false, out var before, out var after)
            .ShouldBeFalse();
        before.ShouldBeFalse();
        after.ShouldBeFalse();

        // Present -> absent -> present is non-empty.
        SparseOperationKernels.TryComposePresence(true, false, true, out _, out _).ShouldBeTrue();
    }

    [Test]
    public void EmptyIdentities_Hold()
    {
        SparseOperationKernels.IsEmptyTransition(true, true, false).ShouldBeTrue();
        SparseOperationKernels.IsEmptyTransition(false, false, false).ShouldBeTrue();
        SparseOperationKernels.IsEmptyTransition(true, false, false).ShouldBeFalse();
        SparseOperationKernels.IsEmptyTransition(true, true, true).ShouldBeFalse();
        SparseOperationKernels.IsEmptyKeyedSet<string>([], [], false).ShouldBeTrue();
        SparseOperationKernels.IsEmptyKeyedSet<string>(["a"], [], false).ShouldBeFalse();
    }

    [Test]
    public void KeyedCompose_AddThenRemoveCancels()
    {
        SparseOperationKernels.ComposeKeyedSets<string>(
            ["k"],
            [],
            [],
            ["k"],
            out var added,
            out var removed,
            out _
        );
        added.ShouldBeEmpty();
        removed.ShouldBeEmpty();
    }

    [Test]
    public void KeyedCompose_RemoveThenAddRetains()
    {
        SparseOperationKernels.ComposeKeyedSets<string>(
            [],
            ["k"],
            ["k"],
            [],
            out var added,
            out var removed,
            out _
        );
        added.ShouldBeEmpty();
        removed.ShouldBeEmpty();
    }

    [Test]
    public void KeyedInvert_SwapsAndRoundTrips()
    {
        SparseOperationKernels.InvertKeyedSets<string>(
            ["a"],
            ["b"],
            out var added,
            out var removed
        );
        added.ShouldBe(["b"]);
        removed.ShouldBe(["a"]);

        SparseOperationKernels.InvertKeyedSets<string>(
            added,
            removed,
            out var restored,
            out var restoredRemoved
        );
        restored.ShouldBe(["a"]);
        restoredRemoved.ShouldBe(["b"]);
    }

    [Test]
    public void KeyedKernels_WorkAcrossKeyShapes()
    {
        // Keyed-sequence shape: string keys with ordinal comparison.
        SparseOperationKernels
            .IndexOfKey<string>(["a", "b"], "b", StringComparer.Ordinal)
            .ShouldBe(1);
        SparseOperationKernels
            .IndexOfKey<string>(["a"], "missing", StringComparer.Ordinal)
            .ShouldBe(-1);

        // Dictionary shape: int keys with default comparison.
        SparseOperationKernels.IndexOfKey<int>([1, 2], 2).ShouldBe(1);
        SparseOperationKernels.IsUnassignedKey(0, 0).ShouldBeTrue();
        SparseOperationKernels.IsUnassignedKey("sentinel", "sentinel").ShouldBeTrue();
        SparseOperationKernels.IsUnassignedKey("a", "sentinel").ShouldBeFalse();
    }

    [Test]
    public void ConflictPaths_UseCanonicalQuoting()
    {
        SparseOperationKernels.FormatKeyedPath("Items", "k").ShouldBe("Items[\"k\"]");
        SparseOperationKernels.FormatKeyedPath("Scores", "a]b").ShouldBe("Scores[\"a]b\"]");
        SparseOperationKernels.FormatKeyedPath("Tags", "a\"b\\c").ShouldBe("Tags[\"a\\\"b\\\\c\"]");
    }

    [Test]
    public void AncestorChecks_MatchSegmentSemantics()
    {
        SparseOperationKernels.IsAncestorOrDescendant("Nested", "Nested.Host").ShouldBeTrue();
        SparseOperationKernels.IsAncestorOrDescendant("A", "AB").ShouldBeFalse();
        SparseOperationKernels
            .IsAncestorOrDescendant("Items[\"k\"]", "Items[\"k\"].Value")
            .ShouldBeTrue();
        SparseOperationKernels.IsAncestorOrDescendant("A", "A").ShouldBeFalse();
    }

    [Test]
    public void SharedHelperSource_IsDeterministicAndStable()
    {
        var kinds =
            SparsePatchKernelKind.KeyedMembership
            | SparsePatchKernelKind.EmptyIdentities
            | SparsePatchKernelKind.ConflictPaths
            | SparsePatchKernelKind.PathKernels;
        var first = SparsePatchKernelEmitter.RenderHelperSource("Acme.Generated", kinds);
        var second = SparsePatchKernelEmitter.RenderHelperSource("Acme.Generated", kinds);
        first.ShouldBe(second);
        first.ShouldContain("namespace Acme.Generated");
        first.ShouldContain("PatchOperationKernels");
        first.ShouldContain("IndexOfKey");
        first.ShouldContain("FormatKeyedPath");
        first.ShouldNotContain("SparseFragments.");
        SparsePatchKernelInventory.Extracted.Length.ShouldBeGreaterThan(0);
        SparsePatchKernelInventory.RemainingModelSpecific.Length.ShouldBeGreaterThan(0);
        SparsePatchKernelCapabilities.KernelHintName.ShouldEndWith(".g.cs");
    }

    [Test]
    public void KernelCapabilities_AggregatePerCompilation()
    {
        SparsePatchKernelCapabilities
            .ForCompilation(false, false, false)
            .ShouldBe(SparsePatchKernelKind.None);
        var kinds = SparsePatchKernelCapabilities.ForCompilation(true, true, true);
        kinds.HasFlag(SparsePatchKernelKind.KeyedMembership).ShouldBeTrue();
        kinds.HasFlag(SparsePatchKernelKind.EmptyIdentities).ShouldBeTrue();
    }
}
