using SparseFragments;

namespace SparseFragments.Tests;

/// <summary>Merges divergent scalars by concatenating both sides.</summary>
public sealed class ConcatPolicy : FragmentRebasePolicy<string?>
{
    public override bool AreEqual(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    public override bool TryRebase(
        Optional<string?> editBase,
        Optional<string?> desired,
        Optional<string?> current,
        out Optional<string?> rebased,
        out string? reason
    )
    {
        if (!editBase.IsPresent || !desired.IsPresent || !current.IsPresent)
        {
            return FragmentRebasePolicy<string?>
                .FailOnConflict()
                .TryRebase(editBase, desired, current, out rebased, out reason);
        }

        if (AreEqual(desired.Value, editBase.Value))
        {
            rebased = current;
            reason = null;
            return true;
        }

        if (AreEqual(current.Value, editBase.Value) || AreEqual(current.Value, desired.Value))
        {
            rebased = desired;
            reason = null;
            return true;
        }

        rebased = Optional<string?>.Present(desired.Value + "|" + current.Value);
        reason = null;
        return true;
    }
}

/// <summary>Always reports a conflict with a fixed reason.</summary>
public sealed class AlwaysConflictPolicy : FragmentRebasePolicy<int>
{
    public override bool AreEqual(int left, int right) => left == right;

    public override bool TryRebase(
        Optional<int> editBase,
        Optional<int> desired,
        Optional<int> current,
        out Optional<int> rebased,
        out string? reason
    )
    {
        _ = editBase;
        _ = desired;
        _ = current;
        rebased = Optional<int>.Missing;
        reason = "The test policy always conflicts.";
        return false;
    }
}

/// <summary>Always reports a conflict so policy precedence is observable.</summary>
public sealed class AlwaysConflictStrategy : FragmentMergeStrategy<string?>
{
    public override Optional<string?> Merge(
        Optional<string?> lowerPriority,
        Optional<string?> higherPriority
    ) => higherPriority.IsPresent ? higherPriority : lowerPriority;

    public override bool AreEqual(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    public override bool TryRebase(
        Optional<string?> editBase,
        Optional<string?> desired,
        Optional<string?> current,
        out Optional<string?> rebased,
        out string? reason
    )
    {
        _ = editBase;
        _ = desired;
        _ = current;
        rebased = Optional<string?>.Missing;
        reason = "The test strategy always conflicts.";
        return false;
    }
}

[SparseFragmentModel]
public partial class RebasePolicyModel
{
    public string? Note { get; set; }

    [SparseRebasePolicy(typeof(ConcatPolicy))]
    public string? Tagged { get; set; }

    [SparseRebasePolicy(typeof(AlwaysConflictPolicy))]
    public int Count { get; set; }
}

[SparseFragmentModel]
public partial class RebasePrecedenceModel
{
    [SparseMerge(typeof(AlwaysConflictStrategy))]
    [SparseRebasePolicy(typeof(ConcatPolicy))]
    public string? Value { get; set; }
}

public sealed class SparseRebasePolicyTests
{
    [Test]
    public void CustomPolicy_ReconcilesWithoutMergeStrategy()
    {
        var before = new RebasePolicyModel
        {
            Note = "a",
            Tagged = "a",
            Count = 1,
        };
        var edited = new RebasePolicyModel
        {
            Note = "b",
            Tagged = "b",
            Count = 1,
        };
        var current = new RebasePolicyModel
        {
            Note = "a",
            Tagged = "c",
            Count = 1,
        };

        var applied = before.CreateChangeSet(edited).TryApplyTo(current, out var updated);

        applied.ShouldBeTrue();
        updated!.Note.ShouldBe("b");
        updated.Tagged.ShouldBe("b|c");
        updated.Count.ShouldBe(1);
    }

    [Test]
    public void CustomPolicy_ConflictKeepsCustomStrategyKindAndReason()
    {
        var before = new RebasePolicyModel { Count = 1 };
        var edited = new RebasePolicyModel { Count = 2 };
        var current = new RebasePolicyModel { Count = 3 };

        var applied = before
            .CreateChangeSet(edited)
            .TryApplyTo(current, out var updated, out var conflicts);

        applied.ShouldBeFalse();
        updated.ShouldBeNull();
        conflicts!.Count.ShouldBe(1);
        conflicts[0].Kind.ShouldBe(SparseConflictKind.CustomStrategy);
        conflicts[0].PathText.ShouldBe("Count");
        conflicts[0].Reason.ShouldBe("The test policy always conflicts.");
    }

    [Test]
    public void Policy_TakesPrecedenceOverMergeStrategyTryRebase()
    {
        var before = new RebasePrecedenceModel { Value = "a" };
        var edited = new RebasePrecedenceModel { Value = "b" };
        var current = new RebasePrecedenceModel { Value = "c" };

        var applied = before.CreateChangeSet(edited).TryApplyTo(current, out var updated);

        applied.ShouldBeTrue();
        updated!.Value.ShouldBe("b|c");
    }

    [Test]
    public void Merge_StillUsesMergeStrategyWhenPolicyPresent()
    {
        var lower = new RebasePrecedenceModel.Fragment
        {
            Value = Optional<string?>.Present("lower"),
        };
        var higher = new RebasePrecedenceModel.Fragment
        {
            Value = Optional<string?>.Present("higher"),
        };

        lower.Merge(higher).Value.Value.ShouldBe("higher");
    }

