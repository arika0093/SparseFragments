using System.Text.Json;

namespace SparseTypes.Tests.Patch;

[SparseModel]
public partial class MixedItemDetail
{
    public string Note { get; set; } = string.Empty;
}

[SparseModel]
public partial class MixedSecretItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    [SparseRedactBefore]
    public string? Secret { get; set; }

    public MixedItemDetail? Detail { get; set; }
}

[SparseModel]
public partial class MixedSecretHolder
{
    public List<MixedSecretItem> Items { get; set; } = [];

    public Dictionary<string, MixedSecretItem> Map { get; set; } = new();
}

[SparseModel]
public partial class MixedTempItem
{
    [SparseKey(Unassigned = 0)]
    public int Id { get; set; }

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    public string Name { get; set; } = string.Empty;

    [SparseRedactBefore]
    public string? Secret { get; set; }
}

[SparseModel]
public partial class MixedTempHolder
{
    public List<MixedTempItem> Items { get; set; } = [];
}

/// <summary>Same-item ordinary plus blind edits keep independent partitions.</summary>
/// <remarks>An ordinary child change and a redacted-before child change inside one
/// keyed or dictionary element must not collapse into a single blind patch: the
/// ordinary part keeps baseline checks while the blind part passes through.</remarks>
public sealed class MixedItemPartitionTests
{
    private const string BeforeSecret = "s3cr3t-before-value";

    private const string AfterSecret = "s3cr3t-after-value";

    private static MixedSecretItem Item(string id, string name, string? secret) =>
        new()
        {
            Id = id,
            Name = name,
            Secret = secret,
            Detail = new MixedItemDetail { Note = "note" },
        };

    private static Optional<MixedSecretHolder.Fragment?> HolderState(MixedSecretHolder model) =>
        Optional<MixedSecretHolder.Fragment?>.Present(MixedSecretHolder.Fragment.From(model));

    private static MixedSecretHolder MixedHolder(string name, string? secret) =>
        new()
        {
            Items = [Item("a", name, secret)],
            Map = new() { ["k"] = Item("m", name, secret) },
        };

    private static MixedSecretHolder.ChangePayload MixedPayload(string name, string? secret)
    {
        var payload = MixedSecretHolder
            .ChangeSet.Between(
                HolderState(MixedHolder("Alice", BeforeSecret)),
                HolderState(MixedHolder(name, secret))
            )
            .ToPayload();
        // Exercise the JSON wire shape as well as the in-memory DTOs.
        return JsonSerializer.Deserialize<MixedSecretHolder.ChangePayload>(
            JsonSerializer.Serialize(payload)
        )!;
    }

    private static void ShouldNotLeakSecrets(params string?[] texts)
    {
        foreach (var text in texts)
        {
            (text ?? string.Empty).ShouldNotContain(BeforeSecret);
            (text ?? string.Empty).ShouldNotContain(AfterSecret);
        }
    }

    [Test]
    public void KeyedMixedItemConflictAppliesNothing()
    {
        var payload = MixedPayload("Bob", AfterSecret);
        var current = MixedHolder("Charlie", "current-secret");

        var applied = payload.TryApplyMixedTo(current, out var updated, out var result);

        applied.ShouldBeFalse();
        updated.ShouldBeNull();
        result.HasConflicts.ShouldBeTrue();
        result
            .Conflicts.Select(static conflict => conflict.PathText)
            .ShouldBe(["Items[\"a\"].Name", "Map[\"k\"].Name"]);
        // The write-only subset is reported but never committed on its own.
        result.WriteOnlyPaths.ShouldBe(["Items[a].Secret", "Map[k].Secret"]);
        current.Items.Single().Name.ShouldBe("Charlie");
        current.Items.Single().Secret.ShouldBe("current-secret");
        current.Map["k"].Name.ShouldBe("Charlie");
        current.Map["k"].Secret.ShouldBe("current-secret");
        foreach (var conflict in result.Conflicts)
        {
            ShouldNotLeakSecrets(conflict.PathText, conflict.Reason, conflict.Kind.ToString());
        }
    }

