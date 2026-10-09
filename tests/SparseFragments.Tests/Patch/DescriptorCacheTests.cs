namespace SparseFragments.Tests;

/// <summary>Session descriptor graphs are cached without going stale (#134).</summary>
public sealed class DescriptorCacheTests
{
    [Test]
    public void DescriptorsReturnTheSameSessionBoundInstance()
    {
        var session = new ObservableHolder().CreateEditSession();

        ReferenceEquals(session.Descriptors, session.Descriptors).ShouldBeTrue();
    }

    [Test]
    public void CachedDescriptorsStayLiveAfterObservableMutation()
    {
        var model = new ObservableHolder
        {
            Title = "before",
            Tags = ["one"],
            Child = new ObservableChild { Name = "child-before" },
        };
        var session = model.CreateEditSession();
        var descriptors = session.Descriptors;

        session.Observable.Title = "after";
        session.Observable.Tags.Add("two");
        session.Observable.Child!.Name = "child-after";

        descriptors.TryGet(nameof(ObservableHolder.Title), out var title).ShouldBeTrue();
        title.GetValue().ShouldBe("after");
        title.TrySetValue("via-descriptor").ShouldBeTrue();
        model.Title.ShouldBe("via-descriptor");

        descriptors.TryGet(nameof(ObservableHolder.Tags), out var tags).ShouldBeTrue();
        tags.Array.ShouldNotBeNull();
        tags.Array!.Count.ShouldBe(2);

        descriptors.TryGet(nameof(ObservableHolder.Child), out var child).ShouldBeTrue();
        child.Child!.TryGet(nameof(ObservableChild.Name), out var childName).ShouldBeTrue();
        childName.GetValue().ShouldBe("child-after");
    }

    [Test]
    public void CachedDescriptorsTrackReorderReplacementAndReload()
    {
        var model = new ObservableHolder
        {
            Children =
            [
                new ObservableListChild { Id = "a", Name = "first" },
                new ObservableListChild { Id = "b", Name = "second" },
            ],
        };
        var session = model.CreateEditSession();
        var descriptors = session.Descriptors;

        session.Observable.Children.Move(0, 1);

        descriptors.TryGet(nameof(ObservableHolder.Children), out var children).ShouldBeTrue();
        children
            .Array!.GetItemDescriptors(0)!
            .TryGet(nameof(ObservableListChild.Name), out var first)
            .ShouldBeTrue();
        first.GetValue().ShouldBe("second");

        session.Observable.ReplaceReplacementTags(["replaced"]);
        descriptors
            .TryGet(nameof(ObservableHolder.ReplacementTags), out var replacement)
            .ShouldBeTrue();
        replacement.Array!.Count.ShouldBe(1);

        var server = new ObservableHolder
        {
            Children =
            [
                new ObservableListChild { Id = "a", Name = "first" },
                new ObservableListChild { Id = "b", Name = "server" },
            ],
        };
        session.Reload(server).HasConflicts.ShouldBeFalse();

        // Nested descriptors re-resolve through the cached root against live state.
        var live = model.Children;
        live.Count.ShouldBe(2);
        for (var index = 0; index < live.Count; index++)
        {
            children
                .Array!.GetItemDescriptors(index)!
                .TryGet(nameof(ObservableListChild.Name), out var current)
                .ShouldBeTrue();
            current.GetValue().ShouldBe(live[index].Name);
        }

        descriptors.TryGet(nameof(ObservableHolder.Title), out var title).ShouldBeTrue();
        title.GetValue().ShouldBe(model.Title);

        session.RevertChanges();
        descriptors.TryGet(nameof(ObservableHolder.Title), out var afterRevert).ShouldBeTrue();
        afterRevert.GetValue().ShouldBe(model.Title);
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void RepeatedDescriptorAccessAvoidsReallocation()
    {
        var session = new ObservableHolder { Title = "cached" }.CreateEditSession();
        _ = session.Descriptors;

        const int reads = 200;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < reads; index++)
        {
            _ = session.Descriptors;
        }

        var perRead = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)reads;
        // A cached lookup allocates nothing; allow a small margin for test noise.
        perRead.ShouldBeLessThan(64);
    }
}
