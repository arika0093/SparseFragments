using System.Text.Json;

namespace SparseTypes.Tests.Patch;

/// <summary>Structural ChangePayload validation without conversion or state.</summary>
/// <remarks>Validation answers whether the envelope is well-formed. It stays silent
/// about rebase conflicts and never echoes secret values.</remarks>
public sealed class ChangePayloadValidateTests
{
    private const string BeforeSecret = "s3cr3t-validate-before";

    private const string AfterSecret = "s3cr3t-validate-after";

    private static void ShouldNotLeakSecrets(ChangePayloadValidationResult result)
    {
        foreach (var error in result.Errors)
        {
            (error.Path ?? string.Empty).ShouldNotContain(BeforeSecret);
            (error.Path ?? string.Empty).ShouldNotContain(AfterSecret);
            (error.Message ?? string.Empty).ShouldNotContain(BeforeSecret);
            (error.Message ?? string.Empty).ShouldNotContain(AfterSecret);
        }
    }

    private static Optional<Settings.Fragment?> SettingsState(string? label, int retry) =>
        Optional<Settings.Fragment?>.Present(
            Settings.Fragment.From(new Settings { Label = label, RetryCount = retry })
        );

    [Test]
    public void ValidOrdinaryEnvelopePasses()
    {
        var payload = Settings
            .ChangeSet.Between(SettingsState("a", 1), SettingsState("b", 2))
            .ToPayload();

        var result = payload.Validate();

        result.IsValid.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Test]
    public void BlindAndMixedEnvelopesAreValidWithoutConversion()
    {
        // A baseline-free command carries no history, yet it is well-formed.
        var command = RedactedAccount.ChangePayload.FromPatch(
            new RedactedAccount.Patch { Password = AfterSecret }
        );
        var commandResult = command.Validate();
        commandResult.IsValid.ShouldBeTrue();
        ShouldNotLeakSecrets(commandResult);

        // A mixed envelope is valid even though ToChangeSet cannot rebuild it.
        var mixed = JsonSerializer.Deserialize<RedactedAccount.ChangePayload>(
            """{"version":"0.1","changes":[{"member":"DisplayName","before":{"state":"value","value":"a"},"after":{"state":"value","value":"b"}},{"member":"Password","before":{"state":"redacted"},"after":{"state":"value","value":"rotated"}}]}"""
        )!;
        var mixedResult = mixed.Validate();
        mixedResult.IsValid.ShouldBeTrue();
        Should.Throw<ArgumentException>(() => mixed.ToChangeSet());
        ShouldNotLeakSecrets(mixedResult);
    }

    [Test]
    public void MixedKeyedItemEnvelopeIsValid()
    {
        var before = Optional<MixedSecretHolder.Fragment?>.Present(
            MixedSecretHolder.Fragment.From(
                new MixedSecretHolder
                {
                    Items =
                    [
                        new MixedSecretItem
                        {
                            Id = "a",
                            Name = "Alice",
                            Secret = BeforeSecret,
                        },
                    ],
                }
            )
        );
        var after = Optional<MixedSecretHolder.Fragment?>.Present(
            MixedSecretHolder.Fragment.From(
                new MixedSecretHolder
                {
                    Items =
                    [
                        new MixedSecretItem
                        {
                            Id = "a",
                            Name = "Bob",
                            Secret = AfterSecret,
                        },
                    ],
                }
            )
        );
        var payload = JsonSerializer.Deserialize<MixedSecretHolder.ChangePayload>(
            JsonSerializer.Serialize(MixedSecretHolder.ChangeSet.Between(before, after).ToPayload())
        )!;

        var result = payload.Validate();

        result.IsValid.ShouldBeTrue();
        ShouldNotLeakSecrets(result);
    }

    [Test]
    public void InvalidVersionFails()
    {
        var payload = Settings
            .ChangeSet.Between(SettingsState("a", 1), SettingsState("b", 1))
            .ToPayload();
        payload.Version = "0.2";

        var result = payload.Validate();

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem();
        result.Errors.Single().Kind.ShouldBe(ChangePayloadValidationKind.Version);
        result.Errors.Single().Path.ShouldBe(string.Empty);

        payload.Version = null;
        var missing = payload.Validate();
        missing.IsValid.ShouldBeFalse();
        missing.Errors.Single().Kind.ShouldBe(ChangePayloadValidationKind.Version);
    }

