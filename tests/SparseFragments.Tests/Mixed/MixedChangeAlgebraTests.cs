using SparseFragments;

namespace SparseFragments.Tests.Mixed;

/// <summary>Mixed-operation descriptor algebra (issue #119).</summary>
/// <remarks>
/// Table-driven proof of the deterministic composition, rollback, and projection
/// rules. Descriptors carry paths only, so these tests also pin the secrecy
/// boundary: no rule consumes or produces secret values.
/// </remarks>
public sealed class MixedChangeAlgebraTests
{
    private static MixedMemberOperation Transition(
        string path,
        MixedAfterKind after = MixedAfterKind.Value
    ) => new(path, MixedHistoryKind.Transition, after);

    private static MixedMemberOperation Blind(
        string path,
        MixedAfterKind after = MixedAfterKind.Value
    ) => new(path, MixedHistoryKind.BlindSet, after);

    private static readonly (
        MixedMemberOperation First,
        MixedMemberOperation Second,
        MixedHistoryKind History,
        MixedAfterKind After,
        bool Check
    )[] ComposeTable =
    [
        // Transition then transition keeps continuity checks.
        (
            Transition("Label"),
            Transition("Label"),
            MixedHistoryKind.Transition,
            MixedAfterKind.Value,
            true
        ),
        // Transition then blind set keeps the real baseline without further checks.
        (
            Transition("Label"),
            Blind("Label"),
            MixedHistoryKind.Transition,
            MixedAfterKind.Value,
            false
        ),
        (
            Transition("Label"),
            Blind("Label", MixedAfterKind.Null),
            MixedHistoryKind.Transition,
            MixedAfterKind.Null,
            false
        ),
        (
            Transition("Label"),
            Blind("Label", MixedAfterKind.Missing),
            MixedHistoryKind.Transition,
            MixedAfterKind.Missing,
            false
        ),
        // Blind set then transition stays blind and still needs a value-level check.
        (
            Blind("Label"),
            Transition("Label"),
            MixedHistoryKind.BlindSet,
            MixedAfterKind.Value,
            true
        ),
        (
            Blind("Label", MixedAfterKind.Null),
            Transition("Label", MixedAfterKind.Missing),
            MixedHistoryKind.BlindSet,
            MixedAfterKind.Missing,
            true
        ),
        // Blind set then blind set stays blind, including null and removal vocabulary.
        (Blind("Label"), Blind("Label"), MixedHistoryKind.BlindSet, MixedAfterKind.Value, false),
        (
            Blind("Label"),
            Blind("Label", MixedAfterKind.Null),
            MixedHistoryKind.BlindSet,
            MixedAfterKind.Null,
            false
        ),
        (
            Blind("Label", MixedAfterKind.Value),
            Blind("Label", MixedAfterKind.Missing),
            MixedHistoryKind.BlindSet,
            MixedAfterKind.Missing,
            false
        ),
        // Nested, keyed, and dictionary paths follow the same cells.
        (
            Transition("Nested.Host"),
            Blind("Nested.Host"),
            MixedHistoryKind.Transition,
            MixedAfterKind.Value,
            false
        ),
        (
            Blind("Items"),
            Transition("Items"),
            MixedHistoryKind.BlindSet,
            MixedAfterKind.Value,
            true
        ),
        (
            Transition("Scores"),
            Transition("Scores"),
            MixedHistoryKind.Transition,
            MixedAfterKind.Value,
            true
        ),
        (
            Blind("Scores"),
            Blind("Scores", MixedAfterKind.Missing),
            MixedHistoryKind.BlindSet,
            MixedAfterKind.Missing,
            false
        ),
    ];

    [Test]
    public void SamePathCompositionFollowsTheTable()
    {
        foreach (var row in ComposeTable)
        {
            var outcome = MixedChangeAlgebra.Compose(row.First, row.Second);
            outcome.Succeeded.ShouldBeTrue(
                $"compose {Describe(row.First)} then {Describe(row.Second)}"
            );
            outcome.Path.ShouldBe(row.First.Path);
            outcome.ResultHistory.ShouldBe(row.History);
            outcome.ResultAfter.ShouldBe(row.After);
            outcome.RequiresContinuityCheck.ShouldBe(row.Check);
            outcome.ResultIsWholeRoot.ShouldBeFalse();
            outcome.FailureReason.ShouldBeNull();
        }
    }

