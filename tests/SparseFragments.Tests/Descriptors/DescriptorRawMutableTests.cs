using SparseFragments.Generated;

namespace SparseFragments.Tests;

public sealed class RawMutablePoco
{
    public int Counter { get; set; }
}

[SparseFragmentModel]
public partial class RawMutableHolder
{
    public int[] Numbers { get; set; } = [1, 2, 3];

    public string Title { get; set; } = "title";
}

[SparseFragmentModel]
public partial class RawPocoHolder
{
    [SparseMerge(MergeMode.Replace)]
    public RawMutablePoco? Poco { get; set; } = new();
}

public sealed class DescriptorRawMutableTests
{
    [Test]
    public void ArrayElementMutationViaDescriptorIsObserved()
    {
        var model = new RawMutableHolder();
        var session = model.CreateEditSession();
        session.HasChanges.ShouldBeFalse();

        session
            .Descriptors.TryGet(nameof(RawMutableHolder.Numbers), out var descriptor)
            .ShouldBeTrue();
        var numbers = (int[])descriptor.GetValue()!;
        ReferenceEquals(numbers, model.Numbers).ShouldBeTrue();
        numbers[0] = 42;

        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void ObservableRawGetterAlsoInvalidatesTheCache()
    {
        var model = new RawMutableHolder();
        var session = model.CreateEditSession();
        session.HasChanges.ShouldBeFalse();

        session.Observable.Numbers[0] = 99;

        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void DescriptorReadsInvalidateCacheWithoutSpuriousChanges()
    {
        var model = new RawMutableHolder();
        var snapshots = 0;
        var configuration = new EditSessionCoreConfiguration<
            RawMutableHolder,
            RawMutableHolder.Fragment,
            RawMutableHolder.Patch,
            RawMutableHolder.ChangeSet,
            RawObservable,
            RawView
        >
        {
            FromModel = current =>
            {
                snapshots++;
                return RawMutableHolder.Fragment.From(current);
            },
            Between = RawMutableHolder.ChangeSet.Between,
            ToPatch = static changes => changes.ToPatch(),
            IsEmpty = static changes => changes.IsEmpty,
            AdvanceBaseline = static (changes, baseline) => changes.ApplyToBaseline(baseline),
            ToObservable = static (current, changed, access) =>
                new RawObservable(current, changed, access),
            ToCurrent = static current => new RawView(current),
            EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(),
            RefreshObservable = static observable => observable.__SparseRefresh(),
        };
        var session = EditSessionCore<
            RawMutableHolder,
            RawMutableHolder.Fragment,
            RawMutableHolder.Patch,
            RawMutableHolder.ChangeSet,
            RawObservable,
            RawView
        >.Create(model, configuration);

        session.Observable.Title = "observable edit";
        session.HasChanges.ShouldBeTrue();
        var cached = snapshots;
        session.HasChanges.ShouldBeTrue();
        snapshots.ShouldBe(cached);

        session.Observable.Title = "title";
        session.HasChanges.ShouldBeFalse();
        cached = snapshots;

        // The hand-built core has no generated EditSession subclass; resolve the
        // session-bound descriptor set through the observable's internal accessor.
        var accessor = typeof(RawObservable)
            .GetMethods(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            )
            .Single(m => m.Name.StartsWith("__SparseGetDescriptors_", StringComparison.Ordinal));
        var sets = (IDescriptorSet)
            accessor.Invoke(session.Observable, [SparsePath.Root(typeof(RawMutableHolder))])!;

        // Primitive reads stay cheap: no cache invalidation.
        sets.TryGet(nameof(RawMutableHolder.Title), out var title).ShouldBeTrue();
        title.GetValue().ShouldBe("title");
        session.HasChanges.ShouldBeFalse();
        snapshots.ShouldBe(cached);

        // Raw mutable reads invalidate the cache but claim no change on their own.
        sets.TryGet(nameof(RawMutableHolder.Numbers), out var numbers).ShouldBeTrue();
        _ = numbers.GetValue();
        session.HasChanges.ShouldBeFalse();
        snapshots.ShouldBe(cached + 1);

        ((int[])numbers.GetValue()!)[0] = 42;
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void UnproxiedPocoDescriptorExposesTheLiveReference()
    {
        var model = new RawPocoHolder();
        var session = model.CreateEditSession();

        session.Descriptors.TryGet(nameof(RawPocoHolder.Poco), out var descriptor).ShouldBeTrue();
        // No notifying view exists for the POCO: the read is the live reference.
        ReferenceEquals(descriptor.GetValue(), model.Poco).ShouldBeTrue();
        descriptor.Type.ShouldBe(typeof(RawMutablePoco));
        descriptor.ViewType.ShouldBe(typeof(RawMutablePoco));
    }
}