    [Test]
    public void NullChangesAndNullAndDuplicateMembersFail()
    {
        var nullChanges = new Settings.ChangePayload { Version = "0.1", Changes = null };
        var nullResult = nullChanges.Validate();
        nullResult.IsValid.ShouldBeFalse();
        nullResult.Errors.ShouldHaveSingleItem();
        nullResult.Errors.Single().Kind.ShouldBe(ChangePayloadValidationKind.Changes);

        var nullEntry = Settings
            .ChangeSet.Between(SettingsState("a", 1), SettingsState("b", 1))
            .ToPayload();
        nullEntry.Changes!.Add(null!);
        var nullEntryResult = nullEntry.Validate();
        nullEntryResult.IsValid.ShouldBeFalse();
        nullEntryResult
            .Errors.Select(static error => error.Kind)
            .ShouldBe([ChangePayloadValidationKind.Changes]);

        var duplicate = JsonSerializer.Deserialize<Settings.ChangePayload>(
            """{"version":"0.1","changes":[{"member":"Label","before":{"state":"value","value":"a"},"after":{"state":"value","value":"b"}},{"member":"Label","before":{"state":"value","value":"b"},"after":{"state":"value","value":"c"}}]}"""
        )!;
        var duplicateResult = duplicate.Validate();
        duplicateResult.IsValid.ShouldBeFalse();
        duplicateResult.Errors.ShouldHaveSingleItem();
        duplicateResult.Errors.Single().Kind.ShouldBe(ChangePayloadValidationKind.Duplicate);
        duplicateResult.Errors.Single().Path.ShouldBe("Label");
    }

    [Test]
    public void InvalidEndpointsFailWithoutSecrets()
    {
        var missingAfter = JsonSerializer.Deserialize<RedactedAccount.ChangePayload>(
            """{"version":"0.1","changes":[{"member":"DisplayName","before":{"state":"value","value":"a"}}]}"""
        )!;
        var missingResult = missingAfter.Validate();
        missingResult.IsValid.ShouldBeFalse();
        missingResult.Errors.Single().Kind.ShouldBe(ChangePayloadValidationKind.Endpoint);
        missingResult.Errors.Single().Path.ShouldBe("DisplayName");

        var redactedAfter = JsonSerializer.Deserialize<RedactedAccount.ChangePayload>(
            """{"version":"0.1","changes":[{"member":"DisplayName","before":{"state":"value","value":"a"},"after":{"state":"redacted"}}]}"""
        )!;
        var redactedResult = redactedAfter.Validate();
        redactedResult.IsValid.ShouldBeFalse();
        redactedResult.Errors.Single().Kind.ShouldBe(ChangePayloadValidationKind.Endpoint);

        // A smuggled value inside a value-free endpoint is rejected, and the
        // rejection never echoes the value.
        var smuggled = JsonSerializer.Deserialize<RedactedAccount.ChangePayload>(
            """{"version":"0.1","changes":[{"member":"Password","before":{"state":"redacted","value":"SECRET-SMUGGLED"},"after":{"state":"value","value":"after"}}]}"""
        )!;
        var smuggledResult = smuggled.Validate();
        smuggledResult.IsValid.ShouldBeFalse();
        smuggledResult.Errors.Single().Kind.ShouldBe(ChangePayloadValidationKind.Endpoint);
        smuggledResult.Errors.Single().Message.ShouldNotContain("SECRET-SMUGGLED");
        ShouldNotLeakSecrets(smuggledResult);
    }

