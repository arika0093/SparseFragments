using System.Text.Json;
using SparseFragments;

namespace SparseFragments.Tests.Provenance;

public sealed class FragmentOriginViewTests
{
    private static OriginSettings.Fragment Layered()
    {
        var defaults = new OriginSettings.Fragment("defaults")
        {
            Theme = "light",
            Logging = true,
            Child = new OriginChild.Fragment { Host = "h", Port = 1 },
            Plugins = new List<string> { "a" },
            Tags = new HashSet<string> { "x" },
        };
        var tenant = new OriginSettings.Fragment("tenant")
        {
            Theme = "dark",
            Child = new OriginChild.Fragment { Port = 2 },
            Plugins = new List<string> { "b" },
            Tags = new HashSet<string> { "y" },
        };
        return defaults.Merge(tenant);
    }

    [Test]
    public void EnumerateOrigins_ListsEffectiveAttributions()
    {
        var entries = Layered()
            .EnumerateOrigins()
            .Where(entry => !entry.Path.ToString().StartsWith("Tags[", StringComparison.Ordinal))
            .Select(entry => (entry.Path.ToString(), entry.Origin))
            .ToArray();

        entries.ShouldBe(
            new (string, string?)[]
            {
                ("Child.Host", "defaults"),
                ("Child.Port", "tenant"),
                ("Logging", "defaults"),
                ("Plugins[0]", "defaults"),
                ("Plugins[1]", "tenant"),
                ("Theme", "tenant"),
            }
        );
    }

    [Test]
    public void EnumerateOrigins_OrdersTagsByEffectiveEnumeration()
    {
        var entries = Layered().EnumerateOrigins().ToArray();
        var tags = entries
            .Where(entry => entry.Path.ToString().StartsWith("Tags[", StringComparison.Ordinal))
            .ToArray();

        // The set carries one entry per effective element; positions follow
        // the effective enumeration order at query time.
        tags.Length.ShouldBe(2);
        tags.Select(entry => entry.Origin).ToArray().ShouldBe(new[] { "defaults", "tenant" });
    }

    [Test]
    public void GetOrigin_AbsentPathsReportUnknown()
    {
        var effective = Layered();

        effective
            .TryGetOrigin(SparsePath.Parse<OriginSettings>("Missing"), out var missing)
            .ShouldBeFalse();
        missing.ShouldBeNull();
        effective.GetOrigin(SparsePath.Parse<OriginSettings>("Missing")).ShouldBeNull();
        // Member-level reads over mixed collections report Unknown.
        effective.GetOrigin(OriginSettings.SparsePath.Plugins).ShouldBeNull();
        effective.GetOrigin(SparsePath.Parse<OriginSettings>("Child")).ShouldBeNull();
    }

    [Test]
    public void GetByOrigin_ProjectsReadOnlyEffectiveValues()
    {
        var tenant = Layered().GetByOrigin("tenant");

        tenant.Origin.ShouldBe("tenant");
        tenant.Theme.Value.ShouldBe("dark");
        tenant.Logging.IsPresent.ShouldBeFalse();
        tenant.Child.Value!.Port.Value.ShouldBe(2);
        tenant.Child.Value!.Host.IsPresent.ShouldBeFalse();
        tenant.Plugins.Value.ShouldBe(new List<string> { "b" });
        tenant.Tags.Value.ShouldBe(new HashSet<string> { "y" });
    }

