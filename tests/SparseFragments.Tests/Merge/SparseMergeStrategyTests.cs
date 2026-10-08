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
        Optional<List<string>> editBase,
        Optional<List<string>> desired,
        Optional<List<string>> current,
        out Optional<List<string>> rebased,
        out string? reason
    )
    {
        var currentValues = current.IsPresent ? current.Value ?? [] : [];
        var desiredValues = desired.IsPresent ? desired.Value ?? [] : [];
        var result = new List<string>(currentValues);
        foreach (var value in desiredValues)
        {
            if (!result.Contains(value))
            {
                result.Add(value);
            }
        }

        rebased =
            desired.IsPresent || current.IsPresent
                ? Optional<List<string>>.Present(result)
                : Optional<List<string>>.Missing;
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

public sealed class PresenceNoteStrategy : FragmentMergeStrategy<string?>
{
    public override Optional<string?> Merge(
        Optional<string?> lowerPriority,
        Optional<string?> higherPriority
    ) => higherPriority.IsPresent ? higherPriority : lowerPriority;

    public override bool AreEqual(string? left, string? right) => left == right;
}

[SparseFragmentModel]
public partial class PresenceNoteSettings
{
    [SparseMerge(typeof(PresenceNoteStrategy))]
    public string? Note { get; set; }
}

public sealed class PresenceCountStrategy : FragmentMergeStrategy<int>
{
    public override Optional<int> Merge(
        Optional<int> lowerPriority,
        Optional<int> higherPriority
    ) => higherPriority.IsPresent ? higherPriority : lowerPriority;

    public override bool AreEqual(int left, int right) => left == right;
}

[SparseFragmentModel]
public partial class PresenceCountSettings
{
    [SparseMerge(typeof(PresenceCountStrategy))]
    public int Count { get; set; }
}

public sealed class ObservingNoteStrategy : FragmentMergeStrategy<string?>
{
    public static Optional<string?> LastEditBase;
    public static Optional<string?> LastDesired;
    public static Optional<string?> LastCurrent;

    public override Optional<string?> Merge(
        Optional<string?> lowerPriority,
        Optional<string?> higherPriority
    ) => higherPriority.IsPresent ? higherPriority : lowerPriority;

    public override bool AreEqual(string? left, string? right) => left == right;

    public override bool TryRebase(
        Optional<string?> editBase,
        Optional<string?> desired,
        Optional<string?> current,
        out Optional<string?> rebased,
        out string? reason
    )
    {
        LastEditBase = editBase;
        LastDesired = desired;
        LastCurrent = current;
        return base.TryRebase(editBase, desired, current, out rebased, out reason);
    }
}

[SparseFragmentModel]
public partial class ObservedNoteSettings
{
    [SparseMerge(typeof(ObservingNoteStrategy))]
    public string? Note { get; set; }
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
        strategy
            .TryRebase(
                Optional<List<int>>.Present([1]),
                Optional<List<int>>.Present([1]),
                Optional<List<int>>.Present([5]),
                out var result,
                out _
            )
            .ShouldBeTrue();
        result.IsPresent.ShouldBeTrue();
        result.Value.ShouldBe([5]);
    }

    [Test]
    public void DefaultTryRebaseReconcilesSharedValuesAndReportsConflicts()
    {
        var strategy = new SumMergeStrategy();

        strategy
            .TryRebase(
                Optional<List<int>>.Present([1]),
                Optional<List<int>>.Present([9]),
                Optional<List<int>>.Present([1]),
                out var rebased,
                out var reason
            )
            .ShouldBeTrue();
        rebased.Value.ShouldBe([9]);
        reason.ShouldBeNull();

        strategy
            .TryRebase(
                Optional<List<int>>.Present([1]),
                Optional<List<int>>.Present([9]),
                Optional<List<int>>.Present([9]),
                out var alreadyApplied,
                out _
            )
            .ShouldBeTrue();
        alreadyApplied.Value.ShouldBe([9]);

        strategy
            .TryRebase(
                Optional<List<int>>.Present([1]),
                Optional<List<int>>.Present([9]),
                Optional<List<int>>.Present([5]),
                out _,
                out var conflictReason
            )
            .ShouldBeFalse();
        conflictReason.ShouldNotBeNull();
    }
}

public sealed class PresenceAwareCustomRebaseTests
{
    [Test]
    public void DefaultRebaseDistinguishesMissingFromPresentNull()
    {
        var strategy = new PresenceNoteStrategy();

        strategy
            .TryRebase(
                Optional<string?>.Missing,
                Optional<string?>.Present(null),
                Optional<string?>.Missing,
                out var rebased,
                out var reason
            )
            .ShouldBeTrue();
        reason.ShouldBeNull();
        rebased.IsPresent.ShouldBeTrue();
        rebased.Value.ShouldBeNull();
    }