    [Test]
    public void DuplicateKeysAndTemporaryIdentitiesFail()
    {
        var duplicateKeys = JsonSerializer.Deserialize<ScalarDictHolder.ChangePayload>(
            """{"version":"0.1","changes":[{"member":"Scores","items":[{"key":"a","kind":"add","after":{"state":"value","value":1}},{"key":"a","kind":"add","after":{"state":"value","value":2}}]}]}"""
        )!;
        var keysResult = duplicateKeys.Validate();
        keysResult.IsValid.ShouldBeFalse();
        keysResult.Errors.ShouldHaveSingleItem();
        keysResult.Errors.Single().Kind.ShouldBe(ChangePayloadValidationKind.Duplicate);
        keysResult.Errors.Single().Path.ShouldBe("Scores");

        var temp = Guid.NewGuid();
        var duplicateTemps = JsonSerializer.Deserialize<MixedTempHolder.ChangePayload>(
            """{"version":"0.1","changes":[{"member":"Items","items":[{"key":0,"kind":"add","beforeIndex":-1,"afterIndex":0,"temporaryKey":"TEMP","after":{"state":"value","value":{"Id":0,"Name":"A"}}},{"key":0,"kind":"add","beforeIndex":-1,"afterIndex":1,"temporaryKey":"TEMP","after":{"state":"value","value":{"Id":0,"Name":"B"}}}],"beforeOrder":[],"afterOrder":[0,0],"tempBeforeOrder":["TEMP"],"tempAfterOrder":["TEMP","TEMP"]}]}""".Replace(
                "TEMP",
                temp.ToString()
            )
        )!;
        var tempsResult = duplicateTemps.Validate();
        tempsResult.IsValid.ShouldBeFalse();
        tempsResult
            .Errors.Select(static error => error.Kind)
            .ShouldContain(ChangePayloadValidationKind.Duplicate);
    }

    [Test]
    public void MalformedItemsFailWithPaths()
    {
        // An addition must omit before and carry after.
        var badAdd = JsonSerializer.Deserialize<KeyedServerHolder.ChangePayload>(
            """{"version":"0.1","changes":[{"member":"Items","items":[{"key":"a","kind":"add","beforeIndex":-1,"afterIndex":0,"before":{"state":"value","value":{"Id":"a","Name":"A","Count":0}},"after":{"state":"value","value":{"Id":"a","Name":"A","Count":0}}}],"beforeOrder":[],"afterOrder":["a"]}]}"""
        )!;
        var addResult = badAdd.Validate();
        addResult.IsValid.ShouldBeFalse();
        addResult.Errors.ShouldHaveSingleItem();
        addResult.Errors.Single().Kind.ShouldBe(ChangePayloadValidationKind.Item);
        addResult.Errors.Single().Path.ShouldBe("Items[a]");

        // A scalar edit must carry both endpoints.
        var badEdit = JsonSerializer.Deserialize<ScalarDictHolder.ChangePayload>(
            """{"version":"0.1","changes":[{"member":"Scores","items":[{"key":"a","kind":"edit","before":{"state":"value","value":1}}]}]}"""
        )!;
        var editResult = badEdit.Validate();
        editResult.IsValid.ShouldBeFalse();
        editResult.Errors.Single().Kind.ShouldBe(ChangePayloadValidationKind.Item);
        editResult.Errors.Single().Path.ShouldBe("Scores[a]");

        // Dictionaries do not reorder.
        var reorder = JsonSerializer.Deserialize<ScalarDictHolder.ChangePayload>(
            """{"version":"0.1","changes":[{"member":"Scores","items":[{"key":"a","kind":"reorder"}]}]}"""
        )!;
        var reorderResult = reorder.Validate();
        reorderResult.IsValid.ShouldBeFalse();
        reorderResult.Errors.Single().Kind.ShouldBe(ChangePayloadValidationKind.Item);

        // A nested member without its core is malformed.
        var nested = JsonSerializer.Deserialize<Settings.ChangePayload>(
            """{"version":"0.1","changes":[{"member":"Nested"}]}"""
        )!;
        var nestedResult = nested.Validate();
        nestedResult.IsValid.ShouldBeFalse();
        nestedResult.Errors.Single().Kind.ShouldBe(ChangePayloadValidationKind.Changes);
        nestedResult.Errors.Single().Path.ShouldBe("Nested");
    }

