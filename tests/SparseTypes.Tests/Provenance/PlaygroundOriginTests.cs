using SparseTypes.Playground.Models;

namespace SparseTypes.Tests.Provenance;

public sealed class PlaygroundOriginTests
{
    [Test]
    public void LayerMergeShowsScalarNestedAppendAndSetUnionOrigins()
    {
        var lower = FragmentEditState.LowerDefaults(includeTags: true).Build("lower");
        var higher = FragmentEditState.HigherDefaults(includeTags: true).Build("higher");
        var merged = lower.Merge(higher);

        merged.GetOrigin(PlaygroundSettings.SparsePath.Enabled).ShouldBe("lower");
        merged.GetOrigin(PlaygroundSettings.SparsePath.RetryCount).ShouldBe("lower");
        merged.GetOrigin(PlaygroundSettings.SparsePath.Label).ShouldBe("higher");
        merged.GetOrigin(PlaygroundSettings.SparsePath.Nested.Host).ShouldBe("lower");
        merged.GetOrigin(PlaygroundSettings.SparsePath.Nested.Port).ShouldBe("higher");
        merged
            .GetOrigin(SparsePath.Root<PlaygroundSettings>().Member("Plugins").At(1))
            .ShouldBe("higher");

        var tags = merged.Tags.Value!.ToArray();
        tags.Length.ShouldBe(3);
        foreach (var tag in tags)
        {
            var index = Array.IndexOf(tags, tag);
            merged
                .GetOrigin(SparsePath.Root<PlaygroundSettings>().Member("Tags").At(index))
                .ShouldBe(tag == "tenant-tag" ? "higher" : "lower");
        }
    }

    [Test]
    public void FragmentOriginLabelsDoNotChangeSerializedValues()
    {
        var lower = FragmentEditState.LowerDefaults(includeTags: true).Build("lower");
        var sameValues = FragmentEditState.LowerDefaults(includeTags: true).Build("another-layer");

        lower.Origin.ShouldBe("lower");
        sameValues.Origin.ShouldBe("another-layer");
        PlaygroundJson.WriteFragment(lower).ShouldBe(PlaygroundJson.WriteFragment(sameValues));
    }
}