    [Test]
    public void PreferIncoming_OverwritesDivergenceWithoutConflict()
    {
        var before = new RebasePolicyModel
        {
            Note = "a",
            Tagged = "kept",
            Count = 1,
        };
        var edited = new RebasePolicyModel
        {
            Note = "b",
            Tagged = "kept",
            Count = 1,
        };
        var current = new RebasePolicyModel
        {
            Note = "c",
            Tagged = "kept",
            Count = 1,
        };
        var options = new ChangePayloadRebaseOptions
        {
            DefaultRebaseMode = SparseRebaseMode.PreferIncoming,
        };

        var applied = before
            .CreateChangeSet(edited)
            .TryApplyTo(current, out var updated, out var conflicts, options);

        applied.ShouldBeTrue();
        conflicts.ShouldBeNull();
        updated!.Note.ShouldBe("b");
    }

    [Test]
    public void PreferCurrent_DropsDivergenceWithoutConflict()
    {
        var before = new RebasePolicyModel
        {
            Note = "a",
            Tagged = "kept",
            Count = 1,
        };
        var edited = new RebasePolicyModel
        {
            Note = "b",
            Tagged = "kept",
            Count = 1,
        };
        var current = new RebasePolicyModel
        {
            Note = "c",
            Tagged = "kept",
            Count = 1,
        };
        var options = new ChangePayloadRebaseOptions
        {
            DefaultRebaseMode = SparseRebaseMode.PreferCurrent,
        };

        var applied = before
            .CreateChangeSet(edited)
            .TryApplyTo(current, out var updated, out var conflicts, options);

        applied.ShouldBeTrue();
        conflicts.ShouldBeNull();
        updated!.Note.ShouldBe("c");
    }

    [Test]
    public void FailOnConflict_MatchesBuiltInThreeWay()
    {
        var before = new RebasePolicyModel
        {
            Note = "a",
            Tagged = "kept",
            Count = 1,
        };
        var edited = new RebasePolicyModel
        {
            Note = "b",
            Tagged = "kept",
            Count = 1,
        };
        var current = new RebasePolicyModel
        {
            Note = "c",
            Tagged = "kept",
            Count = 1,
        };
        var options = new ChangePayloadRebaseOptions
        {
            DefaultRebaseMode = SparseRebaseMode.FailOnConflict,
        };

        var applied = before
            .CreateChangeSet(edited)
            .TryApplyTo(current, out var updated, out var conflicts, options);

        applied.ShouldBeFalse();
        updated.ShouldBeNull();
        conflicts!.Count.ShouldBe(1);
        conflicts[0].Kind.ShouldBe(SparseConflictKind.Scalar);
        conflicts[0].PathText.ShouldBe("Note");
    }

    [Test]
    public void CallerMode_DoesNotOverrideExplicitPolicy()
    {
        var before = new RebasePolicyModel { Note = "kept", Tagged = "a" };
        var edited = new RebasePolicyModel { Note = "kept", Tagged = "b" };
        var current = new RebasePolicyModel { Note = "kept", Tagged = "c" };
        var options = new ChangePayloadRebaseOptions
        {
            DefaultRebaseMode = SparseRebaseMode.PreferCurrent,
        };

        var applied = before
            .CreateChangeSet(edited)
            .TryApplyTo(current, out var updated, out var conflicts, options);

        applied.ShouldBeTrue();
        conflicts.ShouldBeNull();
        updated!.Tagged.ShouldBe("b|c");
    }

    [Test]
    public void CallerMode_DoesNotOverrideMergeStrategy()
    {
        var before = new StrategySettings { Values = [1] };
        var edited = new StrategySettings { Values = [2] };
        var current = new StrategySettings { Values = [3] };
        var options = new ChangePayloadRebaseOptions
        {
            DefaultRebaseMode = SparseRebaseMode.PreferIncoming,
        };

        var applied = before
            .CreateChangeSet(edited)
            .TryApplyTo(current, out var updated, out var conflicts, options);

        applied.ShouldBeFalse();
        updated.ShouldBeNull();
        conflicts!.Count.ShouldBe(1);
        conflicts[0].Kind.ShouldBe(SparseConflictKind.CustomStrategy);
    }

    [Test]
    public void BuiltInPolicies_AgreeOnCleanReplay()
    {
        var policy = FragmentRebasePolicy<string?>.FailOnConflict();
        var @base = Optional<string?>.Present("a");
        var desired = Optional<string?>.Present("b");
        var current = Optional<string?>.Present("a");

        policy.TryRebase(@base, desired, current, out var replayed, out _).ShouldBeTrue();
        replayed.ShouldBe(desired);

        FragmentRebasePolicy<string?>
            .PreferIncoming()
            .TryRebase(@base, desired, current, out var incoming, out _)
            .ShouldBeTrue();
        incoming.ShouldBe(desired);

        FragmentRebasePolicy<string?>
            .PreferCurrent()
            .TryRebase(@base, desired, current, out var kept, out _)
            .ShouldBeTrue();
        kept.ShouldBe(current);
    }

    [Test]
    public void PatchRebase_UsesPolicyForScalarMembers()
    {
        var baseState = Optional<RebasePolicyModel.Fragment?>.Present(
            RebasePolicyModel.Fragment.From(new RebasePolicyModel { Tagged = "a" })
        );
        var currentState = Optional<RebasePolicyModel.Fragment?>.Present(
            RebasePolicyModel.Fragment.From(new RebasePolicyModel { Tagged = "c" })
        );
        var local = new RebasePolicyModel.Patch { Tagged = "b" };

        var result = RebasePolicyModel.Patch.Rebase(baseState, local, currentState);

        result.HasConflicts.ShouldBeFalse();
        result.Rebased.Apply(currentState).Value!.Tagged.Value.ShouldBe("b|c");
    }
}
