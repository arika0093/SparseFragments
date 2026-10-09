using System.Text.Json;
using SparseFragments;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class NamingWidget
{
    [System.Text.Json.Serialization.JsonPropertyName("customName")]
    public string? Value { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("a/b")]
    public int Slash { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int Plain { get; set; }
}

public sealed class FragmentAndChangeSetJsonTests
{
    [Test]
    public void FragmentWireNamesRoundTrip()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        options.Converters.Add(NamingWidget.Fragment.JsonConverter);

        var fragment = new NamingWidget.Fragment
        {
            Value = Optional<string?>.Present("v"),
            Slash = Optional<int>.Present(7),
        };
        var json = JsonSerializer.Serialize(fragment, options);
        json.ShouldContain("customName");
        json.ShouldContain("a/b");

        var back = JsonSerializer.Deserialize<NamingWidget.Fragment>(json, options)!;
        back.Value.Value.ShouldBe("v");
        back.Slash.Value.ShouldBe(7);
        back.Plain.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void ChangeSetRoundTripsWithExplicitWireNames()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        options.Converters.Add(NamingWidget.Fragment.JsonConverter);

        Optional<NamingWidget.Fragment?> State(NamingWidget model) =>
            Optional<NamingWidget.Fragment?>.Present(NamingWidget.Fragment.From(model));
        var before = State(
            new NamingWidget
            {
                Value = "a",
                Slash = 1,
                Plain = 2,
            }
        );
        var after = State(
            new NamingWidget
            {
                Value = "b",
                Slash = 3,
                Plain = 2,
            }
        );

        var payload = NamingWidget.ChangeSet.Between(before, after).ToPayload();
        var json = JsonSerializer.Serialize(payload, options);
        json.ShouldContain("\"member\":\"Value\"");

        var restored = JsonSerializer
            .Deserialize<NamingWidget.ChangePayload>(json, options)!
            .ToChangeSet();
        NamingWidget.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void IgnoredOnlyChangeFailsTransportInsteadOfGoingSilent()
    {
        Optional<NamingWidget.Fragment?> State(NamingWidget model) =>
            Optional<NamingWidget.Fragment?>.Present(NamingWidget.Fragment.From(model));
        var before = State(
            new NamingWidget
            {
                Value = "a",
                Slash = 1,
                Plain = 2,
            }
        );
        var after = State(
            new NamingWidget
            {
                Value = "a",
                Slash = 1,
                Plain = 3,
            }
        );

        // The only modified property is JSON-ignored: the ChangeSet is
        // nonempty, but its payload would travel empty.
        var ignoredOnly = NamingWidget.ChangeSet.Between(before, after);
        ignoredOnly.IsEmpty.ShouldBeFalse();
        var omitted = Should.Throw<InvalidOperationException>(() => ignoredOnly.ToPayload());
        omitted.Message.ShouldContain("Plain");

        // A mixed ignored/non-ignored edit fails too rather than partially exporting.
        var mixed = NamingWidget.ChangeSet.Between(
            before,
            State(
                new NamingWidget
                {
                    Value = "b",
                    Slash = 1,
                    Plain = 3,
                }
            )
        );
        Should.Throw<InvalidOperationException>(() => mixed.ToPayload());

        // Patch-side export follows the same lossless rule.
        Should.Throw<InvalidOperationException>(() =>
            NamingWidget.ChangePayload.FromPatch(new NamingWidget.Patch { Plain = 3 })
        );
        NamingWidget.ChangePayload.FromPatch(new NamingWidget.Patch { Value = "b" });
    }
}
