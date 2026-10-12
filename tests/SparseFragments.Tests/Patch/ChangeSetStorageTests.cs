using System.Text.Json;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class CompactStorageRecord
{
    public string? Text { get; set; }

    public int? Count { get; set; }

    public decimal Amount { get; set; }

    public string __sparse_wholeBeforeValue { get; set; } = "";

    public string __sparse_wholeBeforeIsPresent { get; set; } = "";

    public string __sparse_before_0Value { get; set; } = "";
}

public sealed class ChangeSetStorageTests
{
    [Test]
    public void ScalarTransitionsDistinguishMissingPresentNullAndDefaultValues()
    {
        var before = Optional<CompactStorageRecord.Fragment?>.Present(new());
        var after = Optional<CompactStorageRecord.Fragment?>.Present(
            new()
            {
                Text = Optional<string?>.Present(null),
                Count = Optional<int?>.Present(null),
                Amount = 0m,
            }
        );
        var change = CompactStorageRecord.ChangeSet.Between(before, after);
        change.Text.Before.IsPresent.ShouldBeFalse();
        change.Text.After.IsPresent.ShouldBeTrue();
        change.Text.After.Value.ShouldBeNull();
        change.Count.Before.IsPresent.ShouldBeFalse();
        change.Count.After.IsPresent.ShouldBeTrue();
        change.Count.After.Value.ShouldBeNull();
        change.Amount.Before.IsPresent.ShouldBeFalse();
        change.Amount.After.IsPresent.ShouldBeTrue();
        change.Amount.After.Value.ShouldBe(0m);
    }

    [Test]
    public void MissingNullAndValueEndpointsSurviveOperationsAndPayloads()
    {
        Optional<CompactStorageRecord.Fragment?>[] states =
        [
            default,
            Optional<CompactStorageRecord.Fragment?>.Present(null),
            Optional<CompactStorageRecord.Fragment?>.Present(new()),
            Optional<CompactStorageRecord.Fragment?>.Present(
                new()
                {
                    Text = Optional<string?>.Present(null),
                    Count = Optional<int?>.Present(null),
                    Amount = 0m,
                }
            ),
            Optional<CompactStorageRecord.Fragment?>.Present(
                new()
                {
                    Text = "value",
                    Count = 42,
                    Amount = decimal.MaxValue,
                }
            ),
        ];
        foreach (var before in states)
        foreach (var after in states)
        {
            var change = CompactStorageRecord.ChangeSet.Between(before, after);
            RequireState(change.ToPatch().Apply(before), after);
            RequireState(change.Invert().ToPatch().Apply(after), before);
            var json = JsonSerializer.Serialize(change.ToPayload());
            var restored = JsonSerializer
                .Deserialize<CompactStorageRecord.ChangePayload>(json)!
                .ToChangeSet();
            RequireState(restored.ToPatch().Apply(before), after);
            foreach (var final in states)
            {
                var next = CompactStorageRecord.ChangeSet.Between(after, final);
                RequireState(change.Compose(next).ToPatch().Apply(before), final);
            }
        }
    }

    [Test]
    public void StorageNamesDoNotShadowPublicTransitions()
    {
        var before = new CompactStorageRecord
        {
            __sparse_wholeBeforeValue = "a",
            __sparse_wholeBeforeIsPresent = "b",
            __sparse_before_0Value = "c",
        };
        var after = new CompactStorageRecord
        {
            __sparse_wholeBeforeValue = "A",
            __sparse_wholeBeforeIsPresent = "B",
            __sparse_before_0Value = "C",
        };
        var change = CompactStorageRecord.ChangeSet.Between(before, after);
        change.__sparse_wholeBeforeValue.Before.Value.ShouldBe("a");
        change.__sparse_wholeBeforeValue.After.Value.ShouldBe("A");
        change.__sparse_wholeBeforeIsPresent.Before.Value.ShouldBe("b");
        change.__sparse_wholeBeforeIsPresent.After.Value.ShouldBe("B");
        change.__sparse_before_0Value.Before.Value.ShouldBe("c");
        change.__sparse_before_0Value.After.Value.ShouldBe("C");
        change.ToPatch().ApplyTo(before).__sparse_before_0Value.ShouldBe("C");
    }

    private static void RequireState(
        Optional<CompactStorageRecord.Fragment?> actual,
        Optional<CompactStorageRecord.Fragment?> expected
    )
    {
        actual.IsPresent.ShouldBe(expected.IsPresent);
        if (expected.IsPresent && expected.Value is null)
            actual.Value.ShouldBeNull();
        CompactStorageRecord.Patch.Between(actual, expected).IsEmpty.ShouldBeTrue();
    }
}
