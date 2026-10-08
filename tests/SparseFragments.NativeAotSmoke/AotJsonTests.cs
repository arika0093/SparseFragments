using System.Text;
using System.Text.Json;
using SparseFragments.NativeAotFixtures;

namespace SparseFragments.NativeAotSmoke;

public sealed class AotJsonTests
{
    private static JsonSerializerOptions AotOptions()
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = AotSerializerContext.Default };
        options.Converters.Add(new AotWidget.Fragment.FragmentJsonConverter());
        return options;
    }

    // Serializes through the generated converter directly (the same path the
    // Patch converter uses for fragments), which stays trim/NativeAOT
    // clean unlike the reflection-dispatched JsonSerializer.Serialize overloads.
    private static string ToCanonicalJson(
        AotWidget.Fragment fragment,
        JsonSerializerOptions options
    )
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            new AotWidget.Fragment.FragmentJsonConverter().Write(writer, fragment, options);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static AotWidget.Fragment JsonBaseline() =>
        new()
        {
            Name = Optional<string?>.Present("a"),
            Count = Optional<int>.Present(1),
            Nested = Optional<AotNested.Fragment?>.Present(
                new AotNested.Fragment { Host = Optional<string>.Present("h") }
            ),
        };

    [Test]
    public async Task FragmentSerializesPresentMembersAndOmitsMissing()
    {
        var canonical = ToCanonicalJson(JsonBaseline(), AotOptions());

        await Assert.That(canonical.Contains("\"Name\":\"a\"")).IsTrue();
        await Assert.That(!canonical.Contains("Plugins")).IsTrue();
    }

    [Test]
    public async Task FragmentPreservesExplicitNullInJson()
    {
        var explicitNullFragment = new AotWidget.Fragment
        {
            Name = Optional<string?>.Present(null),
        };

        await Assert
            .That(ToCanonicalJson(explicitNullFragment, AotOptions()).Contains("\"Name\":null"))
            .IsTrue();
    }

    [Test]
    public async Task ChangePayloadRoundTripsWithSourceGenAndNormalizedJson()
    {
        Optional<PayloadRoot.Fragment?> WidgetState(string label, string name) =>
            Optional<PayloadRoot.Fragment?>.Present(
                PayloadRoot.Fragment.From(
                    new PayloadRoot
                    {
                        Label = label,
                        Child = new PayloadChild { Name = name, Count = 1 },
                    }
                )
            );

        var widgetBefore = WidgetState("old", "h1");
        var widgetAfter = WidgetState("new", "h2");
        var changes = PayloadRoot.ChangeSet.Between(widgetBefore, widgetAfter);
        var json = JsonSerializer.Serialize(
            changes.ToPayload(),
            AotSerializerContext.Default.PayloadRootChangePayload
        );
        using (var document = JsonDocument.Parse(json))
        {
            await Assert
                .That(
                    document
                        .RootElement.EnumerateObject()
                        .Select(property => property.Name)
                        .SequenceEqual(["version", "changes"])
                )
                .IsTrue();
            await Assert
                .That(document.RootElement.GetProperty("version").GetString())
                .IsEqualTo("0.1");
            var nested = document
                .RootElement.GetProperty("changes")
                .EnumerateArray()
                .Single(change => change.GetProperty("member").GetString() == "Child")
                .GetProperty("nested");
            await Assert.That(nested.TryGetProperty("version", out _)).IsFalse();
            await Assert.That(nested.TryGetProperty("changes", out _)).IsTrue();
            var endpoint = document
                .RootElement.GetProperty("changes")
                .EnumerateArray()
                .Single(change => change.GetProperty("member").GetString() == "Label")
                .GetProperty("after");
            await Assert
                .That(
                    endpoint
                        .EnumerateObject()
                        .Select(property => property.Name)
                        .SequenceEqual(["state", "value"])
                )
                .IsTrue();
        }
        await Assert.That(json.Contains("\"state\":\"value\"")).IsTrue();
        var restored = JsonSerializer
            .Deserialize(json, AotSerializerContext.Default.PayloadRootChangePayload)!
            .ToChangeSet();
        await Assert
            .That(
                PayloadRoot
                    .Patch.Between(restored.ToPatch().Apply(widgetBefore), widgetAfter)
                    .IsEmpty
            )
            .IsTrue();

        Optional<PayloadCollection.Fragment?> ServerState(params PayloadItem[] items) =>
            Optional<PayloadCollection.Fragment?>.Present(
                PayloadCollection.Fragment.From(new PayloadCollection { Items = items.ToList() })
            );
        var serverBefore = ServerState(new PayloadItem { Id = "a", Name = "A" });
        var serverAfter = ServerState(
            new PayloadItem { Id = "a", Name = "A2" },
            new PayloadItem { Id = "b", Name = "B" }
        );
        var serverChanges = PayloadCollection.ChangeSet.Between(serverBefore, serverAfter);
        var serverJson = JsonSerializer.Serialize(
            serverChanges.ToPayload(),
            AotSerializerContext.Default.PayloadCollectionChangePayload
        );
        using var serverDocument = JsonDocument.Parse(serverJson);
        var serverItems = serverDocument
            .RootElement.GetProperty("changes")
            .EnumerateArray()
            .Single(change => change.GetProperty("member").GetString() == "Items")
            .GetProperty("items");
        var editedItem = serverItems
            .EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == "a");
        await Assert
            .That(
                editedItem
                    .EnumerateObject()
                    .Select(property => property.Name)
                    .SequenceEqual(["key", "kind", "beforeIndex", "afterIndex", "edit"])
            )
            .IsTrue();
        await Assert.That(editedItem.GetProperty("kind").GetString()).IsEqualTo("edit");
        await Assert
            .That(
                serverItems
                    .EnumerateArray()
                    .Single(item => item.GetProperty("key").GetString() == "b")
                    .GetProperty("kind")
                    .GetString()
            )
            .IsEqualTo("add");
        var serverRestored = JsonSerializer
            .Deserialize(
                serverJson,
                AotSerializerContext.Default.PayloadCollectionChangePayload
            )!
            .ToChangeSet();
        await Assert
            .That(
                PayloadCollection
                    .Patch.Between(serverRestored.ToPatch().Apply(serverBefore), serverAfter)
                    .IsEmpty
            )
            .IsTrue();
    }

    [Test]
    public async Task ChangePayloadRedactsBeforeStateWithSourceGen()
    {
        Optional<PayloadSecret.Fragment?> SecretState(string? label, string? token) =>
            Optional<PayloadSecret.Fragment?>.Present(
                PayloadSecret.Fragment.From(new PayloadSecret { Label = label, Token = token })
            );

        var before = SecretState("before", "before-token");
        var after = SecretState("after", "after-token");
        var json = JsonSerializer.Serialize(
            PayloadSecret.ChangeSet.Between(before, after).ToPayload(),
            AotSerializerContext.Default.PayloadSecretChangePayload
        );

        await Assert.That(json.Contains("\"version\":\"0.1\"")).IsTrue();
        await Assert.That(json.Contains("\"state\":\"redacted\"")).IsTrue();
        await Assert.That(json.Contains("before-token")).IsFalse();

        var restored = JsonSerializer
            .Deserialize(json, AotSerializerContext.Default.PayloadSecretChangePayload)!
            .ToPatch();
        await Assert
            .That(PayloadSecret.Patch.Between(restored.Apply(before), after).IsEmpty)
            .IsTrue();

        var incomplete = JsonSerializer.Deserialize(
            json,
            AotSerializerContext.Default.PayloadSecretChangePayload
        )!;
        var rejected = false;
        try
        {
            incomplete.ToChangeSet();
        }
        catch (ArgumentException)
        {
            rejected = true;
        }

        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task ObservableCollectionViewsRemainAotSafe()
    {
        var numbers = new List<int> { 1 };
        var model = new AotIntCollections { Numbers = numbers };
        var notified = 0;
        var session = model.CreateEditSession(onChanged: () => notified++);

        session.Observable.Numbers.Add(2);
        session.Observable.Scores.Add("a", 3);

        await Assert.That(ReferenceEquals(numbers, model.Numbers)).IsTrue();
        await Assert.That(model.Numbers.Count).IsEqualTo(2);
        await Assert.That(model.Scores["a"]).IsEqualTo(3);
        await Assert.That(notified).IsEqualTo(2);
    }
}
