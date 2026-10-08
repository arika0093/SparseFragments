using SparseFragments;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class DefaultMergeGrandchild
{
    public string Value { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class DefaultMergeChild
{
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }

    public DefaultMergeGrandchild? Grandchild { get; set; }
}

[SparseFragmentModel]
public partial class DefaultMergeParent
{
    public string? Title { get; set; }

    public DefaultMergeChild? Child { get; set; }

    public List<string> Tags { get; set; } = new();
}

[SparseFragmentModel]
public partial class ReplaceMergeParent
{
    [SparseMerge(MergeMode.Replace)]
    public DefaultMergeChild? Child { get; set; }
}

[SparseFragmentModel]
public partial class ExplicitDefaultMergeParent
{
    [SparseMerge(MergeMode.Default)]
    public DefaultMergeChild? Child { get; set; }
}

public sealed class DefaultMergeModeTests
{
    [Test]
    public void DefaultIsZeroAndExplicitModesAreStable()
    {
        ((int)MergeMode.Default).ShouldBe(0);
        ((int)MergeMode.Replace).ShouldBe(1);
        ((int)MergeMode.Deep).ShouldBe(2);
        ((int)MergeMode.Append).ShouldBe(3);
        ((int)MergeMode.SetUnion).ShouldBe(4);
        ((int)MergeMode.Custom).ShouldBe(5);
    }

    [Test]
    public void ScalarDefault_PresentHighReplacesLow()
    {
        var lower = new DefaultMergeParent.Fragment { Title = Optional<string?>.Present("low") };
        var higher = new DefaultMergeParent.Fragment { Title = Optional<string?>.Present("high") };

        lower.Merge(higher).Title.Value.ShouldBe("high");
    }

    [Test]
    public void ScalarDefault_MissingHighPreservesLowAndPresentNullReplaces()
    {
        var lower = new DefaultMergeParent.Fragment { Title = Optional<string?>.Present("low") };

        lower.Merge(new DefaultMergeParent.Fragment()).Title.Value.ShouldBe("low");
        lower
            .Merge(new DefaultMergeParent.Fragment { Title = Optional<string?>.Present(null) })
            .Title.IsPresent.ShouldBeTrue();
        lower
            .Merge(new DefaultMergeParent.Fragment { Title = Optional<string?>.Present(null) })
            .Title.Value.ShouldBeNull();
    }

    [Test]
    public void NestedDefault_DeepMergesPreservingLowMembers()
    {
        var lower = new DefaultMergeParent.Fragment
        {
            Child = Optional<DefaultMergeChild.Fragment?>.Present(
                new DefaultMergeChild.Fragment
                {
                    Name = Optional<string>.Present("low-name"),
                    Count = Optional<int>.Present(1),
                }
            ),
        };
        var higher = new DefaultMergeParent.Fragment
        {
            Child = Optional<DefaultMergeChild.Fragment?>.Present(
                new DefaultMergeChild.Fragment { Count = Optional<int>.Present(2) }
            ),
        };

        var merged = lower.Merge(higher).Child.Value!;

        merged.Name.Value.ShouldBe("low-name");
        merged.Count.Value.ShouldBe(2);
    }

    [Test]
    public void NestedDefault_MergesRecursively()
    {
        var lower = new DefaultMergeParent.Fragment
        {
            Child = Optional<DefaultMergeChild.Fragment?>.Present(
                new DefaultMergeChild.Fragment
                {
                    Grandchild = Optional<DefaultMergeGrandchild.Fragment?>.Present(
                        new DefaultMergeGrandchild.Fragment
                        {
                            Value = Optional<string>.Present("low-value"),
                        }
                    ),
                }
            ),
        };
        var higher = new DefaultMergeParent.Fragment
        {
            Child = Optional<DefaultMergeChild.Fragment?>.Present(
                new DefaultMergeChild.Fragment { Name = Optional<string>.Present("high-name") }
            ),
        };

        var merged = lower.Merge(higher).Child.Value!;

        merged.Name.Value.ShouldBe("high-name");
        merged.Grandchild.Value!.Value.Value.ShouldBe("low-value");
    }

    [Test]
    public void NestedDefault_MissingHighPreservesLowAndPresentNullReplaces()
    {
        var lower = new DefaultMergeParent.Fragment
        {
            Child = Optional<DefaultMergeChild.Fragment?>.Present(
                new DefaultMergeChild.Fragment { Name = Optional<string>.Present("low") }
            ),
        };

        lower.Merge(new DefaultMergeParent.Fragment()).Child.Value!.Name.Value.ShouldBe("low");

        var nulled = lower.Merge(
            new DefaultMergeParent.Fragment
            {
                Child = Optional<DefaultMergeChild.Fragment?>.Present(null),
            }
        );

        nulled.Child.IsPresent.ShouldBeTrue();
        nulled.Child.Value.ShouldBeNull();
    }

    [Test]
    public void ExplicitReplace_RestoresAtomicReplacement()
    {
        var lower = new ReplaceMergeParent.Fragment
        {
            Child = Optional<DefaultMergeChild.Fragment?>.Present(
                new DefaultMergeChild.Fragment
                {
                    Name = Optional<string>.Present("low-name"),
                    Count = Optional<int>.Present(1),
                }
            ),
        };
        var higher = new ReplaceMergeParent.Fragment
        {
            Child = Optional<DefaultMergeChild.Fragment?>.Present(
                new DefaultMergeChild.Fragment { Count = Optional<int>.Present(2) }
            ),
        };

        var merged = lower.Merge(higher).Child.Value!;

        merged.Count.Value.ShouldBe(2);
        merged.Name.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void ExplicitDefault_BehavesLikeShapeAwareDefault()
    {
        var lower = new ExplicitDefaultMergeParent.Fragment
        {
            Child = Optional<DefaultMergeChild.Fragment?>.Present(
                new DefaultMergeChild.Fragment { Name = Optional<string>.Present("low-name") }
            ),
        };
        var higher = new ExplicitDefaultMergeParent.Fragment
        {
            Child = Optional<DefaultMergeChild.Fragment?>.Present(
                new DefaultMergeChild.Fragment { Count = Optional<int>.Present(7) }
            ),
        };

        var merged = lower.Merge(higher).Child.Value!;

        merged.Name.Value.ShouldBe("low-name");
        merged.Count.Value.ShouldBe(7);
    }

    [Test]
    public void ScalarSequenceDefault_ReplacesWholeValue()
    {
        var lower = new DefaultMergeParent.Fragment
        {
            Tags = Optional<List<string>>.Present(["a"]),
        };
        var higher = new DefaultMergeParent.Fragment
        {
            Tags = Optional<List<string>>.Present(["b"]),
        };

        lower.Merge(higher).Tags.Value!.ShouldBe(["b"]);
        lower.Merge(new DefaultMergeParent.Fragment()).Tags.Value!.ShouldBe(["a"]);
    }

    [Test]
    public void KeyedFragmentMerge_IsWholeValueReplacementNotChangeAlgebra()
    {
        var lower = FragmentChildListHolder.Fragment.From(
            new FragmentChildListHolder
            {
                Items = new List<FragmentChild>
                {
                    new() { Name = "a", Count = 1 },
                    new() { Name = "b", Count = 2 },
                },
            }
        );
        var higher = FragmentChildListHolder.Fragment.From(
            new FragmentChildListHolder
            {
                Items = new List<FragmentChild>
                {
                    new() { Name = "a", Count = 9 },
                },
            }
        );

        // Fragment.Merge takes the higher whole value; it does not express
        // keyed removals/edits like ChangeSet (low "b" disappears).
        var merged = lower.Merge(higher).Items.Value!;
        merged.Count.ShouldBe(1);
        merged[0].Name.ShouldBe("a");
        merged[0].Count.ShouldBe(9);

        lower.Merge(new FragmentChildListHolder.Fragment()).Items.Value!.Count.ShouldBe(2);
    }

    [Test]
    public void KeyedChangeSet_StillExpressesGranularEdits()
    {
        var before = new FragmentChildListHolder
        {
            Items = new List<FragmentChild>
            {
                new() { Name = "a", Count = 1 },
            },
        };
        var after = new FragmentChildListHolder
        {
            Items = new List<FragmentChild>
            {
                new() { Name = "a", Count = 2 },
            },
        };

        var changes = FragmentChildListHolder.ChangeSet.Between(
            Optional<FragmentChildListHolder.Fragment?>.Present(
                FragmentChildListHolder.Fragment.From(before)
            ),
            Optional<FragmentChildListHolder.Fragment?>.Present(
                FragmentChildListHolder.Fragment.From(after)
            )
        );

        changes.IsEmpty.ShouldBeFalse();
    }
}