    [Test]
    public void GetByOrigin_UnknownGroupHoldsUnattributedValues()
    {
        var merged = new OriginSettings.Fragment { Theme = "plain" }.Merge(
            new OriginSettings.Fragment("tenant") { Logging = true }
        );

        var unknown = merged.GetByOrigin(null);

        unknown.Origin.ShouldBeNull();
        unknown.Theme.Value.ShouldBe("plain");
        unknown.Logging.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void SplitByOrigin_GroupsInFirstSeenOrder()
    {
        var groups = Layered().SplitByOrigin();

        // First-seen order follows member order: Child.Host carries defaults.
        groups
            .Select(group => group.Origin)
            .ToArray()
            .ShouldBe(new string?[] { "defaults", "tenant" });
        groups[0].Fragment.Logging.Value.ShouldBeTrue();
        groups[1].Fragment.Theme.Value.ShouldBe("dark");
    }

    [Test]
    public void SplitByOrigin_GroupingIsLossy()
    {
        var lower = new OriginSettings.Fragment("defaults") { Theme = "light" };
        var higher = new OriginSettings.Fragment("tenant") { Theme = "dark" };
        var effective = lower.Merge(higher);

        // The overwritten lower value survives in no group: grouping exposes
        // effective values, never the hidden layers.
        var regrouped = effective
            .SplitByOrigin()
            .Select(group => group.Fragment.Theme)
            .Where(theme => theme.IsPresent)
            .Select(theme => theme.Value)
            .ToArray();

        regrouped.ShouldBe(new[] { "dark" });
        effective.SplitByOrigin().Sum(group => group.Fragment.IsEmpty ? 0 : 1).ShouldBe(1);
    }

    [Test]
    public void Origins_DoNotChangeSemanticDiffOrChangeSet()
    {
        var first = new OriginSettings.Fragment("one") { Theme = "same", Logging = true };
        var second = new OriginSettings.Fragment("two") { Theme = "same", Logging = true };

        var diff = OriginSettings.Fragment.Diff(
            new OriginSettings { Theme = "same", Logging = true },
            new OriginSettings { Theme = "same", Logging = true }
        );
        diff.IsEmpty.ShouldBeTrue();

        var changes = OriginSettings.ChangeSet.Between(
            Optional<OriginSettings.Fragment?>.Present(first),
            Optional<OriginSettings.Fragment?>.Present(second)
        );
        changes.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void Origins_DoNotLeakIntoFragmentJson()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(OriginSettings.Fragment.JsonConverter);

        var plain = new OriginSettings.Fragment { Theme = "a" };
        var attributed = new OriginSettings.Fragment("tenant") { Theme = "a" };

        var plainJson = JsonSerializer.Serialize(plain, options);
        var attributedJson = JsonSerializer.Serialize(attributed, options);

        attributedJson.ShouldBe(plainJson);
        var restored = JsonSerializer.Deserialize<OriginSettings.Fragment>(attributedJson, options);
        restored.ShouldNotBeNull();
        restored!.Origin.ShouldBeNull();
        restored.Theme.Value.ShouldBe("a");
    }

    [Test]
    public void Origins_DoNotChangeChangePayloadJson()
    {
        var first = new OriginSettings.Fragment("one") { Theme = "a" };
        var second = new OriginSettings.Fragment("two") { Theme = "b" };
        var plainFirst = new OriginSettings.Fragment { Theme = "a" };
        var plainSecond = new OriginSettings.Fragment { Theme = "b" };

        var attributedJson = JsonSerializer.Serialize(
            OriginSettings
                .ChangeSet.Between(
                    Optional<OriginSettings.Fragment?>.Present(first),
                    Optional<OriginSettings.Fragment?>.Present(second)
                )
                .ToPayload()
        );
        var plainJson = JsonSerializer.Serialize(
            OriginSettings
                .ChangeSet.Between(
                    Optional<OriginSettings.Fragment?>.Present(plainFirst),
                    Optional<OriginSettings.Fragment?>.Present(plainSecond)
                )
                .ToPayload()
        );

        attributedJson.ShouldBe(plainJson);
    }

    [Test]
    public void PatchApply_ResetsAttributionToUnknown()
    {
        var basis = new OriginSettings.Fragment("base") { Theme = "a", Logging = true };
        var patched = basis.Apply(new OriginSettings.Patch { Theme = "b" });

        // Patch operations carry no origin metadata, so the result resets to
        // Unknown rather than mixing unattributed edits with base provenance.
        patched.Origin.ShouldBeNull();
        patched.Theme.Value.ShouldBe("b");
        patched.GetOrigin(OriginSettings.SparsePath.Theme).ShouldBeNull();
        patched.GetOrigin(OriginSettings.SparsePath.Logging).ShouldBeNull();
    }

    [Test]
    public void SparsePath_TypedBuildersBindToOriginQueries()
    {
        // Canonical typed paths (issue #202) bind to the same origin queries
        // without signature changes via the typed-to-untyped conversion.
        SparsePath root = SparsePath.Root<OriginSettings>();
        root.IsRoot.ShouldBeTrue();
        root.Depth.ShouldBe(0);
        root.ToString().ShouldBe("$root");

        SparsePath theme = OriginSettings.SparsePath.Theme;
        theme.ToString().ShouldBe("Theme");
        theme.Depth.ShouldBe(1);

        SparsePath childHost = OriginSettings.SparsePath.Child.Host;
        childHost.ToString().ShouldBe("Child.Host");
        childHost.Depth.ShouldBe(2);

        var element = SparsePath.Root<OriginSettings>().Member("Tags").At(2);
        element.ToString().ShouldBe("Tags[2]");

        var childPrefix = SparsePath.Root<OriginSettings>().Member("Child");
        var hostSuffix = SparsePath.Parse<OriginChild>("Host");
        var combined = childPrefix.Append(hostSuffix);
        combined.ShouldBe(childHost);

        var parsed = SparsePath.Parse<OriginSettings>("Child.Host");
        parsed.ShouldBe(childHost);
        parsed.Parent.ShouldBe(SparsePath.Root<OriginSettings>().Member("Child"));
        parsed.IsDescendantOf(SparsePath.Root<OriginSettings>().Member("Child")).ShouldBeTrue();

        // Typed and parsed paths resolve through the same origin surface.
        var effective = Layered();
        effective.GetOrigin(OriginSettings.SparsePath.Theme).ShouldBe("tenant");
        effective.GetOrigin(SparsePath.Parse<OriginSettings>("Theme")).ShouldBe("tenant");
        effective.GetOrigin(OriginSettings.SparsePath.Child.Host).ShouldBe("defaults");
    }
}
