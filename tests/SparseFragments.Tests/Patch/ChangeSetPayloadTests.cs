using System.Text.Json;
using SparseFragments.Playground.Models;

namespace SparseFragments.Tests.Patch;

public sealed class ChangeSetPayloadTests
{
    private static KeyedServer Server(string id, string name, int count = 0) =>
        new() { Id = id, Name = name, Count = count };

    private static Optional<KeyedServerHolder.Fragment?> KeyedState(params KeyedServer[] items) =>
        Optional<KeyedServerHolder.Fragment?>.Present(
            KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = items.ToList() })
        );

    private static T RoundTrip<T>(T payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return JsonSerializer.Deserialize<T>(json)!;
    }

    [Test]
    public void ToPayload_UsesTypedMemberVariantAndRoundTripsThroughJson()
    {
        var before = Settings.Fragment.From(new Settings { Label = "before", RetryCount = 1 });
        var after = Settings.Fragment.From(new Settings { Label = "after", RetryCount = 1 });
        var changes = Settings.ChangeSet.Between(
            Optional<Settings.Fragment?>.Present(before),
            Optional<Settings.Fragment?>.Present(after)
        );

        var payload = changes.ToPayload();
        var json = JsonSerializer.Serialize(payload);
        var restored = JsonSerializer.Deserialize<Settings.ChangeSetPayload>(json)!;

        json.ShouldContain("\"state\":\"value\"");
        restored.Version.ShouldBe(1);
        restored.Changes.ShouldHaveSingleItem();
        restored.Changes[0].GetType().Name.ShouldContain("ChangeSetPayloadChange");
        var endpointProperty = restored.Changes[0].GetType().GetProperty("After")!;
        var endpoint = endpointProperty.GetValue(restored.Changes[0])!;
        var endpointType = endpoint.GetType();
        endpointType.GetProperty("State")!.GetValue(endpoint)!.ToString().ShouldBe("Value");
        endpointType.GetProperty("Value")!.GetValue(endpoint).ShouldBe("after");
    }

    [Test]
    public void Payload_RoundTripsWholeRootMissingNullAndValueStates()
    {
        var states = new[]
        {
            Optional<Settings.Fragment?>.Missing,
            Optional<Settings.Fragment?>.Present(null),
            Optional<Settings.Fragment?>.Present(
                Settings.Fragment.From(new Settings { Label = "present", RetryCount = 3 })
            ),
        };

        foreach (var before in states)
        foreach (var after in states)
        {
            var changes = Settings.ChangeSet.Between(before, after);
            var restored = RoundTrip(changes.ToPayload()).ToChangeSet();
            Settings.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
        }
    }

    [Test]
    public void Payload_EncodesWholeRootTransitionsInsideChangesAsMemberArrays()
    {
        var after = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Label = Optional<string?>.Present("present"),
                RetryCount = Optional<int>.Present(3),
            }
        );

        var payload = Settings.ChangeSet.Between(Optional<Settings.Fragment?>.Missing, after)
            .ToPayload();
        var json = JsonSerializer.Serialize(payload);

        payload.Changes.ShouldHaveSingleItem();
        json.ShouldContain("\"member\":\"$root\"");
        json.ShouldContain("\"members\":[");
        json.ShouldContain("\"state\":\"missing\"");
        json.ShouldNotContain("Member1");
        Settings.Patch
            .Between(payload.ToChangeSet().ToPatch().Apply(Optional<Settings.Fragment?>.Missing), after)
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void Payload_UsesMemberArraysForNestedUnsetAndSetNull()
    {
        var before = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Nested = Optional<Nested.Fragment?>.Present(
                    new Nested.Fragment { Host = Optional<string>.Present("old") }
                ),
            }
        );
        var afterStates = new[]
        {
            Optional<Nested.Fragment?>.Missing,
            Optional<Nested.Fragment?>.Present(null),
        };

        foreach (var nestedAfter in afterStates)
        {
            var after = Optional<Settings.Fragment?>.Present(
                new Settings.Fragment { Nested = nestedAfter }
            );
            var payload = Settings.ChangeSet.Between(before, after).ToPayload();
            var json = JsonSerializer.Serialize(payload);
            var restored = RoundTrip(payload).ToChangeSet();

            json.ShouldContain("\"member\":\"$root\"");
            json.ShouldContain("\"members\":[");
            json.ShouldNotContain("Member1");
            Settings.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
        }
    }

    [Test]
    public void Payload_RoundTripsNestedMemberChanges()
    {
        var before = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Nested = Optional<Nested.Fragment?>.Present(
                    new Nested.Fragment { Host = Optional<string>.Present("old") }
                ),
            }
        );
        var after = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Nested = Optional<Nested.Fragment?>.Present(
                    new Nested.Fragment { Host = Optional<string>.Present("new") }
                ),
            }
        );

        var payload = Settings.ChangeSet.Between(before, after).ToPayload();
        var json = JsonSerializer.Serialize(payload);
        using (var document = JsonDocument.Parse(json))
        {
            document
                .RootElement.EnumerateObject()
                .Select(property => property.Name)
                .ShouldBe(["version", "changes"]);
            var nested = document
                .RootElement.GetProperty("changes")[0]
                .GetProperty("nested");
            nested.TryGetProperty("version", out _).ShouldBeFalse();
            nested.TryGetProperty("changes", out _).ShouldBeTrue();
        }
        var restored = RoundTrip(payload).ToChangeSet();
        Settings.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void Payload_RoundTripsScalarMissingNullAndValueEndpoints()
    {
        var states = new[]
        {
            Optional<string?>.Missing,
            Optional<string?>.Present(null),
            Optional<string?>.Present("value"),
        };

        foreach (var beforeLabel in states)
        foreach (var afterLabel in states)
        {
            var before = Optional<Settings.Fragment?>.Present(
                new Settings.Fragment { Label = beforeLabel }
            );
            var after = Optional<Settings.Fragment?>.Present(
                new Settings.Fragment { Label = afterLabel }
            );
            var restored = RoundTrip(Settings.ChangeSet.Between(before, after).ToPayload())
                .ToChangeSet();

            Settings.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
        }
    }

    [Test]
    public void Payload_RoundTripsSparseKeyedAddRemoveEditAndReorder()
    {
        var before = KeyedState(Server("a", "A"), Server("b", "B"));
        var after = KeyedState(Server("b", "B2", 2), Server("c", "C"));
        var payload = KeyedServerHolder.ChangeSet.Between(before, after).ToPayload();
        var json = JsonSerializer.Serialize(payload);

        json.ShouldContain("\"kind\":\"edit\"");
        json.ShouldNotContain("\"before\":null");
        json.ShouldNotContain("\"after\":null");
        json.ShouldNotContain("\"edit\":null");
        json.ShouldNotContain("\"isReordered\":false");
        using (var document = JsonDocument.Parse(json))
        {
            var itemChanges = document
                .RootElement.GetProperty("changes")
                .EnumerateArray()
                .Single(change => change.GetProperty("member").GetString() == "Items");
            var items = itemChanges.GetProperty("items").EnumerateArray().ToArray();
            var edited = items.Single(item => item.GetProperty("key").GetString() == "b");
            edited.TryGetProperty("before", out _).ShouldBeFalse();
            edited.TryGetProperty("after", out _).ShouldBeFalse();
            edited.GetProperty("edit")
                .GetProperty("changes")
                .EnumerateArray()
                .Select(change => change.GetProperty("member").GetString())
                .ShouldNotContain("Id");
            items.Single(item => item.GetProperty("key").GetString() == "c")
                .TryGetProperty("before", out _)
                .ShouldBeFalse();
            items.Single(item => item.GetProperty("key").GetString() == "a")
                .TryGetProperty("after", out _)
                .ShouldBeFalse();
        }
        var restored = RoundTrip(payload).ToChangeSet();

        KeyedServerHolder.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
        restored.Items.GetChange("b").IsEdited.ShouldBeTrue();
        restored.Items.GetChange("c").IsAdded.ShouldBeTrue();
        restored.Items.GetChange("a").IsRemoved.ShouldBeTrue();

        var orderBefore = KeyedState(Server("a", "A"), Server("b", "B"), Server("c", "C"));
        var orderAfter = KeyedState(Server("b", "B"), Server("a", "A"), Server("c", "C"));
        var orderPayload = KeyedServerHolder.ChangeSet.Between(orderBefore, orderAfter).ToPayload();
        var orderJson = JsonSerializer.Serialize(orderPayload);
        orderJson.ShouldContain("\"kind\":\"reorder\"");
        orderJson.ShouldContain("\"isReordered\":true");
        orderJson.ShouldNotContain("\"before\":null");
        orderJson.ShouldNotContain("\"after\":null");
        var orderRestored = RoundTrip(orderPayload).ToChangeSet();
        KeyedServerHolder.Patch
            .Between(orderRestored.ToPatch().Apply(orderBefore), orderAfter)
            .IsEmpty
            .ShouldBeTrue();
    }

    [Test]
    public void Payload_RoundTripsSparseScalarAndStructuralDictionaryChanges()
    {
        var scalarBefore = Optional<ScalarDictHolder.Fragment?>.Present(
            ScalarDictHolder.Fragment.From(new ScalarDictHolder { Scores = new() { ["a"] = 1, ["b"] = 2 } })
        );
        var scalarAfter = Optional<ScalarDictHolder.Fragment?>.Present(
            ScalarDictHolder.Fragment.From(new ScalarDictHolder { Scores = new() { ["b"] = 3, ["c"] = 4 } })
        );
        var scalarChanges = ScalarDictHolder.ChangeSet.Between(scalarBefore, scalarAfter);
        var scalarRestored = RoundTrip(scalarChanges.ToPayload()).ToChangeSet();
        ScalarDictHolder.Patch
            .Between(scalarRestored.ToPatch().Apply(scalarBefore), scalarAfter)
            .IsEmpty
            .ShouldBeTrue();

        var structuralBefore = Optional<StructuralDictHolder.Fragment?>.Present(
            StructuralDictHolder.Fragment.From(
                new StructuralDictHolder { Servers = new() { ["a"] = Server("a", "old") } }
            )
        );
        var structuralAfter = Optional<StructuralDictHolder.Fragment?>.Present(
            StructuralDictHolder.Fragment.From(
                new StructuralDictHolder { Servers = new() { ["a"] = Server("a", "new"), ["b"] = Server("b", "added") } }
            )
        );
        var structuralChanges = StructuralDictHolder.ChangeSet.Between(structuralBefore, structuralAfter);
        var structuralPayload = structuralChanges.ToPayload();
        var structuralJson = JsonSerializer.Serialize(structuralPayload);
        using (var document = JsonDocument.Parse(structuralJson))
        {
            var itemChanges = document
                .RootElement.GetProperty("changes")
                .EnumerateArray()
                .Single(change => change.GetProperty("member").GetString() == "Servers");
            var edited = itemChanges
                .GetProperty("items")
                .EnumerateArray()
                .Single(item => item.GetProperty("key").GetString() == "a");
            edited.TryGetProperty("before", out _).ShouldBeFalse();
            edited.TryGetProperty("after", out _).ShouldBeFalse();
            edited.GetProperty("edit")
                .GetProperty("changes")
                .EnumerateArray()
                .Select(change => change.GetProperty("member").GetString())
                .ShouldBe(["Name"]);
        }
        var structuralRestored = RoundTrip(structuralPayload).ToChangeSet();
        StructuralDictHolder.Patch
            .Between(structuralRestored.ToPatch().Apply(structuralBefore), structuralAfter)
            .IsEmpty
            .ShouldBeTrue();
    }

    [Test]
    public void SparsePayloadTransitionsComposeAcrossKeyedAndDictionaryValues()
    {
        var keyedInitial = KeyedState();
        var keyedAdded = KeyedState(Server("a", "A"));
        var keyedEdited = KeyedState(Server("a", "B", 1));
        var keyedAdd = RoundTrip(KeyedServerHolder.ChangeSet.Between(keyedInitial, keyedAdded).ToPayload())
            .ToChangeSet();
        var keyedEdit = RoundTrip(KeyedServerHolder.ChangeSet.Between(keyedAdded, keyedEdited).ToPayload())
            .ToChangeSet();
        var keyedAddThenEdit = keyedAdd.Compose(keyedEdit);
        keyedAddThenEdit.Items.GetChange("a").IsAdded.ShouldBeTrue();
        KeyedServerHolder.Patch
            .Between(keyedAddThenEdit.ToPatch().Apply(keyedInitial), keyedEdited)
            .IsEmpty
            .ShouldBeTrue();

        var keyedRemove = RoundTrip(KeyedServerHolder.ChangeSet.Between(keyedEdited, KeyedState()).ToPayload())
            .ToChangeSet();
        var keyedEditThenRemove = keyedEdit.Compose(keyedRemove);
        keyedEditThenRemove.Items.GetChange("a").IsRemoved.ShouldBeTrue();
        KeyedServerHolder.Patch
            .Between(keyedEditThenRemove.ToPatch().Apply(keyedAdded), KeyedState())
            .IsEmpty
            .ShouldBeTrue();

        var dictInitial = Optional<StructuralDictHolder.Fragment?>.Present(
            StructuralDictHolder.Fragment.From(new StructuralDictHolder { Servers = new() })
        );
        var dictAdded = Optional<StructuralDictHolder.Fragment?>.Present(
            StructuralDictHolder.Fragment.From(new StructuralDictHolder { Servers = new() { ["a"] = Server("a", "A") } })
        );
        var dictEdited = Optional<StructuralDictHolder.Fragment?>.Present(
            StructuralDictHolder.Fragment.From(new StructuralDictHolder { Servers = new() { ["a"] = Server("a", "B", 1) } })
        );
        var dictAdd = RoundTrip(StructuralDictHolder.ChangeSet.Between(dictInitial, dictAdded).ToPayload())
            .ToChangeSet();
        var dictEdit = RoundTrip(StructuralDictHolder.ChangeSet.Between(dictAdded, dictEdited).ToPayload())
            .ToChangeSet();
        var dictAddThenEdit = dictAdd.Compose(dictEdit);
        dictAddThenEdit.Servers.GetChange("a").IsAdded.ShouldBeTrue();
        StructuralDictHolder.Patch
            .Between(dictAddThenEdit.ToPatch().Apply(dictInitial), dictEdited)
            .IsEmpty
            .ShouldBeTrue();

        var dictRemove = RoundTrip(StructuralDictHolder.ChangeSet.Between(dictEdited, dictInitial).ToPayload())
            .ToChangeSet();
        var dictEditThenRemove = dictEdit.Compose(dictRemove);
        dictEditThenRemove.Servers.GetChange("a").IsRemoved.ShouldBeTrue();
        StructuralDictHolder.Patch
            .Between(dictEditThenRemove.ToPatch().Apply(dictAdded), dictInitial)
            .IsEmpty
            .ShouldBeTrue();
    }

    [Test]
    public void Payload_RejectsUnsupportedVersionsAndDuplicateMemberChanges()
    {
        var payload = new Settings.ChangeSetPayload { Version = 2 };
        Should.Throw<ArgumentException>(() => payload.ToChangeSet());

        var source = Settings.ChangeSet.Between(
            Optional<Settings.Fragment?>.Present(new Settings.Fragment { Label = Optional<string?>.Present("before") }),
            Optional<Settings.Fragment?>.Present(new Settings.Fragment { Label = Optional<string?>.Present("after") })
        );
        var duplicate = source.ToPayload();
        duplicate.Changes.Add(duplicate.Changes[0]);
        Should.Throw<ArgumentException>(() => duplicate.ToChangeSet());
    }

    [Test]
    public void Endpoint_RejectsNullValueStateWithoutValue()
    {
        var endpoint = new ChangeSetPayloadEndpoint<string>
        {
            State = ChangeSetPayloadState.Value,
        };
        Should.Throw<InvalidOperationException>(() => endpoint.ToOptional());
    }
}