    [Test]
    public void KeyedMixedItemSuccessAppliesBothPartitions()
    {
        var payload = MixedPayload("Bob", AfterSecret);
        var current = MixedHolder("Alice", "current-secret");

        var applied = payload.TryApplyMixedTo(current, out var updated, out var result);

        applied.ShouldBeTrue();
        result.HasConflicts.ShouldBeFalse();
        result.WriteOnlyPaths.ShouldBe(["Items[a].Secret", "Map[k].Secret"]);
        updated.ShouldNotBeNull();
        updated!.Items.Single().Name.ShouldBe("Bob");
        updated.Items.Single().Secret.ShouldBe(AfterSecret);
        updated.Map["k"].Name.ShouldBe("Bob");
        updated.Map["k"].Secret.ShouldBe(AfterSecret);
        // The source model is never mutated in place.
        current.Items.Single().Name.ShouldBe("Alice");
    }

    [Test]
    public void DictionaryMixedValueConflictAppliesNothing()
    {
        var payload = MixedPayload("Bob", AfterSecret);
        var current = MixedHolder("Alice", "current-secret");
        current.Map["k"].Name = "Charlie";

        var applied = payload.TryApplyMixedTo(current, out var updated, out var result);

        applied.ShouldBeFalse();
        updated.ShouldBeNull();
        result.HasConflicts.ShouldBeTrue();
        result.Conflicts.Select(static conflict => conflict.PathText).ShouldBe(["Map[\"k\"].Name"]);
        result.WriteOnlyPaths.ShouldBe(["Items[a].Secret", "Map[k].Secret"]);
        // Neither the blind nor the ordinary subset is applied.
        current.Items.Single().Name.ShouldBe("Alice");
        current.Items.Single().Secret.ShouldBe("current-secret");
        current.Map["k"].Name.ShouldBe("Charlie");
        current.Map["k"].Secret.ShouldBe("current-secret");
        foreach (var conflict in result.Conflicts)
        {
            ShouldNotLeakSecrets(conflict.PathText, conflict.Reason, conflict.Kind.ToString());
        }
    }

    [Test]
    public void NestedChildMixedEditKeepsOrdinaryChecks()
    {
        var before = HolderState(
            new MixedSecretHolder { Items = [Item("a", "Alice", BeforeSecret)] }
        );
        var edited = Item("a", "Alice", AfterSecret);
        edited.Detail!.Note = "note-Bob";
        var after = HolderState(new MixedSecretHolder { Items = [edited] });
        var payload = JsonSerializer.Deserialize<MixedSecretHolder.ChangePayload>(
            JsonSerializer.Serialize(MixedSecretHolder.ChangeSet.Between(before, after).ToPayload())
        )!;

        var diverged = new MixedSecretHolder { Items = [Item("a", "Alice", "current-secret")] };
        diverged.Items.Single().Detail!.Note = "note-Charlie";

        var applied = payload.TryApplyMixedTo(diverged, out var updated, out var result);

        applied.ShouldBeFalse();
        updated.ShouldBeNull();
        result
            .Conflicts.Select(static conflict => conflict.PathText)
            .ShouldBe(["Items[\"a\"].Detail.Note"]);
        result.WriteOnlyPaths.ShouldBe(["Items[a].Secret"]);
        diverged.Items.Single().Detail!.Note.ShouldBe("note-Charlie");
        diverged.Items.Single().Secret.ShouldBe("current-secret");
        foreach (var conflict in result.Conflicts)
        {
            ShouldNotLeakSecrets(conflict.PathText, conflict.Reason, conflict.Kind.ToString());
        }

        var current = new MixedSecretHolder { Items = [Item("a", "Alice", "current-secret")] };
        payload.TryApplyMixedTo(current, out var merged, out var mergedResult).ShouldBeTrue();
        mergedResult.HasConflicts.ShouldBeFalse();
        merged!.Items.Single().Detail!.Note.ShouldBe("note-Bob");
        merged.Items.Single().Secret.ShouldBe(AfterSecret);
    }

