using SparseFragments;

namespace SparseFragments.Tests.Provenance;

// Feature models for fragment origin attribution (issue #204). The view
// tests reuse these models; each shape below exercises one propagation rule.
[SparseFragmentModel]
public partial class OriginChild
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }
}

[SparseFragmentModel]
public partial class OriginSettings
{
    public string? Theme { get; set; }

    public bool Logging { get; set; }

    public OriginChild? Child { get; set; }

    [SparseMerge(MergeMode.Append)]
    public List<string> Plugins { get; set; } = new();

    [SparseMerge(MergeMode.SetUnion)]
    public HashSet<string> Tags { get; set; } = new(StringComparer.Ordinal);

    [SparseMerge(MergeMode.Replace)]
    public List<string> Replaced { get; set; } = new();
}

public sealed class OriginLastWriteStrategy : FragmentMergeStrategy<string?>
{
    public override Optional<string?> Merge(Optional<string?> lower, Optional<string?> higher) =>
        higher.IsPresent ? higher : lower;

    public override bool AreEqual(string? left, string? right) => left == right;
}

[SparseFragmentModel]
public partial class OriginCustom
{
    [SparseMerge(typeof(OriginLastWriteStrategy))]
    public string? Note { get; set; }

    public string? Plain { get; set; }
}