    [Test]
    public void DefaultRebaseDistinguishesPresentNullFromMissing()
    {
        var strategy = new PresenceNoteStrategy();

        strategy
            .TryRebase(
                Optional<string?>.Present(null),
                Optional<string?>.Missing,
                Optional<string?>.Present(null),
                out var rebased,
                out _
            )
            .ShouldBeTrue();
        rebased.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void DefaultRebaseDistinguishesMissingFromPresentDefault()
    {
        var strategy = new PresenceCountStrategy();

        strategy
            .TryRebase(
                Optional<int>.Missing,
                Optional<int>.Present(0),
                Optional<int>.Missing,
                out var rebased,
                out _
            )
            .ShouldBeTrue();
        rebased.IsPresent.ShouldBeTrue();
        rebased.Value.ShouldBe(0);
    }

    [Test]
    public void DefaultRebaseConflictsOnConcurrentPresenceTransitions()
    {
        var note = new PresenceNoteStrategy();
        note.TryRebase(
                Optional<string?>.Missing,
                Optional<string?>.Present(null),
                Optional<string?>.Present("remote"),
                out _,
                out var firstReason
            )
            .ShouldBeFalse();
        firstReason.ShouldNotBeNull();

        note.TryRebase(
                Optional<string?>.Present(null),
                Optional<string?>.Missing,
                Optional<string?>.Present("remote"),
                out _,
                out var secondReason
            )
            .ShouldBeFalse();
        secondReason.ShouldNotBeNull();

        var count = new PresenceCountStrategy();
        count
            .TryRebase(
                Optional<int>.Missing,
                Optional<int>.Present(0),
                Optional<int>.Present(5),
                out _,
                out var countReason
            )
            .ShouldBeFalse();
        countReason.ShouldNotBeNull();
    }

    [Test]
    public void DefaultRebasePreservesCurrentForUnchangedLocalPresence()
    {
        var strategy = new PresenceNoteStrategy();

        strategy
            .TryRebase(
                Optional<string?>.Present("a"),
                Optional<string?>.Present("a"),
                Optional<string?>.Present("b"),
                out var rebased,
                out _
            )
            .ShouldBeTrue();
        rebased.IsPresent.ShouldBeTrue();
        rebased.Value.ShouldBe("b");

        strategy
            .TryRebase(
                Optional<string?>.Missing,
                Optional<string?>.Missing,
                Optional<string?>.Present("x"),
                out var missingRebased,
                out _
            )
            .ShouldBeTrue();
        missingRebased.IsPresent.ShouldBeTrue();
        missingRebased.Value.ShouldBe("x");
    }

    [Test]
    public void DefaultRebaseTreatsAlreadyAppliedPresenceAsNoOp()
    {
        var strategy = new PresenceNoteStrategy();

        strategy
            .TryRebase(
                Optional<string?>.Present("a"),
                Optional<string?>.Present("b"),
                Optional<string?>.Present("b"),
                out var rebased,
                out _
            )
            .ShouldBeTrue();
        rebased.IsPresent.ShouldBeTrue();
        rebased.Value.ShouldBe("b");

        strategy
            .TryRebase(
                Optional<string?>.Present("a"),
                Optional<string?>.Missing,
                Optional<string?>.Missing,
                out var missingRebased,
                out _
            )
            .ShouldBeTrue();
        missingRebased.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void GeneratedRebasePreservesMissingToPresentNull()
    {
        var baseState = NoteState(new PresenceNoteSettings.Fragment());
        var local = new PresenceNoteSettings.Patch { Note = (string?)null };
        var currentState = NoteState(new PresenceNoteSettings.Fragment());

        var result = PresenceNoteSettings.Patch.Rebase(baseState, local, currentState);

        result.HasConflicts.ShouldBeFalse();
        var applied = result.Rebased.Apply(currentState);
        applied.IsPresent.ShouldBeTrue();
        applied.Value!.Note.IsPresent.ShouldBeTrue();
        applied.Value!.Note.Value.ShouldBeNull();
    }

    [Test]
    public void GeneratedRebasePreservesPresentNullToMissing()
    {
        var baseState = NoteState(
            new PresenceNoteSettings.Fragment { Note = Optional<string?>.Present(null) }
        );
        var local = new PresenceNoteSettings.Patch { Note = FragmentOperation<string?>.Remove };
        var currentState = NoteState(
            new PresenceNoteSettings.Fragment { Note = Optional<string?>.Present(null) }
        );

        var result = PresenceNoteSettings.Patch.Rebase(baseState, local, currentState);

        result.HasConflicts.ShouldBeFalse();
        result.Rebased.Note.Kind.ShouldBe(FragmentOperationKind.Remove);
        var applied = result.Rebased.Apply(currentState);
        applied.Value!.Note.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void GeneratedRebasePreservesMissingToPresentDefault()
    {
        var baseState = CountState(new PresenceCountSettings.Fragment());
        var local = new PresenceCountSettings.Patch { Count = 0 };
        var currentState = CountState(new PresenceCountSettings.Fragment());

        var result = PresenceCountSettings.Patch.Rebase(baseState, local, currentState);

        result.HasConflicts.ShouldBeFalse();
        var applied = result.Rebased.Apply(currentState);
        applied.Value!.Count.IsPresent.ShouldBeTrue();
        applied.Value!.Count.Value.ShouldBe(0);
    }

    [Test]
    public void GeneratedRebaseConflictsOnConcurrentPresenceTransitions()
    {
        var baseState = NoteState(new PresenceNoteSettings.Fragment());
        var local = new PresenceNoteSettings.Patch { Note = (string?)null };
        var currentState = NoteState(
            new PresenceNoteSettings.Fragment { Note = Optional<string?>.Present("remote") }
        );

        var result = PresenceNoteSettings.Patch.Rebase(baseState, local, currentState);

        result.HasConflicts.ShouldBeTrue();
        var conflict = result.Conflicts.Single();
        conflict.Kind.ShouldBe(SparseConflictKind.CustomStrategy);
        conflict.BaseValue.IsPresent.ShouldBeFalse();
        conflict.LocalValue.IsPresent.ShouldBeTrue();
        conflict.LocalValue.Value.ShouldBeNull();
        conflict.CurrentValue.IsPresent.ShouldBeTrue();
    }

    [Test]
    public void GeneratedRebaseKeepsCurrentForUnchangedLocalPresence()
    {
        var baseState = NoteState(
            new PresenceNoteSettings.Fragment { Note = Optional<string?>.Present("a") }
        );
        var local = new PresenceNoteSettings.Patch { Note = "a" };
        var currentState = NoteState(
            new PresenceNoteSettings.Fragment { Note = Optional<string?>.Present("b") }
        );

        var result = PresenceNoteSettings.Patch.Rebase(baseState, local, currentState);

        result.HasConflicts.ShouldBeFalse();
        result.Rebased.Note.Kind.ShouldBe(FragmentOperationKind.Keep);
        result.Rebased.Apply(currentState).Value!.Note.Value.ShouldBe("b");
    }

    [Test]
    public void GeneratedRebaseTreatsAlreadyAppliedPresenceAsNoOp()
    {
        var baseState = NoteState(
            new PresenceNoteSettings.Fragment { Note = Optional<string?>.Present("a") }
        );
        var local = new PresenceNoteSettings.Patch { Note = "b" };
        var currentState = NoteState(
            new PresenceNoteSettings.Fragment { Note = Optional<string?>.Present("b") }
        );

        var result = PresenceNoteSettings.Patch.Rebase(baseState, local, currentState);

        result.HasConflicts.ShouldBeFalse();
        result.Rebased.Note.Kind.ShouldBe(FragmentOperationKind.Keep);
    }

    [Test]
    public void GeneratedRebasePassesPresenceThroughToCustomStrategy()
    {
        ObservingNoteStrategy.LastEditBase = Optional<string?>.Present("sentinel");
        ObservingNoteStrategy.LastDesired = Optional<string?>.Present("sentinel");
        ObservingNoteStrategy.LastCurrent = Optional<string?>.Present("sentinel");

        var baseState = ObservedState(new ObservedNoteSettings.Fragment());
        var local = new ObservedNoteSettings.Patch { Note = (string?)null };
        var currentState = ObservedState(new ObservedNoteSettings.Fragment());

        var result = ObservedNoteSettings.Patch.Rebase(baseState, local, currentState);

        result.HasConflicts.ShouldBeFalse();
        ObservingNoteStrategy.LastEditBase.IsPresent.ShouldBeFalse();
        ObservingNoteStrategy.LastDesired.IsPresent.ShouldBeTrue();
        ObservingNoteStrategy.LastDesired.Value.ShouldBeNull();
        ObservingNoteStrategy.LastCurrent.IsPresent.ShouldBeFalse();
    }

    private static Optional<PresenceNoteSettings.Fragment?> NoteState(
        PresenceNoteSettings.Fragment fragment
    ) => Optional<PresenceNoteSettings.Fragment?>.Present(fragment);

    private static Optional<PresenceCountSettings.Fragment?> CountState(
        PresenceCountSettings.Fragment fragment
    ) => Optional<PresenceCountSettings.Fragment?>.Present(fragment);

    private static Optional<ObservedNoteSettings.Fragment?> ObservedState(
        ObservedNoteSettings.Fragment fragment
    ) => Optional<ObservedNoteSettings.Fragment?>.Present(fragment);
}