    [Test]
    public void MissingTemporaryOrdersFail()
    {
        var temp = Guid.NewGuid();
        var payload = JsonSerializer.Deserialize<MixedTempHolder.ChangePayload>(
            """{"version":"0.1","changes":[{"member":"Items","items":[{"key":0,"kind":"edit","beforeIndex":0,"afterIndex":0,"temporaryKey":"TEMP","edit":{"changes":[{"member":"Name","before":{"state":"value","value":"Alice"},"after":{"state":"value","value":"Bob"}}]}}],"beforeOrder":[0],"afterOrder":[0]}]}""".Replace(
                "TEMP",
                temp.ToString()
            )
        )!;

        var result = payload.Validate();

        result.IsValid.ShouldBeFalse();
        result.Errors.Count.ShouldBe(2);
        result.Errors.ShouldAllBe(static error =>
            error.Kind == ChangePayloadValidationKind.Order && error.Path == "Items"
        );
    }

    [Test]
    public void MultipleErrorsArriveInPayloadOrder()
    {
        var payload = JsonSerializer.Deserialize<Settings.ChangePayload>(
            """{"version":"0.1","changes":[{"member":"Label","before":{"state":"value","value":"a"}},{"member":"RetryCount","before":{"state":"value","value":1},"after":{"state":"redacted"}},{"member":"Label","before":{"state":"value","value":"b"},"after":{"state":"value","value":"c"}}]}"""
        )!;

        var result = payload.Validate();

        result.IsValid.ShouldBeFalse();
        result.Errors.Count.ShouldBe(3);
        result.Errors.Select(static error => error.Path).ShouldBe(["Label", "RetryCount", "Label"]);
        result
            .Errors.Select(static error => error.Kind)
            .ShouldBe([
                ChangePayloadValidationKind.Endpoint,
                ChangePayloadValidationKind.Endpoint,
                ChangePayloadValidationKind.Duplicate,
            ]);
        ShouldNotLeakSecrets(result);
    }

    [Test]
    public void WholeRootShapesValidate()
    {
        var whole = JsonSerializer.Deserialize<Nested.ChangePayload>(
            """{"version":"0.1","changes":[{"member":"$root","before":{"state":"missing","value":null},"after":{"state":"value","value":{"members":[{"member":"Host","value":{"state":"value","value":"h"}},{"member":"Port","value":{"state":"value","value":2}}]}}}]}"""
        )!;
        whole.Validate().IsValid.ShouldBeTrue();

        var redactedAfter = JsonSerializer.Deserialize<Nested.ChangePayload>(
            """{"version":"0.1","changes":[{"member":"$root","before":{"state":"missing","value":null},"after":{"state":"redacted","value":null}}]}"""
        )!;
        var redactedResult = redactedAfter.Validate();
        redactedResult.IsValid.ShouldBeFalse();
        redactedResult.Errors.Single().Kind.ShouldBe(ChangePayloadValidationKind.Endpoint);
        redactedResult.Errors.Single().Path.ShouldBe("$root");

        // A blind whole-collection removal has no patch projection.
        var blindRemoval = JsonSerializer.Deserialize<KeyedServerHolder.ChangePayload>(
            """{"version":"0.1","changes":[{"member":"Items","before":{"state":"redacted"},"after":{"state":"missing"}}]}"""
        )!;
        var removalResult = blindRemoval.Validate();
        removalResult.IsValid.ShouldBeFalse();
        removalResult.Errors.Single().Path.ShouldBe("Items");
        ShouldNotLeakSecrets(removalResult);
    }

    [Test]
    public void ValidPayloadCanStillConflictOnApply()
    {
        // Structural validity, conversion, and concurrent-state conflicts are
        // three separate answers: this mixed envelope is valid, unconvertible
        // to a ChangeSet, and conflicting against a diverged model.
        var payload = JsonSerializer.Deserialize<RedactedAccount.ChangePayload>(
            """{"version":"0.1","changes":[{"member":"DisplayName","before":{"state":"value","value":"a"},"after":{"state":"value","value":"b"}},{"member":"Password","before":{"state":"redacted"},"after":{"state":"value","value":"rotated"}}]}"""
        )!;
        payload.Validate().IsValid.ShouldBeTrue();
        Should.Throw<ArgumentException>(() => payload.ToChangeSet());

        var applied = payload.TryApplyMixedTo(
            new RedactedAccount { DisplayName = "diverged", Password = "kept" },
            out var updated,
            out var result
        );

        applied.ShouldBeFalse();
        updated.ShouldBeNull();
        result.HasConflicts.ShouldBeTrue();
    }
}
