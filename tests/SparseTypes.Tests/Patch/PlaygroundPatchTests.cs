extern alias Playground;

using Playground::SparseTypes.Playground.Models;

namespace SparseTypes.Tests.Patch;

public sealed class PlaygroundPatchTests
{
    [Test]
    public void InvalidOrIncompatiblePayloadJsonThrows()
    {
        Should.Throw<System.Text.Json.JsonException>(() =>
            PlaygroundJson.ReadSettingsChangeSet("{")
        );
        Should.Throw<System.Text.Json.JsonException>(() =>
            PlaygroundJson.ReadSettingsChangeSet("""{"version":"0.1","changes":"invalid"}""")
        );
    }

    [Test]
    public void HelperBuiltPatchSurvivesPayloadRoundTripAndAppliesToItsBaseline()
    {
        var baseline = FragmentEditState.LowerDefaults().Build();
        var patch = new PatchEditState
        {
            EnabledOp = PatchKind.Keep,
            RetryOp = PatchKind.Set,
            RetryValue = 5,
            LabelOp = NullPatchKind.SetNull,
            NestedOp = NestedPatchKind.Set,
            HostOp = PatchKind.Remove,
            PortOp = PatchKind.Set,
            PortValue = 9000,
            PluginsOp = PatchKind.Remove,
        }.Build();
        var baselineState = Optional<PlaygroundSettings.Fragment?>.Present(baseline);
        var changes = PlaygroundSettings.ChangeSet.FromPatch(baselineState, patch);
        var imported = PlaygroundJson.ReadSettingsChangeSet(
            PlaygroundJson.WriteSettingsChangeSet(changes)
        );

        var expected = baseline.Apply(patch);
        var actual = baseline.Apply(imported.ToPatch());

        patch.Enabled.Kind.ShouldBe(FragmentOperationKind.Keep);
        patch.RetryCount.Kind.ShouldBe(FragmentOperationKind.Set);
        patch.Label.Kind.ShouldBe(FragmentOperationKind.Set);
        patch.Label.Value.ShouldBeNull();
        patch.Nested.Host.Kind.ShouldBe(FragmentOperationKind.Remove);
        patch.Nested.Port.Value.ShouldBe(9000);
        patch.Plugins.Kind.ShouldBe(FragmentOperationKind.Remove);
        PlaygroundJson.WriteFragment(actual).ShouldBe(PlaygroundJson.WriteFragment(expected));
    }
}
