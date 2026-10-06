using SparseFragments;

namespace SparseFragments.Tests;

public sealed class DistinctUnionMergeStrategy : FragmentMergeStrategy<List<string>>
{
    public override Optional<List<string>> Merge(
        Optional<List<string>> lowerPriority,
        Optional<List<string>> higherPriority
    )
    {
        if (!higherPriority.IsPresent)
        {
            return lowerPriority;
        }

        if (!lowerPriority.IsPresent)
        {
            return higherPriority;
        }

        var merged = new List<string>(lowerPriority.Value!);
        foreach (var value in higherPriority.Value!)
        {
            if (!merged.Contains(value))
            {
                merged.Add(value);
            }
        }

        return Optional<List<string>>.Present(merged);
    }

    public override bool AreEqual(List<string>? left, List<string>? right) =>
        (left is null && right is null)
        || (left is not null && right is not null && left.SequenceEqual(right));

    public override bool TryRebase(
        List<string>? editBase,
        List<string>? desired,
        List<string>? current,
        out List<string>? rebased,
        out string? reason
    )
    {
        var result = current is null ? [] : new List<string>(current);
        foreach (var value in desired ?? [])
        {
            if (!result.Contains(value))
            {
                result.Add(value);
            }
        }

        rebased = result;
        reason = null;
        return true;
    }
}

[SparseFragmentModel]
public partial class TraceSettings
{
    [SparseMerge(typeof(DistinctUnionMergeStrategy))]
    public List<string> Tags { get; set; } = [];
}

[SparseFragmentModel]
public partial class ReplaceTraceSettings
{
    public List<string> Values { get; set; } = [];
}

public sealed class SparseMergeStrategyTests
{
    [Test]
    public void CustomMergeStrategyMergesDistinctValues()
    {
        var lower = new TraceSettings.Fragment
        {
            Tags = Optional<List<string>>.Present(["alpha", "beta"]),
        };
        var higher = new TraceSettings.Fragment
        {
            Tags = Optional<List<string>>.Present(["beta", "gamma"]),
        };

        lower.Merge(higher).ToModel().Tags.ShouldBe(["alpha", "beta", "gamma"]);
    }

    [Test]
    public void DefaultRebasePreservesCurrentForAnUnchangedLocalValue()
    {
        var strategy = new SumMergeStrategy();
        strategy.TryRebase([1], [1], [5], out var result, out _).ShouldBeTrue();
        result.ShouldBe([5]);
    }

    [Test]
    public void DefaultTryRebaseReconcilesSharedValuesAndReportsConflicts()
    {
        var strategy = new SumMergeStrategy();

        strategy.TryRebase([1], [9], [1], out var rebased, out var reason).ShouldBeTrue();
        rebased.ShouldBe([9]);
        reason.ShouldBeNull();

        strategy.TryRebase([1], [9], [9], out var alreadyApplied, out _).ShouldBeTrue();
        alreadyApplied.ShouldBe([9]);

        strategy.TryRebase([1], [9], [5], out _, out var conflictReason).ShouldBeFalse();
        conflictReason.ShouldNotBeNull();
    }
}
