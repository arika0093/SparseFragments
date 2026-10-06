using SparseFragments.CompilerServices;

namespace SparseFragments.Tests;

public sealed class SparseCollectionProvenanceTests
{
    private static Optional<IReadOnlyList<string>?> Present(params string[] values) =>
        Optional<IReadOnlyList<string>?>.Present(values);

    private static Optional<IReadOnlyList<string>?> PresentNull() =>
        Optional<IReadOnlyList<string>?>.Present((IReadOnlyList<string>?)null);

    private static Optional<IReadOnlyList<string>?> Missing() => Optional<IReadOnlyList<string>?>.Missing;

    [Test]
    public void ReplaceMapsEveryElementToHighestPresent()
    {
        var contributions = new[] { Present("a"), Present("b", "c"), Missing() };

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.Replace,
                contributions,
                Present("b", "c"),
                null,
                out var origins,
                out var reason
            )
            .ShouldBeTrue();
        reason.ShouldBeNull();
        origins.ShouldBe([1, 1]);
    }

    [Test]
    public void ReplaceIgnoresMissingHigherContributions()
    {
        var contributions = new[] { Present("a"), Missing() };

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.Replace,
                contributions,
                Present("a"),
                null,
                out var origins,
                out _
            )
            .ShouldBeTrue();
        origins.ShouldBe([0]);
    }

    [Test]
    public void ReplaceWithNoPresentRequiresMissingEffective()
    {
        var contributions = new[] { Missing(), Missing() };

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.Replace,
                contributions,
                Missing(),
                null,
                out var origins,
                out _
            )
            .ShouldBeTrue();
        origins.ShouldBeEmpty();

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.Replace,
                contributions,
                Present("a"),
                null,
                out _,
                out var reason
            )
            .ShouldBeFalse();
        reason.ShouldNotBeNull();
    }

    [Test]
    public void ReplaceTreatsPresentNullAsWinningReset()
    {
        var contributions = new[] { Present("a"), PresentNull() };

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.Replace,
                contributions,
                PresentNull(),
                null,
                out var origins,
                out _
            )
            .ShouldBeTrue();
        origins.ShouldBeEmpty();

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.Replace,
                contributions,
                Present("a"),
                null,
                out _,
                out var reason
            )
            .ShouldBeFalse();
        reason.ShouldNotBeNull();
    }

    [Test]
    public void ReplaceKeepsDuplicateElementsOnWinner()
    {
        var contributions = new[] { Present("x"), Present("a", "a") };

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.Replace,
                contributions,
                Present("a", "a"),
                null,
                out var origins,
                out _
            )
            .ShouldBeTrue();
        origins.ShouldBe([1, 1]);
    }

    [Test]
    public void ReplaceFailsWhenEffectiveDiffersFromWinner()
    {
        var contributions = new[] { Present("a"), Present("b") };

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.Replace,
                contributions,
                Present("a"),
                null,
                out _,
                out var reason
            )
            .ShouldBeFalse();
        reason.ShouldNotBeNull();
    }

    [Test]
    public void AppendConcatenatesLowToHighWithPerPositionOrigins()
    {
        var contributions = new[] { Present("a"), Present("b", "c"), Present("d") };

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.Append,
                contributions,
                Present("a", "b", "c", "d"),
                null,
                out var origins,
                out _
            )
            .ShouldBeTrue();
        origins.ShouldBe([0, 1, 1, 2]);
    }

    [Test]
    public void AppendSkipsMissingContributions()
    {
        var contributions = new[] { Present("a"), Missing(), Present("b") };

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.Append,
                contributions,
                Present("a", "b"),
                null,
                out var origins,
                out _
            )
            .ShouldBeTrue();
        origins.ShouldBe([0, 2]);
    }

    [Test]
    public void AppendResetDiscardsLowerContributions()
    {
        var contributions = new[] { Present("a", "b"), PresentNull(), Present("c") };

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.Append,
                contributions,
                Present("c"),
                null,
                out var origins,
                out _
            )
            .ShouldBeTrue();
        origins.ShouldBe([2]);

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.Append,
                contributions,
                Present("a", "b", "c"),
                null,
                out _,
                out var reason
            )
            .ShouldBeFalse();
        reason.ShouldNotBeNull();
    }

    [Test]
    public void AppendHighestResetRequiresNullEffective()
    {
        var contributions = new[] { Present("a"), PresentNull() };

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.Append,
                contributions,
                PresentNull(),
                null,
                out var origins,
                out _
            )
            .ShouldBeTrue();
        origins.ShouldBeEmpty();

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.Append,
                contributions,
                Missing(),
                null,
                out _,
                out var reason
            )
            .ShouldBeFalse();
        reason.ShouldNotBeNull();
    }

    [Test]
    public void AppendPreservesDuplicateScalarValues()
    {
        // Scalar sequences stay atomic whole values (#3): duplicates are preserved here,
        // unlike keyed collections where duplicate keys are invalid.
        var contributions = new[] { Present("a", "a"), Present("a") };

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.Append,
                contributions,
                Present("a", "a", "a"),
                null,
                out var origins,
                out _
            )
            .ShouldBeTrue();
        origins.ShouldBe([0, 0, 1]);
    }

    [Test]
    public void AppendWithNoPresentRequiresMissingEffective()
    {
        var contributions = new[] { Missing(), Missing() };

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.Append,
                contributions,
                Missing(),
                null,
                out var origins,
                out _
            )
            .ShouldBeTrue();
        origins.ShouldBeEmpty();

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.Append,
                contributions,
                Present("a"),
                null,
                out _,
                out var reason
            )
            .ShouldBeFalse();
        reason.ShouldNotBeNull();
    }

    [Test]
    public void SetUnionSequenceKeepsFirstOccurrence()
    {
        var contributions = new[] { Present("a", "b"), Present("b", "c") };

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.SetUnion,
                contributions,
                Present("a", "b", "c"),
                null,
                out var origins,
                out _
            )
            .ShouldBeTrue();
        origins.ShouldBe([0, 0, 1]);
    }

    [Test]
    public void SetUnionSequenceCollapsesDuplicatesWithinOneContribution()
    {
        var contributions = new[] { Present("a", "a", "b") };

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.SetUnion,
                contributions,
                Present("a", "b"),
                null,
                out var origins,
                out _
            )
            .ShouldBeTrue();
        origins.ShouldBe([0, 0]);
    }

    [Test]
    public void SetUnionSequenceRequiresInsertionOrder()
    {
        var contributions = new[] { Present("a", "b"), Present("c") };

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.SetUnion,
                contributions,
                Present("c", "b", "a"),
                null,
                out _,
                out var reason
            )
            .ShouldBeFalse();
        reason.ShouldNotBeNull();
    }

    [Test]
    public void SetUnionSequenceRespectsComparer()
    {
        var contributions = new[] { Present("ALPHA"), Present("alpha", "beta") };

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.SetUnion,
                contributions,
                Present("ALPHA", "beta"),
                StringComparer.OrdinalIgnoreCase,
                out var origins,
                out _
            )
            .ShouldBeTrue();
        origins.ShouldBe([0, 1]);

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.SetUnion,
                contributions,
                Present("ALPHA", "beta"),
                StringComparer.Ordinal,
                out _,
                out var reason
            )
            .ShouldBeFalse();
        reason.ShouldNotBeNull();
    }

    [Test]
    public void SetUnionSequenceResetDiscardsLowerContributions()
    {
        var contributions = new[] { Present("a"), PresentNull(), Present("a", "b") };

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.SetUnion,
                contributions,
                Present("a", "b"),
                null,
                out var origins,
                out _
            )
            .ShouldBeTrue();
        origins.ShouldBe([2, 2]);
    }

    [Test]
    public void UnifiedDispatchRejectsCustomContracts()
    {
        var contributions = new[] { Present("a"), Present("b") };

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.Custom,
                contributions,
                Present("a", "b"),
                null,
                out _,
                out var customReason
            )
            .ShouldBeFalse();
        customReason.ShouldNotBeNull();
        customReason!.ShouldContain("explicit provenance contract");

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.Deep,
                contributions,
                Present("a", "b"),
                null,
                out _,
                out var deepReason
            )
            .ShouldBeFalse();
        deepReason.ShouldNotBeNull();
    }

    [Test]
    public void SetProvenanceMapsFirstContributorComparerCorrect()
    {
        var lower = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alpha" };
        var higher = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ALPHA", "beta" };
        var contributions = new Optional<IEnumerable<string>?>[]
        {
            Optional<IEnumerable<string>?>.Present(lower),
            Optional<IEnumerable<string>?>.Present(higher),
        };
        var effective = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alpha", "beta" };

        SparseFragmentRuntime
            .TryExplainSetProvenance(
                contributions,
                Optional<IEnumerable<string>?>.Present(effective),
                out var origins,
                out var reason
            )
            .ShouldBeTrue(reason);
        origins.Length.ShouldBe(2);

        // The shared "alpha"/"ALPHA" value must come from the first contribution.
        var effectiveOrder = effective.ToArray();
        for (var index = 0; index < effectiveOrder.Length; index++)
        {
            if (string.Equals(effectiveOrder[index], "alpha", StringComparison.OrdinalIgnoreCase))
            {
                origins[index].ShouldBe(0);
            }
            else
            {
                origins[index].ShouldBe(1);
            }
        }
    }

    [Test]
    public void SetProvenanceRejectsDifferingComparers()
    {
        var lower = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alpha" };
        var higher = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "beta" };
        var contributions = new Optional<IEnumerable<string>?>[]
        {
            Optional<IEnumerable<string>?>.Present(lower),
            Optional<IEnumerable<string>?>.Present(higher),
        };
        // The contributions merge under OrdinalIgnoreCase, so an Ordinal effective set
        // is a different value: the comparer is part of the set value (#5).
        var effective = new HashSet<string>(StringComparer.Ordinal) { "alpha", "beta" };

        SparseFragmentRuntime
            .TryExplainSetProvenance(
                contributions,
                Optional<IEnumerable<string>?>.Present(effective),
                out _,
                out var reason
            )
            .ShouldBeFalse();
        reason.ShouldNotBeNull();
        reason!.ShouldContain("comparer");
    }

    [Test]
    public void SetProvenanceSupportsResetAndMissing()
    {
        var contributions = new Optional<IEnumerable<string>?>[]
        {
            Optional<IEnumerable<string>?>.Present(["a"]),
            Optional<IEnumerable<string>?>.Present((IEnumerable<string>?)null),
            Optional<IEnumerable<string>?>.Present(["b"]),
        };

        SparseFragmentRuntime
            .TryExplainSetProvenance(
                contributions,
                Optional<IEnumerable<string>?>.Present(["b"]),
                out var origins,
                out _
            )
            .ShouldBeTrue();
        origins.ShouldBe([2]);

        var allMissing = new Optional<IEnumerable<string>?>[]
        {
            Optional<IEnumerable<string>?>.Missing,
            Optional<IEnumerable<string>?>.Missing,
        };
        SparseFragmentRuntime
            .TryExplainSetProvenance(
                allMissing,
                Optional<IEnumerable<string>?>.Missing,
                out var empty,
                out _
            )
            .ShouldBeTrue();
        empty.ShouldBeEmpty();
    }
}