    [Test]
    public void MismatchedPathsFailWithReason()
    {
        var outcome = MixedChangeAlgebra.Compose(Transition("Label"), Transition("RetryCount"));
        outcome.Succeeded.ShouldBeFalse();
        outcome.FailureReason.ShouldNotBeNullOrEmpty();
        outcome.FailureReason.ShouldContain("Label");
    }

    [Test]
    public void WholeRootCompositionRules()
    {
        var wholeBlind = new MixedMemberOperation(
            "$root",
            MixedHistoryKind.BlindSet,
            MixedAfterKind.Value,
            true
        );
        var wholeTransition = new MixedMemberOperation(
            "$root",
            MixedHistoryKind.Transition,
            MixedAfterKind.Value,
            true
        );

        // A trailing blind whole-root set overwrites everything before it.
        var overwrite = MixedChangeAlgebra.Compose(Transition("Label"), wholeBlind);
        overwrite.Succeeded.ShouldBeTrue();
        overwrite.ResultIsWholeRoot.ShouldBeTrue();
        overwrite.ResultHistory.ShouldBe(MixedHistoryKind.BlindSet);
        overwrite.RequiresContinuityCheck.ShouldBeFalse();

        // A leading blind whole-root set absorbs memberwise operations that follow.
        var absorb = MixedChangeAlgebra.Compose(wholeBlind, Blind("Label", MixedAfterKind.Missing));
        absorb.Succeeded.ShouldBeTrue();
        absorb.ResultIsWholeRoot.ShouldBeTrue();
        absorb.ResultHistory.ShouldBe(MixedHistoryKind.BlindSet);
        absorb.ResultAfter.ShouldBe(MixedAfterKind.Value);
        absorb.RequiresContinuityCheck.ShouldBeFalse();

        // Blind whole then whole blind keeps the trailing after-state.
        var wholeThenWhole = MixedChangeAlgebra.Compose(wholeBlind, wholeBlind);
        wholeThenWhole.Succeeded.ShouldBeTrue();
        wholeThenWhole.ResultIsWholeRoot.ShouldBeTrue();

        // Whole-root transitions keep history but still need value-level checks.
        var wholeContinuity = MixedChangeAlgebra.Compose(wholeTransition, wholeTransition);
        wholeContinuity.Succeeded.ShouldBeTrue();
        wholeContinuity.ResultHistory.ShouldBe(MixedHistoryKind.Transition);
        wholeContinuity.RequiresContinuityCheck.ShouldBeTrue();

        // Memberwise then whole-root transition regains history from the real baseline.
        var regain = MixedChangeAlgebra.Compose(Blind("Label"), wholeTransition);
        regain.Succeeded.ShouldBeTrue();
        regain.ResultIsWholeRoot.ShouldBeTrue();
        regain.ResultHistory.ShouldBe(MixedHistoryKind.Transition);
        regain.RequiresContinuityCheck.ShouldBeTrue();

        // Blind whole then whole transition cannot be justified without values.
        var unjustified = MixedChangeAlgebra.Compose(wholeBlind, wholeTransition);
        unjustified.Succeeded.ShouldBeFalse();
        unjustified.FailureReason.ShouldNotBeNullOrEmpty();
    }

    [Test]
    public void SequencesMergeDisjointPathsAndComposeOverlaps()
    {
        var first = new[] { Transition("Label"), Blind("Secret"), Transition("Nested.Host") };
        var second = new[]
        {
            // Disjoint nested leaf under a shared parent.
            Transition("Nested.Port"),
            // Overlapping blind path: blind then transition stays blind with a check.
            Transition("Secret", MixedAfterKind.Null),
            // Disjoint keyed and dictionary edits.
            Blind("Items"),
            Transition("Scores"),
        };

        var composed = MixedChangeAlgebra.ComposeSequences(first, second);

        composed.Succeeded.ShouldBeTrue();
        composed.Failures.ShouldBeEmpty();
        composed
            .Composed.Select(static operation => operation.Path)
            .ShouldBe(["Label", "Secret", "Nested.Host", "Nested.Port", "Items", "Scores"]);
        var secret = composed.Composed.Single(static operation => operation.Path == "Secret");
        secret.History.ShouldBe(MixedHistoryKind.BlindSet);
        secret.After.ShouldBe(MixedAfterKind.Null);
    }