[SparseFragmentModel]
public partial class OriginCaseSettings
{
    [SparseMerge(MergeMode.SetUnion)]
    public HashSet<string> Names { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FragmentOriginMergeTests
{
    [Test]
    public void Constructors_DefaultToUnknown()
    {
        new OriginSettings.Fragment().Origin.ShouldBeNull();
        new OriginSettings.Fragment(null).Origin.ShouldBeNull();
        new OriginSettings.Fragment("tenant").Origin.ShouldBe("tenant");
    }

    [Test]
    public void Merge_PropagatesScalarOrigins()
    {
        var defaults = new OriginSettings.Fragment("defaults") { Theme = "light", Logging = true };
        var tenant = new OriginSettings.Fragment("tenant") { Theme = "dark" };

        var effective = defaults.Merge(tenant);

        effective.Theme.Value.ShouldBe("dark");
        effective.Logging.Value.ShouldBeTrue();
        effective.GetOrigin(OriginSettings.SparsePath.Theme).ShouldBe("tenant");
        effective.GetOrigin(OriginSettings.SparsePath.Logging).ShouldBe("defaults");
    }

    [Test]
    public void Merge_LayersThreeLevelsInOrder()
    {
        var first = new OriginSettings.Fragment("one") { Theme = "a", Logging = true };
        var second = new OriginSettings.Fragment("two") { Theme = "b" };
        var third = new OriginSettings.Fragment("three") { Logging = false };

        var effective = first.Merge(second).Merge(third);

        effective.GetOrigin(OriginSettings.SparsePath.Theme).ShouldBe("two");
        effective.GetOrigin(OriginSettings.SparsePath.Logging).ShouldBe("three");
    }

    [Test]
    public void Merge_SameValueKeepsHigherOrigin()
    {
        var lower = new OriginSettings.Fragment("one") { Theme = "same" };
        var higher = new OriginSettings.Fragment("two") { Theme = "same" };

        var effective = lower.Merge(higher);

        effective.Theme.Value.ShouldBe("same");
        effective.GetOrigin(OriginSettings.SparsePath.Theme).ShouldBe("two");
    }

    [Test]
    public void Merge_MissingFallsThroughAndPresentNullResetsWithAttribution()
    {
        var lower = new OriginSettings.Fragment("one") { Theme = "low" };

        lower
            .Merge(new OriginSettings.Fragment())
            .GetOrigin(OriginSettings.SparsePath.Theme)
            .ShouldBe("one");

        var reset = lower.Merge(
            new OriginSettings.Fragment("two") { Theme = Optional<string?>.Present(null) }
        );
        reset.Theme.IsPresent.ShouldBeTrue();
        reset.Theme.Value.ShouldBeNull();
        reset.GetOrigin(OriginSettings.SparsePath.Theme).ShouldBe("two");
    }

    [Test]
    public void Merge_DeepAttributesLeavesAndChildOriginOverridesParent()
    {
        var lower = new OriginSettings.Fragment("defaults")
        {
            Child = new OriginChild.Fragment { Host = "lower-host", Port = 1 },
        };
        var higher = new OriginSettings.Fragment("tenant")
        {
            Child = new OriginChild.Fragment("overrides") { Port = 2 },
        };

        var effective = lower.Merge(higher);
        var child = effective.Child.Value!;

        // Host falls through from the lower child, inheriting the lower
        // fragment default because the nested fragment names no origin.
        child.Host.Value.ShouldBe("lower-host");
        effective.GetOrigin(OriginSettings.SparsePath.Child.Host).ShouldBe("defaults");
        // Port comes from the explicitly originated higher child, which
        // overrides the enclosing tenant default.
        child.Port.Value.ShouldBe(2);
        effective.GetOrigin(OriginSettings.SparsePath.Child.Port).ShouldBe("overrides");
    }

    [Test]
    public void Merge_NonOriginatedChildInheritsParentDefault()
    {
        var lower = new OriginSettings.Fragment("defaults")
        {
            Child = new OriginChild.Fragment { Host = "h", Port = 1 },
        };
        var higher = new OriginSettings.Fragment("tenant")
        {
            Child = new OriginChild.Fragment { Port = 2 },
        };

        var effective = lower.Merge(higher);

        effective.Child.Value!.Host.Value.ShouldBe("h");
        effective.GetOrigin(OriginSettings.SparsePath.Child.Host).ShouldBe("defaults");
        effective.GetOrigin(OriginSettings.SparsePath.Child.Port).ShouldBe("tenant");
    }

    [Test]
    public void Merge_AppendTracksElementRanges()
    {
        var lower = new OriginSettings.Fragment("base")
        {
            Plugins = new List<string> { "a", "b" },
        };
        var higher = new OriginSettings.Fragment("extra") { Plugins = new List<string> { "c" } };

        var effective = lower.Merge(higher);

        effective.Plugins.Value.ShouldBe(new List<string> { "a", "b", "c" });
        effective.GetOrigin(SparsePath.Parse<OriginSettings>("Plugins[0]")).ShouldBe("base");
        effective.GetOrigin(SparsePath.Parse<OriginSettings>("Plugins[1]")).ShouldBe("base");
        effective.GetOrigin(SparsePath.Parse<OriginSettings>("Plugins[2]")).ShouldBe("extra");
    }

    [Test]
    public void Merge_SetUnionAttributesFirstAcceptedContributor()
    {
        var lower = new OriginSettings.Fragment("base")
        {
            Tags = new HashSet<string> { "a", "b" },
        };
        var higher = new OriginSettings.Fragment("extra")
        {
            Tags = new HashSet<string> { "b", "c" },
        };

        var merged = lower.Merge(higher);
        var values = merged.Tags.Value!.ToList();

        values.Count.ShouldBe(3);
        foreach (var value in values)
        {
            var index = values.IndexOf(value);
            merged
                .GetOrigin(SparsePath.Parse<OriginSettings>("Tags[" + index + "]"))
                .ShouldBe(value == "c" ? "extra" : "base");
        }
    }

    [Test]
    public void Merge_SetUnionHonorsCustomComparer()
    {
        var lower = new OriginCaseSettings.Fragment("base")
        {
            Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Apple" },
        };
        var higher = new OriginCaseSettings.Fragment("extra")
        {
            Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "apple", "Banana" },
        };

        var merged = lower.Merge(higher);
        var values = merged.Names.Value!.ToList();

        // "apple" duplicates "Apple" under the member comparer, so the lower
        // contribution wins and keeps the base attribution.
        values.Count.ShouldBe(2);
        foreach (var value in values)
        {
            var index = values.IndexOf(value);
            merged
                .GetOrigin(SparsePath.Parse<OriginCaseSettings>("Names[" + index + "]"))
                .ShouldBe(
                    string.Equals(value, "Banana", StringComparison.Ordinal) ? "extra" : "base"
                );
        }
    }

    [Test]
    public void Merge_ReplaceAttributesWholeValue()
    {
        var lower = new OriginSettings.Fragment("base") { Replaced = new List<string> { "a" } };
        var higher = new OriginSettings.Fragment("extra") { Replaced = new List<string> { "b" } };

        var effective = lower.Merge(higher);

        effective.Replaced.Value.ShouldBe(new List<string> { "b" });
        effective.GetOrigin(OriginSettings.SparsePath.Replaced).ShouldBe("extra");
        lower
            .Merge(new OriginSettings.Fragment())
            .GetOrigin(OriginSettings.SparsePath.Replaced)
            .ShouldBe("base");
    }

    [Test]
    public void Merge_CustomStrategyFallsBackToUnknown()
    {
        var lower = new OriginCustom.Fragment("base") { Note = "low", Plain = "low" };
        var higher = new OriginCustom.Fragment("high") { Note = "high", Plain = "high" };

        var effective = lower.Merge(higher);

        effective.Note.Value.ShouldBe("high");
        effective.GetOrigin(OriginCustom.SparsePath.Note).ShouldBeNull();
        effective.GetOrigin(OriginCustom.SparsePath.Plain).ShouldBe("high");
    }

    [Test]
    public void Merge_WithoutOriginsAllocatesNoAttributionState()
    {
        var lower = new OriginSettings.Fragment { Theme = "a" };
        var higher = new OriginSettings.Fragment { Theme = "b" };

        var effective = lower.Merge(higher);

        effective.Origin.ShouldBeNull();
        effective.__SparseMemberOrigins.ShouldBeNull();
        effective.__SparseElementOrigins.ShouldBeNull();
        effective.TryGetOrigin(OriginSettings.SparsePath.Theme, out var origin).ShouldBeTrue();
        origin.ShouldBeNull();
    }

    [Test]
    public void DeepClone_PreservesOrigins()
    {
        var defaults = new OriginSettings.Fragment("defaults") { Theme = "light" };
        var tenant = new OriginSettings.Fragment("tenant") { Theme = "dark" };

        var clone = defaults.Merge(tenant).DeepClone();

        clone.Origin.ShouldBe("tenant");
        clone.GetOrigin(OriginSettings.SparsePath.Theme).ShouldBe("tenant");
        clone.GetOrigin(OriginSettings.SparsePath.Logging).ShouldBeNull();
    }

    [Test]
    public void Builder_RoundTripPreservesOrigins()
    {
        var effective = new OriginSettings.Fragment("defaults") { Theme = "light" }.Merge(
            new OriginSettings.Fragment("tenant") { Theme = "dark" }
        );

        var rebuilt = effective.ToBuilder().Build();

        rebuilt.GetOrigin(OriginSettings.SparsePath.Theme).ShouldBe("tenant");
        rebuilt.Origin.ShouldBe(effective.Origin);
    }

    [Test]
    public void ApplyChanges_ResetsAppliedMembersAndKeepsBaseAttribution()
    {
        var basis = new OriginSettings.Fragment("base") { Theme = "a", Logging = true };
        var changes = new OriginSettings.Fragment { Theme = "b" };

        var applied = basis.ApplyChanges(changes);

        applied.Theme.Value.ShouldBe("b");
        applied.GetOrigin(OriginSettings.SparsePath.Theme).ShouldBeNull();
        applied.GetOrigin(OriginSettings.SparsePath.Logging).ShouldBe("base");
    }
}
