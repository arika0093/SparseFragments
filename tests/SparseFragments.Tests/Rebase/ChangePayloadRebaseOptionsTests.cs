using System.Text.Json;
using SparseFragments;

namespace SparseFragments.Tests;

/// <summary>Covers the strict redacted-before opt-in and default passthrough.</summary>
/// <remarks>
/// Redacted paths are caller-declared until the wire format learns them; the
/// wire version stays "0.1" throughout these tests.
/// </remarks>
public sealed class ChangePayloadRebaseOptionsTests
{
    private static ChangePayloadRebaseOptions PassThrough(params string[] paths) =>
        new() { RedactedBeforePaths = paths };

    private static ChangePayloadRebaseOptions Strict(params string[] paths) =>
        new()
        {
            RejectChangesWithRedactedBeforeValuesDuringRebase = true,
            RedactedBeforePaths = paths,
        };

    [Test]
    public void Options_DefaultToPassthrough()
    {
        var options = new ChangePayloadRebaseOptions();
        options.RejectChangesWithRedactedBeforeValuesDuringRebase.ShouldBeFalse();
        options.RedactedBeforePaths.Count.ShouldBe(0);
        options.DefaultRebaseMode.ShouldBe(SparseRebaseMode.Default);
        options.IsRedactedBefore("Label").ShouldBeFalse();
        options.IsRedactedBefore("").ShouldBeFalse();
    }

    [Test]
    public void IsRedactedBefore_MatchesExactAndSubtreePaths()
    {
        var options = PassThrough("Nested", "Label");
        options.IsRedactedBefore("Label").ShouldBeTrue();
        options.IsRedactedBefore("Nested").ShouldBeTrue();
        options.IsRedactedBefore("Nested.Host").ShouldBeTrue();
        options.IsRedactedBefore("NestedHost").ShouldBeFalse();
        options.IsRedactedBefore("RetryCount").ShouldBeFalse();
        PassThrough("").IsRedactedBefore("Anything.At.All").ShouldBeTrue();
    }

    [Test]
    public void RedactedScalar_PassesThroughByDefault()
    {
        var before = new Settings { Label = "a", RetryCount = 1 };
        var edited = new Settings { Label = "secret", RetryCount = 2 };
        var current = new Settings { Label = "other", RetryCount = 1 };

        var changes = before.CreateChangeSet(edited);
        var applied = changes.TryApplyTo(
            current,
            out var updated,
            out var conflicts,
            PassThrough("Label")
        );

        applied.ShouldBeTrue();
        conflicts.ShouldBeNull();
        updated!.Label.ShouldBe("secret");
        updated.RetryCount.ShouldBe(2);
    }

    [Test]
    public void RedactedScalar_StrictFailsSecretSafeWithoutPartialApply()
    {
        var before = new Settings { Label = "a", RetryCount = 1 };
        var edited = new Settings { Label = "secret", RetryCount = 2 };
        var current = new Settings { Label = "other", RetryCount = 1 };
        var options = Strict("Label");

        var changes = before.CreateChangeSet(edited);
        var applied = changes.TryApplyTo(current, out var updated, out var conflicts, options);

        applied.ShouldBeFalse();
        updated.ShouldBeNull();
        conflicts!.Count.ShouldBe(1);
        var conflict = conflicts[0];
        conflict.Kind.ShouldBe(SparseConflictKind.RedactedBefore);
        conflict.PathText.ShouldBe("Label");
        conflict.BaseValue.IsPresent.ShouldBeFalse();
        conflict.LocalValue.IsPresent.ShouldBeFalse();
        conflict.CurrentValue.IsPresent.ShouldBeFalse();
        (conflict.Reason ?? string.Empty).ShouldNotContain("secret");

        var rebased = changes.RebaseOnto(current, options);
        rebased.HasConflicts.ShouldBeTrue();
        var kept = rebased
            .Rebased.ToPatch()
            .Apply(Settings.Fragment.From(current))
            .Value!.ToModel();
        kept.Label.ShouldBe("other");
        kept.RetryCount.ShouldBe(2);
    }