    [Test]
    public void SequencesReportOverlappingFailuresWithoutLosingDisjointPaths()
    {
        var wholeBlind = new MixedMemberOperation(
            "$root",
            MixedHistoryKind.BlindSet,
            MixedAfterKind.Value,
            true
        );
        var wholeTransition = new MixedMemberOperation(
            "$root",
            MixedHistoryKind.Transition,
            MixedAfterKind.Value,
            true
        );
        var first = new[] { wholeBlind, Transition("Kept") };
        var second = new[] { wholeTransition, Blind("Other") };

        var composed = MixedChangeAlgebra.ComposeSequences(first, second);

        composed.Succeeded.ShouldBeFalse();
        composed.Failures.ShouldHaveSingleItem();
        composed.Failures[0].Path.ShouldBe("$root");
        // Disjoint paths still merge; only the unjustified overlap is reported.
        composed.Composed.Select(static operation => operation.Path).ShouldBe(["Kept", "Other"]);
    }

    [Test]
    public void RollbackPlanInvertsTransitionsAndSkipsBlindSets()
    {
        var plan = MixedChangeAlgebra.CreateRollbackPlan([
            Transition("Label"),
            Blind("Secret"),
            Transition("Nested.Host"),
            Blind("Items"),
            new MixedMemberOperation(
                "$root",
                MixedHistoryKind.BlindSet,
                MixedAfterKind.Value,
                true
            ),
        ]);

        plan.IsComplete.ShouldBeFalse();
        plan.ReversiblePaths.ShouldBe(["Label", "Nested.Host"]);
        plan.SkippedPaths.ShouldBe(["Secret", "Items", "$root"]);
    }

    [Test]
    public void RollbackPlanIsCompleteWithoutBlindSets()
    {
        var plan = MixedChangeAlgebra.CreateRollbackPlan([
            Transition("Label"),
            Transition("Scores"),
        ]);

        plan.IsComplete.ShouldBeTrue();
        plan.ReversiblePaths.ShouldBe(["Label", "Scores"]);
        plan.SkippedPaths.ShouldBeEmpty();
    }

    [Test]
    public void ChangeSetGateFailsOnlyOnBlindPaths()
    {
        MixedChangeAlgebra.CanFormChangeSet([Transition("Label")], out var clean).ShouldBeTrue();
        clean.ShouldBeEmpty();

        MixedChangeAlgebra
            .CanFormChangeSet([Transition("Label"), Blind("Secret")], out var blind)
            .ShouldBeFalse();
        blind.ShouldBe(["Secret"]);
    }

    [Test]
    public void DuplicatePathsWithinOneSequenceFoldInOrder()
    {
        // Transition then blind keeps the real baseline without further checks.
        var folded = MixedChangeAlgebra.ComposeSequences([Transition("Label"), Blind("Label")], []);
        folded.Succeeded.ShouldBeTrue();
        folded.Composed.ShouldHaveSingleItem();
        folded.Composed[0].History.ShouldBe(MixedHistoryKind.Transition);
    }

    [Test]
    public void DuplicateRootEntriesFoldInsteadOfKeepingLast()
    {
        var wholeBlind = new MixedMemberOperation(
            "$root",
            MixedHistoryKind.BlindSet,
            MixedAfterKind.Value,
            true
        );
        var folded = MixedChangeAlgebra.ComposeSequences([wholeBlind, wholeBlind], []);
        folded.Succeeded.ShouldBeTrue();
        folded.Composed.ShouldHaveSingleItem();
        folded.Composed[0].IsWholeRoot.ShouldBeTrue();
    }

