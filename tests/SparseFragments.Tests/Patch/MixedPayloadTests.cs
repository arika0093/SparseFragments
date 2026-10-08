using System.Text.Json;
using SparseFragments;

namespace SparseFragments.Tests.Patch;

/// <summary>Mixed redacted/write-only payload algebra over generated models (issue #119).</summary>
/// <remarks>
/// A member with a known before-state stays a baseline-aware transition while a
/// member with a redacted before-state is an explicit write-only operation. The
/// tests below pin the default: write-only members pass their requested
/// after-state through without historical comparison or three-way rebase, normal
/// members still validate and rebase, mixed application commits nothing on
/// conflict, and rollback excludes write-only paths while reporting them.
/// </remarks>
public sealed class MixedPayloadTests
{
    private const string Secret = "s3cr3t-label-value";

    private static Settings.ChangeSetPayload DeserializeSettings(string json) =>
        JsonSerializer.Deserialize<Settings.ChangeSetPayload>(json)!;

    private static string WithSecret(string json) => json.Replace("{SECRET}", Secret);

    private static Optional<Settings.Fragment?> SettingsState(string? label, int retry) =>
        Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Label = Optional<string?>.Present(label),
                RetryCount = Optional<int>.Present(retry),
            }
        );

    private static string ScalarChange(string member, string before, string after) =>
        """{"version":"0.1","changes":[{"member":"{MEMBER}","before":{BEFORE},"after":{AFTER}}]}"""
            .Replace("{MEMBER}", member)
            .Replace("{BEFORE}", before)
            .Replace("{AFTER}", after);

    private static void ShouldNotLeakSecret(params string?[] texts)
    {
        foreach (var text in texts)
        {
            (text ?? string.Empty).ShouldNotContain(Secret);
        }
    }

    [Test]
    public void RedactedWireRoundTripsAtVersion01()
    {
        var payload = DeserializeSettings(
            ScalarChange("Label", """{"state":"redacted"}""", """{"state":"value","value":"x"}""")
        );
        var json = JsonSerializer.Serialize(payload);
        json.ShouldContain("\"version\":\"0.1\"");
        json.ShouldContain("\"state\":\"redacted\"");

        var restored = DeserializeSettings(json);
        var change = restored.Changes!.Single();
        var before =
            (ChangeSetPayloadEndpoint<string?>)
                change.GetType().GetProperty("Before")!.GetValue(change)!;
        before.IsRedacted.ShouldBeTrue();
    }

    [Test]
    public void ToChangeSetFailsOnRedactedBeforeWithoutSecrets()
    {
        var payload = DeserializeSettings(
            WithSecret(
                """{"version":"0.1","changes":[{"member":"Label","before":{"state":"redacted"},"after":{"state":"value","value":"{SECRET}"}}]}"""
            )
        );

        var exception = Should.Throw<ArgumentException>(() => payload.ToChangeSet());
        exception.Message.ShouldContain("Label");
        ShouldNotLeakSecret(exception.Message);
    }

    [Test]
    public void ToChangeSetFailsOnRedactedAfter()
    {
        var payload = DeserializeSettings(
            ScalarChange("Label", """{"state":"missing"}""", """{"state":"redacted"}""")
        );

        var exception = Should.Throw<ArgumentException>(() => payload.ToChangeSet());
        exception.Message.ShouldContain("Label");
        exception.Message.ShouldContain("concrete");
    }

    [Test]
    public void ToPatchProjectsBlindSetsWithoutBaselineComparison()
    {
        var payload = DeserializeSettings(
            WithSecret(
                """{"version":"0.1","changes":[{"member":"Label","before":{"state":"redacted"},"after":{"state":"value","value":"{SECRET}"}}]}"""
            )
        );
        // The current state shares no history with the payload; a baseline-aware
        // transition could not be validated here, but the blind set still applies.
        var current = SettingsState("diverged", 7);

        var applied = payload.ToPatch().Apply(current);

        applied.IsPresent.ShouldBeTrue();
        applied.Value!.Label.Value.ShouldBe(Secret);
        applied.Value!.RetryCount.Value.ShouldBe(7);
    }

    [Test]
    public void MixedApplySucceedsAndReportsWriteOnlyPaths()
    {
        var payload = DeserializeSettings(
            WithSecret(
                """{"version":"0.1","changes":[{"member":"Label","before":{"state":"redacted"},"after":{"state":"value","value":"{SECRET}"}},{"member":"RetryCount","before":{"state":"value","value":1},"after":{"state":"value","value":2}}]}"""
            )
        );
        var current = new Settings { Label = "old", RetryCount = 1 };

        var applied = payload.TryApplyMixedTo(current, out var updated, out var result);

        applied.ShouldBeTrue();
        result.HasConflicts.ShouldBeFalse();
        result.Conflicts.ShouldBeEmpty();
        result.WriteOnlyPaths.ShouldBe(["Label"]);
        updated.ShouldNotBeNull();
        updated!.Label.ShouldBe(Secret);
        updated.RetryCount.ShouldBe(2);
        // The source model is never mutated in place.
        current.Label.ShouldBe("old");
        current.RetryCount.ShouldBe(1);
    }

    [Test]
    public void MixedApplyConflictCommitsNothing()
    {
        var payload = DeserializeSettings(
            WithSecret(
                """{"version":"0.1","changes":[{"member":"Label","before":{"state":"redacted"},"after":{"state":"value","value":"{SECRET}"}},{"member":"RetryCount","before":{"state":"value","value":1},"after":{"state":"value","value":2}}]}"""
            )
        );
        var current = new Settings { Label = "current", RetryCount = 99 };

        var applied = payload.TryApplyMixedTo(current, out var updated, out var result);

        applied.ShouldBeFalse();
        updated.ShouldBeNull();
        result.HasConflicts.ShouldBeTrue();
        result.Conflicts.Select(static conflict => conflict.PathText).ShouldBe(["RetryCount"]);
        result.WriteOnlyPaths.ShouldBe(["Label"]);
        // No silent partial persist of the write-only subset.
        current.Label.ShouldBe("current");
        current.RetryCount.ShouldBe(99);
        foreach (var conflict in result.Conflicts)
        {
            ShouldNotLeakSecret(conflict.PathText, conflict.Reason, conflict.Kind.ToString());
        }
    }

    [Test]
    public void RedactedIsNotMissing()
    {
        // A blind remove applies even though no baseline was ever supplied.
        var blindRemove = DeserializeSettings(
            ScalarChange("Label", """{"state":"redacted"}""", """{"state":"missing"}""")
        );
        var removed = blindRemove.ToPatch().Apply(SettingsState("anything", 1));
        removed.Value!.Label.IsPresent.ShouldBeFalse();

        // An ordinary remove against a stale baseline still conflicts instead.
        var staleRemove = Settings.ChangeSet.Between(
            SettingsState("a", 1),
            Optional<Settings.Fragment?>.Present(
                new Settings.Fragment { RetryCount = Optional<int>.Present(1) }
            )
        );
        var conflicted = staleRemove.RebaseOnto(SettingsState("b", 1));
        conflicted.HasConflicts.ShouldBeTrue();
    }

    [Test]
    public void BlindNullSetAppliesExplicitNull()
    {
        var payload = DeserializeSettings(
            ScalarChange("Label", """{"state":"redacted"}""", """{"state":"null"}""")
        );
        var applied = payload.ToPatch().Apply(SettingsState("before", 1));
        applied.Value!.Label.IsPresent.ShouldBeTrue();
        applied.Value!.Label.Value.ShouldBeNull();
    }

    [Test]
    public void NestedBlindLeafAppliesWithoutHistory()
    {
        var payload = DeserializeSettings(
            WithSecret(
                """{"version":"0.1","changes":[{"member":"Nested","nested":{"changes":[{"member":"Host","before":{"state":"redacted"},"after":{"state":"value","value":"{SECRET}"}}]}}]}"""
            )
        );

        var failure = Should.Throw<ArgumentException>(() => payload.ToChangeSet());
        failure.Message.ShouldContain("Nested.Host");
        ShouldNotLeakSecret(failure.Message);

        var current = new Settings
        {
            Nested = new Nested { Host = "elsewhere", Port = 1 },
        };
        var applied = payload.TryApplyMixedTo(current, out var updated, out var result);
        applied.ShouldBeTrue();
        result!.WriteOnlyPaths.ShouldBe(["Nested.Host"]);
        updated!.Nested!.Host.ShouldBe(Secret);
        updated.Nested.Port.ShouldBe(1);

        var rollback = payload.InvertReversibleChanges(out var skipped);
        skipped.ShouldBe(["Nested.Host"]);
        rollback.Nested.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void RollbackExcludesBlindSetsAndReportsThem()
    {
        var payload = DeserializeSettings(
            WithSecret(
                """{"version":"0.1","changes":[{"member":"Label","before":{"state":"redacted"},"after":{"state":"value","value":"{SECRET}"}},{"member":"RetryCount","before":{"state":"value","value":1},"after":{"state":"value","value":2}}]}"""
            )
        );

        var rollback = payload.InvertReversibleChanges(out var skipped);

        skipped.ShouldBe(["Label"]);
        // The reversible part inverts normally: applying it returns RetryCount.
        var before = SettingsState("kept", 1);
        var after = SettingsState("kept", 2);
        Settings.Patch.Between(rollback.ToPatch().Apply(after), before).IsEmpty.ShouldBeTrue();
        rollback.Label.IsChanged.ShouldBeFalse();
        rollback.RetryCount.IsChanged.ShouldBeTrue();
    }

    [Test]
    public void RollbackOfCleanPayloadIsCompleteAndMatchesInvert()
    {
        var before = SettingsState("a", 1);
        var after = SettingsState("b", 2);
        var payload = Settings.ChangeSet.Between(before, after).ToPayload();

        var rollback = payload.InvertReversibleChanges(out var skipped);

        skipped.ShouldBeEmpty();
        Settings.Patch.Between(rollback.ToPatch().Apply(after), before).IsEmpty.ShouldBeTrue();
        var inverted = Settings.ChangeSet.Between(before, after).Invert();
        Settings
            .Patch.Between(rollback.ToPatch().Apply(after), inverted.ToPatch().Apply(after))
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void KeyedWholeBlindSetApplies()
    {
        var json = WithSecret(
            """{"version":"0.1","changes":[{"member":"Items","before":{"state":"redacted"},"after":{"state":"value","value":[{"Id":"a","Name":"{SECRET}","Count":1}]}}]}"""
        );
        var payload = JsonSerializer.Deserialize<KeyedServerHolder.ChangeSetPayload>(json)!;

        Should.Throw<ArgumentException>(() => payload.ToChangeSet()).Message.ShouldContain("Items");

        var before = Optional<KeyedServerHolder.Fragment?>.Present(
            KeyedServerHolder.Fragment.From(new KeyedServerHolder())
        );
        var applied = payload.ToPatch().Apply(before);
        applied.Value!.Items.Value!.Single().Name.ShouldBe(Secret);

        var rollback = payload.InvertReversibleChanges(out var skipped);
        skipped.ShouldBe(["Items"]);
        rollback.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void KeyedItemRedactedFailsTypedOnBothProjections()
    {
        var json =
            """{"version":"0.1","changes":[{"member":"Scores","items":[{"key":"a","kind":"edit","before":{"state":"redacted"},"after":{"state":"value","value":9}}]}]}""";
        var payload = JsonSerializer.Deserialize<ScalarDictHolder.ChangeSetPayload>(json)!;

        var changeFailure = Should.Throw<ArgumentException>(() => payload.ToChangeSet());
        changeFailure.Message.ShouldContain("Scores");
        changeFailure.Message.ShouldContain("a");
        var patchFailure = Should.Throw<ArgumentException>(() => payload.ToPatch());
        patchFailure.Message.ShouldContain("Scores");
        ShouldNotLeakSecret(changeFailure.Message, patchFailure.Message);
    }

    [Test]
    public void DictionaryWholeBlindSetApplies()
    {
        var json =
            """{"version":"0.1","changes":[{"member":"Scores","before":{"state":"redacted"},"after":{"state":"value","value":{"b":3}}}]}""";
        var payload = JsonSerializer.Deserialize<ScalarDictHolder.ChangeSetPayload>(json)!;

        var before = Optional<ScalarDictHolder.Fragment?>.Present(
            ScalarDictHolder.Fragment.From(new ScalarDictHolder { Scores = new() { ["a"] = 1 } })
        );
        var applied = payload.ToPatch().Apply(before);
        applied.Value!.Scores.Value.ShouldBe(new Dictionary<string, int> { ["b"] = 3 });
    }

    [Test]
    public void WholeRootBlindSetReplacesWithoutBaseline()
    {
        var json = WithSecret(
            """{"version":"0.1","changes":[{"member":"$root","before":{"state":"redacted"},"after":{"state":"value","value":{"members":[{"member":"Host","value":{"state":"value","value":"{SECRET}"}},{"member":"Port","value":{"state":"value","value":2}}]}}}]}"""
        );
        var payload = JsonSerializer.Deserialize<Nested.ChangeSetPayload>(json)!;

        var rootFailure = Should.Throw<ArgumentException>(() => payload.ToChangeSet());
        rootFailure.Message.ShouldContain("$root");
        ShouldNotLeakSecret(rootFailure.Message);

        var current = new Nested { Host = "old", Port = 1 };
        var applied = payload.TryApplyMixedTo(current, out var updated, out var result);
        applied.ShouldBeTrue();
        result!.WriteOnlyPaths.ShouldBe(["$root"]);
        updated!.Host.ShouldBe(Secret);
        updated.Port.ShouldBe(2);
        current.Host.ShouldBe("old");

        var rollback = payload.InvertReversibleChanges(out var skipped);
        skipped.ShouldBe(["$root"]);
        rollback.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void RedactedEndpointSemantics()
    {
        var redacted = ChangeSetPayloadEndpoint<string?>.Redacted();
        redacted.IsRedacted.ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => redacted.ToOptional());

        ChangeSetPayloadEndpoint<string?>
            .FromOptional(Optional<string?>.Missing)
            .IsRedacted.ShouldBeFalse();
        var inconsistent = new ChangeSetPayloadEndpoint<string?>
        {
            State = ChangeSetPayloadState.Missing,
            Value = "oops",
        };
        Should.Throw<InvalidOperationException>(() => inconsistent.ToOptional());
    }

    [Test]
    public void KeyedWholeBlindRemovalFailsTyped()
    {
        var json =
            """{"version":"0.1","changes":[{"member":"Items","before":{"state":"redacted"},"after":{"state":"missing"}}]}""";
        var payload = JsonSerializer.Deserialize<KeyedServerHolder.ChangeSetPayload>(json)!;

        var failure = Should.Throw<ArgumentException>(() => payload.ToPatch());
        failure.Message.ShouldContain("Items");
        Should.Throw<ArgumentException>(() => payload.ToChangeSet());
    }

    [Test]
    public void OrdinaryInvertStaysATrueInversion()
    {
        var before = SettingsState("a", 1);
        var after = SettingsState("b", 2);
        var changes = Settings.ChangeSet.Between(before, after);

        var roundTripped = changes.Invert().Invert();
        Settings.Patch.Between(roundTripped.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
        Settings
            .Patch.Between(changes.Invert().ToPatch().Apply(after), before)
            .IsEmpty.ShouldBeTrue();
    }
}
