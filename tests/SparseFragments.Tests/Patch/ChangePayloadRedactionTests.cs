using System.Text.Json;

namespace SparseFragments.Tests.Patch;

[SparseFragmentModel]
public partial class RedactedAccount
{
    public string? DisplayName { get; set; }

    [SparseRedactBefore]
    public string? Password { get; set; }
}

[SparseFragmentModel]
public partial class RedactedProfile
{
    public string? Nickname { get; set; }

    [SparseRedactBefore]
    public string? Token { get; set; }
}

[SparseFragmentModel]
public partial class RedactedAccountHolder
{
    public string? Name { get; set; }

    public RedactedProfile? Profile { get; set; }
}

[SparseFragmentModel]
public partial class RedactedTeamHolder
{
    [SparseRedactBefore]
    public RedactedProfile? Profile { get; set; }
}

[SparseFragmentModel]
public partial class RedactedDevice
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class RedactedDeviceHolder
{
    [SparseRedactBefore]
    public List<RedactedDevice> Devices { get; set; } = [];
}

[SparseFragmentModel]
public partial class RedactedSecretMap
{
    [SparseRedactBefore]
    public Dictionary<string, string> Secrets { get; set; } = new();
}

/// <summary>Covers the unified ChangePayload transport (issue #118).</summary>
public sealed class ChangePayloadRedactionTests
{
    private static Optional<RedactedAccount.Fragment?> AccountState(
        string? displayName,
        string? password
    ) =>
        Optional<RedactedAccount.Fragment?>.Present(
            RedactedAccount.Fragment.From(
                new RedactedAccount { DisplayName = displayName, Password = password }
            )
        );