    [Test]
    public void TemporaryKeyMixedItemConflictAppliesNothing()
    {
        var temp = Guid.NewGuid();
        Optional<MixedTempHolder.Fragment?> TempState(string name, string? secret) =>
            Optional<MixedTempHolder.Fragment?>.Present(
                MixedTempHolder.Fragment.From(
                    new MixedTempHolder
                    {
                        Items =
                        [
                            new MixedTempItem
                            {
                                Id = 0,
                                TemporaryId = temp,
                                Name = name,
                                Secret = secret,
                            },
                        ],
                    }
                )
            );
        var payload = JsonSerializer.Deserialize<MixedTempHolder.ChangePayload>(
            JsonSerializer.Serialize(
                MixedTempHolder
                    .ChangeSet.Between(
                        TempState("Alice", BeforeSecret),
                        TempState("Bob", AfterSecret)
                    )
                    .ToPayload()
            )
        )!;
        var current = new MixedTempHolder
        {
            Items =
            [
                new MixedTempItem
                {
                    Id = 0,
                    TemporaryId = temp,
                    Name = "Charlie",
                    Secret = "current-secret",
                },
            ],
        };

        var applied = payload.TryApplyMixedTo(current, out var updated, out var result);

        applied.ShouldBeFalse();
        updated.ShouldBeNull();
        result.HasConflicts.ShouldBeTrue();
        result.Conflicts.ShouldHaveSingleItem();
        result.Conflicts.Single().PathText.ShouldContain("Name");
        result.WriteOnlyPaths.Count.ShouldBe(1);
        result.WriteOnlyPaths.Single().ShouldContain("Secret");
        current.Items.Single().Name.ShouldBe("Charlie");
        current.Items.Single().Secret.ShouldBe("current-secret");
        foreach (var conflict in result.Conflicts)
        {
            ShouldNotLeakSecrets(conflict.PathText, conflict.Reason, conflict.Kind.ToString());
        }

        var clean = new MixedTempHolder
        {
            Items =
            [
                new MixedTempItem
                {
                    Id = 0,
                    TemporaryId = temp,
                    Name = "Alice",
                    Secret = "current-secret",
                },
            ],
        };
        payload.TryApplyMixedTo(clean, out var merged, out var mergedResult).ShouldBeTrue();
        mergedResult.HasConflicts.ShouldBeFalse();
        merged!.Items.Single().Name.ShouldBe("Bob");
        merged.Items.Single().Secret.ShouldBe(AfterSecret);
        merged.Items.Single().TemporaryId.ShouldBe(temp);
    }

    [Test]
    public void MixedItemInvertKeepsReversibleOrdinaryChanges()
    {
        var payload = MixedPayload("Bob", AfterSecret);

        var rollback = payload.InvertReversibleChanges(out var skipped);

        skipped.ShouldBe(["Items[a].Secret", "Map[k].Secret"]);
        var after = HolderState(MixedHolder("Bob", AfterSecret));
        var before = HolderState(MixedHolder("Alice", BeforeSecret));
        // The reversible part inverts the ordinary Name transitions; the blind
        // Secret transitions stay out and appear in skipped instead.
        var restored = rollback.ToPatch().Apply(after);
        restored.Value!.Items.Value!.Single().Name.ShouldBe("Alice");
        restored.Value!.Map.Value!["k"].Name.ShouldBe("Alice");
        restored.Value!.Items.Value!.Single().Secret.ShouldBe(AfterSecret);
    }

    [Test]
    public void MixedItemToPatchStaysBaselineFree()
    {
        var payload = MixedPayload("Bob", AfterSecret);

        // ToPatch is a baseline-discarding projection: it applies onto a
        // diverged baseline that would conflict under TryApplyMixedTo.
        var diverged = HolderState(MixedHolder("Charlie", "current-secret"));
        var applied = payload.ToPatch().Apply(diverged);

        applied.IsPresent.ShouldBeTrue();
        applied.Value!.Items.Value!.Single().Name.ShouldBe("Bob");
        applied.Value!.Items.Value!.Single().Secret.ShouldBe(AfterSecret);
        applied.Value!.Map.Value!["k"].Name.ShouldBe("Bob");
        applied.Value!.Map.Value!["k"].Secret.ShouldBe(AfterSecret);
    }
}
