using SparseFragments.NativeAotFixtures;

namespace SparseFragments.NativeAotSmoke;

/// <summary>
/// Standalone emission defaults under NativeAOT (issue #121).
/// The default feature selection emits every family without extra
/// configuration; these types resolve at compile time only then.
/// </summary>
public sealed class AotEmissionDefaultsTests
{
    [Test]
    public async Task StandaloneDefaults_ExposeEveryFamily()
    {
        // Compile-time presence: fragment families stay nested; UI/editing
        // types live in the configured generated namespace (issue #190).
        _ = typeof(PayloadRoot.Fragment);
        _ = typeof(PayloadRoot.Patch);
        _ = typeof(PayloadRoot.ChangeSet);
        _ = typeof(PayloadRoot.ChangePayload);
        _ = typeof(PayloadRoot.FragmentBuilder);
        // NOTE: no typeof(PayloadRoot).GetNestedTypes() enumeration here.
        // Enumerating nested types roots every nested type (including the
        // fragment JSON converter and ChangePayload enum converters) and
        // surfaces transitive IL2026/IL3050 diagnostics under PublishAot.
        var session = new PayloadRoot { Label = "a" }.CreateEditSession();
        await Assert
            .That(
                session
                    .Observable.GetType()
                    .Namespace!.StartsWith("SparseFragments.Generated", StringComparison.Ordinal)
            )
            .IsTrue();
        await Assert
            .That(
                session
                    .Current.GetType()
                    .Namespace!.StartsWith("SparseFragments.Generated", StringComparison.Ordinal)
            )
            .IsTrue();
        await Assert
            .That(
                session
                    .GetType()
                    .Namespace!.StartsWith("SparseFragments.Generated", StringComparison.Ordinal)
            )
            .IsTrue();
        await Assert.That(typeof(PayloadRoot.Fragment).Name).IsEqualTo("Fragment");
    }

    [Test]
    public async Task StandaloneDefaults_PayloadRoundTripsCompletely()
    {
        Optional<PayloadRoot.Fragment?> Before(string label) =>
            Optional<PayloadRoot.Fragment?>.Present(
                PayloadRoot.Fragment.From(new PayloadRoot { Label = label })
            );

        var changes = PayloadRoot.ChangeSet.Between(Before("old"), Before("new"));
        var payload = changes.ToPayload();

        await Assert.That(payload.Version).IsEqualTo("0.1");
        var restored = payload.ToChangeSet();
        await Assert.That(restored.IsEmpty).IsFalse();
        var replayed = restored.ToPatch().Apply(Before("old"));
        await Assert.That(PayloadRoot.Patch.Between(replayed, Before("new")).IsEmpty).IsTrue();
    }

    [Test]
    public async Task StandaloneDefaults_WriteToTargetsReadModel()
    {
        var fragment = PayloadRoot.Fragment.From(new PayloadRoot { Label = "a" });
        var target = new PayloadRoot { Label = "b" };
        fragment.WriteTo(target);

        await Assert.That(target.Label).IsEqualTo("a");
    }
}