    private static T PayloadRoundTrip<T>(T payload) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(payload))!;

    [Test]
    public void MixedPayload_CarriesTransitionAndCommandTogether()
    {
        var before = AccountState("before-name", "before-password");
        var after = AccountState("after-name", "after-password");
        var payload = RedactedAccount.ChangeSet.Between(before, after).ToPayload();
        var json = JsonSerializer.Serialize(payload);

        // The regular transition and the baseline-free command share one envelope.
        payload.Changes!.Count.ShouldBe(2);
        json.ShouldContain("\"version\":\"0.1\"");
        json.ShouldContain("\"state\":\"redacted\"");
        // The undisclosed before-state never travels; the desired after-state
        // does, which callers must treat as sensitive downstream.
        json.ShouldNotContain("before-password");
        json.ShouldContain("after-password");
        json.ShouldContain("before-name");

        var restored = PayloadRoundTrip(payload);
        Should.Throw<ArgumentException>(() => restored.ToChangeSet());
        var projected = restored.ToPatch();
        RedactedAccount.Patch.Between(projected.Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void RedactedBefore_IsDistinctFromMissing()
    {
        var redacted = new ChangePayloadEndpoint<string> { State = ChangePayloadState.Redacted };
        redacted.State.ShouldBe(ChangePayloadState.Redacted);
        // Redacted never collapses to Missing: there is no observable value.
        Should.Throw<InvalidOperationException>(() => redacted.ToOptional());

        var missingJson =
            """{"version":"0.1","changes":[{"member":"DisplayName","before":{"state":"missing"},"after":{"state":"value","value":"after-name"}}]}""";
        var missing = JsonSerializer.Deserialize<RedactedAccount.ChangePayload>(missingJson)!;
        missing.ToChangeSet().DisplayName.After.Value.ShouldBe("after-name");

        var redactedJson =
            """{"version":"0.1","changes":[{"member":"Password","before":{"state":"redacted"},"after":{"state":"value","value":"after-password"}}]}""";
        var redactedPayload = JsonSerializer.Deserialize<RedactedAccount.ChangePayload>(
            redactedJson
        )!;
        Should.Throw<ArgumentException>(() => redactedPayload.ToChangeSet());
        var patch = redactedPayload.ToPatch();
        var updated = patch.ApplyTo(
            new RedactedAccount { DisplayName = "kept", Password = "before-password" }
        );
        updated.Password.ShouldBe("after-password");
        updated.DisplayName.ShouldBe("kept");
    }

    [Test]
    public void RedactedBefore_RequiresObservableAfter()
    {
        var missingAfter =
            """{"version":"0.1","changes":[{"member":"DisplayName","before":{"state":"redacted"}}]}""";
        var withoutAfter = JsonSerializer.Deserialize<RedactedAccount.ChangePayload>(missingAfter)!;
        Should.Throw<ArgumentException>(() => withoutAfter.ToChangeSet());
        Should.Throw<ArgumentException>(() => withoutAfter.ToPatch());

        var redactedAfter =
            """{"version":"0.1","changes":[{"member":"DisplayName","before":{"state":"redacted"},"after":{"state":"redacted"}}]}""";
        var withRedactedAfter = JsonSerializer.Deserialize<RedactedAccount.ChangePayload>(
            redactedAfter
        )!;
        Should.Throw<ArgumentException>(() => withRedactedAfter.ToChangeSet());
        Should.Throw<ArgumentException>(() => withRedactedAfter.ToPatch());
    }

    [Test]
    public void RedactedEndpoint_WithValueIsRejectedEverywhere()
    {
        // Unit-level: a redacted endpoint carrying a value is malformed, and
        // the rejection never embeds the plaintext.
        var smuggled = new ChangePayloadEndpoint<string>
        {
            State = ChangePayloadState.Redacted,
            Value = "SECRET",
        };
        var redactedError = Should.Throw<InvalidOperationException>(() => smuggled.Validate());
        redactedError.Message.ShouldNotContain("SECRET");
        Should.Throw<InvalidOperationException>(() => smuggled.ToOptional());

        var missingWithValue = new ChangePayloadEndpoint<string>
        {
            State = ChangePayloadState.Missing,
            Value = "SECRET",
        };
        Should.Throw<InvalidOperationException>(() => missingWithValue.Validate());

        // Value-free redactions (including explicit JSON null) stay valid.
        new ChangePayloadEndpoint<string> { State = ChangePayloadState.Redacted }.Validate();
        new ChangePayloadEndpoint<string?> { State = ChangePayloadState.Redacted }.Validate();

        // Payload-level: every interpretation path rejects a smuggled value
        // without applying anything or leaking the secret.
        var smuggledJson =
            """{"version":"0.1","changes":[{"member":"Password","before":{"state":"redacted","value":"SECRET"},"after":{"state":"value","value":"after-password"}}]}""";
        var smuggledPayload = JsonSerializer.Deserialize<RedactedAccount.ChangePayload>(
            smuggledJson
        )!;
        var beforeError = Should.Throw<InvalidOperationException>(() =>
            smuggledPayload.ToChangeSet()
        );
        beforeError.Message.ShouldNotContain("SECRET");
        Should.Throw<InvalidOperationException>(() => smuggledPayload.ToPatch());
        IReadOnlyList<string> skipped;
        Should.Throw<InvalidOperationException>(() =>
            smuggledPayload.InvertReversibleChanges(out skipped)
        );
        var model = new RedactedAccount { DisplayName = "kept", Password = "before-password" };
        Should.Throw<InvalidOperationException>(() =>
            smuggledPayload.TryApplyMixedTo(model, out _, out _)
        );
        model.Password.ShouldBe("before-password");
    }

    [Test]
    public void FromPatch_BuildsBaselineFreeCommand()
    {
        var patch = new RedactedAccount.Patch { Password = "after-password" };
        var payload = RedactedAccount.ChangePayload.FromPatch(patch);
        var json = JsonSerializer.Serialize(payload);

        payload.Version.ShouldBe("0.1");
        payload.Changes.ShouldHaveSingleItem();
        json.ShouldBe(
            """{"version":"0.1","changes":[{"member":"Password","before":{"state":"redacted","value":null},"after":{"state":"value","value":"after-password"}}]}"""
        );

        var restored = PayloadRoundTrip(payload);
        Should.Throw<ArgumentException>(() => restored.ToChangeSet());
        var updated = restored.ToPatch().ApplyTo(new RedactedAccount { DisplayName = "kept" });
        updated.DisplayName.ShouldBe("kept");
        updated.Password.ShouldBe("after-password");

        var empty = PayloadRoundTrip(RedactedAccount.ChangePayload.FromPatch(new()));
        empty.Changes.ShouldBeEmpty();
        empty.ToChangeSet().IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void NestedRedacted_BeforeStaysUndisclosed()
    {
        Optional<RedactedAccountHolder.Fragment?> State(string? nickname, string? token) =>
            Optional<RedactedAccountHolder.Fragment?>.Present(
                RedactedAccountHolder.Fragment.From(
                    new RedactedAccountHolder
                    {
                        Name = "same",
                        Profile = new RedactedProfile { Nickname = nickname, Token = token },
                    }
                )
            );

        var before = State("before-nickname", "before-token");
        var after = State("after-nickname", "after-token");
        var json = JsonSerializer.Serialize(
            RedactedAccountHolder.ChangeSet.Between(before, after).ToPayload()
        );

        json.ShouldContain("\"state\":\"redacted\"");
        json.ShouldNotContain("before-token");
        json.ShouldContain("before-nickname");

        var restored = JsonSerializer.Deserialize<RedactedAccountHolder.ChangePayload>(json)!;
        Should.Throw<ArgumentException>(() => restored.ToChangeSet());
        RedactedAccountHolder
            .Patch.Between(restored.ToPatch().Apply(before), after)
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void NestedMemberRedacted_RedactsWholeSubtree()
    {
        Optional<RedactedTeamHolder.Fragment?> State(string? nickname, string? token) =>
            Optional<RedactedTeamHolder.Fragment?>.Present(
                RedactedTeamHolder.Fragment.From(
                    new RedactedTeamHolder
                    {
                        Profile = new RedactedProfile { Nickname = nickname, Token = token },
                    }
                )
            );

        var before = State("before-nickname", "before-token");
        var after = State("after-nickname", "after-token");
        var json = JsonSerializer.Serialize(
            RedactedTeamHolder.ChangeSet.Between(before, after).ToPayload()
        );

        // The flagged nested member redacts every descendant before-state.
        json.ShouldNotContain("before-nickname");
        json.ShouldNotContain("before-token");

        var restored = JsonSerializer.Deserialize<RedactedTeamHolder.ChangePayload>(json)!;
        Should.Throw<ArgumentException>(() => restored.ToChangeSet());
        RedactedTeamHolder
            .Patch.Between(restored.ToPatch().Apply(before), after)
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void KeyedRedacted_BeforeStaysUndisclosed()
    {
        Optional<RedactedDeviceHolder.Fragment?> State(params RedactedDevice[] devices) =>
            Optional<RedactedDeviceHolder.Fragment?>.Present(
                RedactedDeviceHolder.Fragment.From(
                    new RedactedDeviceHolder { Devices = devices.ToList() }
                )
            );

        var before = State(new RedactedDevice { Id = "a", Name = "before-device" });
        var after = State(new RedactedDevice { Id = "a", Name = "after-device" });
        var json = JsonSerializer.Serialize(
            RedactedDeviceHolder.ChangeSet.Between(before, after).ToPayload()
        );

        json.ShouldContain("\"state\":\"redacted\"");
        json.ShouldNotContain("before-device");

        var restored = PayloadRoundTrip(
            RedactedDeviceHolder.ChangeSet.Between(before, after).ToPayload()
        );
        Should.Throw<ArgumentException>(() => restored.ToChangeSet());
        RedactedDeviceHolder
            .Patch.Between(restored.ToPatch().Apply(before), after)
            .IsEmpty.ShouldBeTrue();

        var removed = PayloadRoundTrip(
            RedactedDeviceHolder.ChangeSet.Between(after, State()).ToPayload()
        );
        Should.Throw<ArgumentException>(() => removed.ToChangeSet());
        RedactedDeviceHolder
            .Patch.Between(removed.ToPatch().Apply(after), State())
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void DictRedacted_BeforeStaysUndisclosed()
    {
        Optional<RedactedSecretMap.Fragment?> State(string value) =>
            Optional<RedactedSecretMap.Fragment?>.Present(
                RedactedSecretMap.Fragment.From(
                    new RedactedSecretMap { Secrets = new() { ["key"] = value } }
                )
            );

        var before = State("before-value");
        var after = State("after-value");
        var json = JsonSerializer.Serialize(
            RedactedSecretMap.ChangeSet.Between(before, after).ToPayload()
        );

        json.ShouldContain("\"state\":\"redacted\"");
        json.ShouldNotContain("before-value");

        var restored = PayloadRoundTrip(
            RedactedSecretMap.ChangeSet.Between(before, after).ToPayload()
        );
        Should.Throw<ArgumentException>(() => restored.ToChangeSet());
        RedactedSecretMap
            .Patch.Between(restored.ToPatch().Apply(before), after)
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void WholeRoot_RedactsBeforeSnapshot()
    {
        var present = AccountState("name", "before-password");
        var removed = PayloadRoundTrip(
            RedactedAccount
                .ChangeSet.Between(present, Optional<RedactedAccount.Fragment?>.Missing)
                .ToPayload()
        );
        var removedJson = JsonSerializer.Serialize(removed);
        removedJson.ShouldContain("\"member\":\"$root\"");
        removedJson.ShouldContain("\"state\":\"redacted\"");
        removedJson.ShouldNotContain("before-password");
        Should.Throw<ArgumentException>(() => removed.ToChangeSet());
        RedactedAccount
            .Patch.Between(
                removed.ToPatch().Apply(present),
                Optional<RedactedAccount.Fragment?>.Missing
            )
            .IsEmpty.ShouldBeTrue();

        // A missing before-state is known absence, so the add stays complete.
        var added = PayloadRoundTrip(
            RedactedAccount
                .ChangeSet.Between(Optional<RedactedAccount.Fragment?>.Missing, present)
                .ToPayload()
        );
        added.ToChangeSet().IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void ChangePayload_ToPatchProjectsNormalTransitions()
    {
        var before = AccountState("before-name", "before-password");
        var after = AccountState("after-name", "after-password");
        var changes = RedactedAccount.ChangeSet.Between(before, after);
        var direct = changes.ToPatch();
        var payload = PayloadRoundTrip(changes.ToPayload());
        // The redacted member blocks ChangeSet conversion but not projection.
        var projected = payload.ToPatch();
        RedactedAccount.Patch.Between(projected.Apply(before), after).IsEmpty.ShouldBeTrue();
        RedactedAccount.Patch.Between(direct.Apply(before), after).IsEmpty.ShouldBeTrue();
    }
}