    [Test]
    public void TrailingBlindRootSupersedesPriorMembers()
    {
        var wholeBlind = new MixedMemberOperation(
            "$root",
            MixedHistoryKind.BlindSet,
            MixedAfterKind.Value,
            true
        );
        var composed = MixedChangeAlgebra.ComposeSequences(
            [Transition("Label"), Transition("Nested.Host")],
            [wholeBlind]
        );
        composed.Succeeded.ShouldBeTrue();
        composed.Composed.ShouldHaveSingleItem();
        composed.Composed[0].IsWholeRoot.ShouldBeTrue();
    }

    [Test]
    public void LeadingBlindRootAbsorbsLaterMembers()
    {
        var wholeBlind = new MixedMemberOperation(
            "$root",
            MixedHistoryKind.BlindSet,
            MixedAfterKind.Value,
            true
        );
        var composed = MixedChangeAlgebra.ComposeSequences(
            [wholeBlind],
            [Blind("Label"), Blind("Other")]
        );
        composed.Succeeded.ShouldBeTrue();
        composed.Composed.ShouldHaveSingleItem();
        composed.Composed[0].IsWholeRoot.ShouldBeTrue();
    }

    [Test]
    public void WholeRootTransitionNeedsContinuity()
    {
        var wholeTransition = new MixedMemberOperation(
            "$root",
            MixedHistoryKind.Transition,
            MixedAfterKind.Value,
            true
        );
        var wholeBlind = new MixedMemberOperation(
            "$root",
            MixedHistoryKind.BlindSet,
            MixedAfterKind.Value,
            true
        );
        var unjustified = MixedChangeAlgebra.ComposeSequences([wholeBlind], [wholeTransition]);
        unjustified.Succeeded.ShouldBeFalse();
        unjustified.Failures.ShouldHaveSingleItem();
    }

    [Test]
    public void AncestorBlindOverwriteSupersedesDescendant()
    {
        // Trailing whole-member blind write supersedes a prior child edit.
        var composed = MixedChangeAlgebra.ComposeSequences(
            [Blind("Items[\"k\"].Name")],
            [Blind("Items[\"k\"]")]
        );
        composed.Succeeded.ShouldBeTrue();
        composed.Composed.ShouldHaveSingleItem();
        composed.Composed[0].Path.ShouldBe("Items[\"k\"]");
    }

    [Test]
    public void LeadingBlindAncestorAbsorbsTrailingChild()
    {
        var composed = MixedChangeAlgebra.ComposeSequences(
            [Blind("Nested")],
            [Blind("Nested.Host")]
        );
        composed.Succeeded.ShouldBeTrue();
        composed.Composed.ShouldHaveSingleItem();
        composed.Composed[0].Path.ShouldBe("Nested");
    }

    [Test]
    public void AncestorTransitionOverlapFailsWithoutInventingValues()
    {
        var composed = MixedChangeAlgebra.ComposeSequences(
            [Transition("Nested")],
            [Transition("Nested.Host")]
        );
        composed.Succeeded.ShouldBeFalse();
        composed.Failures.ShouldHaveSingleItem();
        composed.Failures[0].FailureReason.ShouldContain("Nested");
    }

    [Test]
    public void SiblingPathsStayDisjoint()
    {
        MixedChangeAlgebra.IsAncestorOrDescendant("A", "AB").ShouldBeFalse();
        MixedChangeAlgebra.IsAncestorOrDescendant("Nested.Host", "Nested.Port").ShouldBeFalse();
        var composed = MixedChangeAlgebra.ComposeSequences(
            [Blind("Nested.Host")],
            [Blind("Nested.Port")]
        );
        composed.Succeeded.ShouldBeTrue();
        composed.Composed.Count.ShouldBe(2);
    }

    [Test]
    public void PolicySeamDefaultsToPassthroughAndFailsClosed()
    {
        MixedChangeAlgebra.EnsurePassthrough(RedactedBeforePolicy.Passthrough);
        Should.Throw<ArgumentOutOfRangeException>(() =>
            MixedChangeAlgebra.EnsurePassthrough((RedactedBeforePolicy)99, "policy")
        );
    }

    private static string Describe(MixedMemberOperation operation) =>
        operation.History + ":" + operation.Path + "->" + operation.After;
}