    [Test]
    public void Strict_MixedRedactedAndConflicting_FailsAtomically()
    {
        var before = new Settings { Label = "a", RetryCount = 1 };
        var edited = new Settings { Label = "secret", RetryCount = 2 };
        var current = new Settings { Label = "other", RetryCount = 3 };

        var applied = before
            .CreateChangeSet(edited)
            .TryApplyTo(current, out var updated, out var conflicts, Strict("Label"));

        applied.ShouldBeFalse();
        updated.ShouldBeNull();
        conflicts!.Count.ShouldBe(2);
        conflicts[0].Kind.ShouldBe(SparseConflictKind.RedactedBefore);
        conflicts[0].PathText.ShouldBe("Label");
        conflicts[1].Kind.ShouldBe(SparseConflictKind.Scalar);
        conflicts[1].PathText.ShouldBe("RetryCount");
    }

    [Test]
    public void RedactedRemoval_PassesThroughMissingDesired()
    {
        var before = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment { Label = Optional<string?>.Present("a") }
        );
        var after = Optional<Settings.Fragment?>.Present(new Settings.Fragment());
        var current = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment { Label = Optional<string?>.Present("b") }
        );

        var changes = Settings.ChangeSet.Between(before, after);
        var rebased = changes.RebaseOnto(current, PassThrough("Label"));

        rebased.HasConflicts.ShouldBeFalse();
        rebased.Rebased.ToPatch().Apply(current).Value!.Label.IsPresent.ShouldBeFalse();

        var strict = changes.RebaseOnto(current, Strict("Label"));
        strict.HasConflicts.ShouldBeTrue();
        strict.Conflicts.Single().Kind.ShouldBe(SparseConflictKind.RedactedBefore);
    }

    [Test]
    public void RedactedNull_PassesThroughPresentNullDesired()
    {
        var before = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment { Label = Optional<string?>.Present("a") }
        );
        var after = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment { Label = Optional<string?>.Present(null) }
        );
        var current = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment { Label = Optional<string?>.Present("b") }
        );

        var rebased = Settings
            .ChangeSet.Between(before, after)
            .RebaseOnto(current, PassThrough("Label"));

        rebased.HasConflicts.ShouldBeFalse();
        var applied = rebased.Rebased.ToPatch().Apply(current).Value!;
        applied.Label.IsPresent.ShouldBeTrue();
        applied.Label.Value.ShouldBeNull();
    }

    [Test]
    public void RedactedNested_PassesThroughWholeSubtreeByDefault()
    {
        var before = new Settings
        {
            Nested = new Nested { Host = "a", Port = 1 },
        };
        var edited = new Settings
        {
            Nested = new Nested { Host = "b", Port = 2 },
        };
        var current = new Settings
        {
            Nested = new Nested { Host = "c", Port = 1 },
        };

        var applied = before
            .CreateChangeSet(edited)
            .TryApplyTo(current, out var updated, out var conflicts, PassThrough("Nested"));

        applied.ShouldBeTrue();
        conflicts.ShouldBeNull();
        updated!.Nested!.Host.ShouldBe("b");
        updated.Nested.Port.ShouldBe(2);
    }

    [Test]
    public void RedactedNested_GranularPathFailsOnlyThatSubtree()
    {
        var before = new Settings
        {
            Nested = new Nested { Host = "a", Port = 1 },
        };
        var edited = new Settings
        {
            Nested = new Nested { Host = "b", Port = 1 },
        };
        var current = new Settings
        {
            Nested = new Nested { Host = "c", Port = 1 },
        };

        var rebased = before.CreateChangeSet(edited).RebaseOnto(current, Strict("Nested.Host"));

        rebased.HasConflicts.ShouldBeTrue();
        rebased.Conflicts.Single().PathText.ShouldBe("Nested.Host");
        rebased.Conflicts.Single().Kind.ShouldBe(SparseConflictKind.RedactedBefore);
    }

    [Test]
    public void RedactedKeyed_PassesThroughWholeMemberByDefault()
    {
        var before = new KeyedServerHolder { Items = [new KeyedServer { Id = "a", Name = "A" }] };
        var edited = new KeyedServerHolder { Items = [new KeyedServer { Id = "a", Name = "B" }] };
        var current = new KeyedServerHolder { Items = [new KeyedServer { Id = "a", Name = "C" }] };

        var applied = before
            .CreateChangeSet(edited)
            .TryApplyTo(current, out var updated, out var conflicts, PassThrough("Items"));

        applied.ShouldBeTrue();
        conflicts.ShouldBeNull();
        updated!.Items.Single().Name.ShouldBe("B");

        var strict = before.CreateChangeSet(edited).RebaseOnto(current, Strict("Items"));
        strict.HasConflicts.ShouldBeTrue();
        strict.Conflicts.Single().Kind.ShouldBe(SparseConflictKind.RedactedBefore);
        strict.Conflicts.Single().PathText.ShouldBe("Items");
    }

    [Test]
    public void RedactedDictionary_PassesThroughWholeMemberByDefault()
    {
        var before = new ScalarDictHolder { Scores = new() { ["a"] = 1 } };
        var edited = new ScalarDictHolder { Scores = new() { ["a"] = 2 } };
        var current = new ScalarDictHolder { Scores = new() { ["a"] = 3 } };

        var applied = before
            .CreateChangeSet(edited)
            .TryApplyTo(current, out var updated, out var conflicts, PassThrough("Scores"));

        applied.ShouldBeTrue();
        conflicts.ShouldBeNull();
        updated!.Scores["a"].ShouldBe(2);

        var strict = before.CreateChangeSet(edited).RebaseOnto(current, Strict("Scores"));
        strict.HasConflicts.ShouldBeTrue();
        strict.Conflicts.Single().Kind.ShouldBe(SparseConflictKind.RedactedBefore);
    }

    [Test]
    public void RedactedRoot_PassesThroughWholeContributionByDefault()
    {
        var missing = Optional<Settings.Fragment?>.Missing;
        var value = Optional<Settings.Fragment?>.Present(
            Settings.Fragment.From(new Settings { Label = "secret" })
        );
        var current = Optional<Settings.Fragment?>.Present(
            Settings.Fragment.From(new Settings { Label = "current" })
        );

        var changes = Settings.ChangeSet.Between(missing, value);
        var passthrough = changes.RebaseOnto(current, PassThrough(""));

        passthrough.HasConflicts.ShouldBeFalse();
        passthrough.Rebased.ToPatch().Apply(current).Value!.Label.Value.ShouldBe("secret");

        var strict = changes.RebaseOnto(current, Strict(""));
        strict.HasConflicts.ShouldBeTrue();
        strict.Conflicts.Single().Kind.ShouldBe(SparseConflictKind.RedactedBefore);
        strict.Conflicts.Single().Path.Count.ShouldBe(0);
    }

    [Test]
    public void PatchRebase_RedactedScalar_PassesThroughOrFails()
    {
        var baseState = Optional<Settings.Fragment?>.Present(
            Settings.Fragment.From(new Settings { Label = "a" })
        );
        var currentState = Optional<Settings.Fragment?>.Present(
            Settings.Fragment.From(new Settings { Label = "b" })
        );
        var local = new Settings.Patch { Label = "secret" };

        var passthrough = Settings.Patch.Rebase(
            baseState,
            local,
            currentState,
            PassThrough("Label")
        );
        passthrough.HasConflicts.ShouldBeFalse();
        passthrough.Rebased.Apply(currentState).Value!.Label.Value.ShouldBe("secret");

        var strict = Settings.Patch.Rebase(baseState, local, currentState, Strict("Label"));
        strict.HasConflicts.ShouldBeTrue();
        strict.Conflicts.Single().Kind.ShouldBe(SparseConflictKind.RedactedBefore);
        strict.Conflicts.Single().LocalValue.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void PayloadRoundTrip_KeepsWireVersion_AndRebasesWithOptions()
    {
        var before = new Settings { Label = "a", RetryCount = 1 };
        var edited = new Settings { Label = "secret", RetryCount = 2 };
        var current = new Settings { Label = "other", RetryCount = 1 };

        var payload = before.CreateChangeSet(edited).ToPayload();
        payload.Version.ShouldBe("0.1");
        var json = JsonSerializer.Serialize(payload);
        var restored = JsonSerializer.Deserialize<Settings.ChangePayload>(json)!.ToChangeSet();

        var applied = restored.TryApplyTo(
            current,
            out var updated,
            out var conflicts,
            PassThrough("Label")
        );

        applied.ShouldBeTrue();
        conflicts.ShouldBeNull();
        updated!.Label.ShouldBe("secret");

        restored
            .TryApplyTo(current, out _, out var strictConflicts, Strict("Label"))
            .ShouldBeFalse();
        strictConflicts!.Single().Kind.ShouldBe(SparseConflictKind.RedactedBefore);
    }
}
