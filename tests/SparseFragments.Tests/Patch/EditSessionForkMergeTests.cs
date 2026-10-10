namespace SparseFragments.Tests;

/// <summary>Fork and non-destructive merge using the generated EditSession type (issue #205).</summary>
public sealed class EditSessionForkMergeTests
{
    private static T Raw<T>(ISparseEditSession<T> session)
        where T : class => session.Model;

    [Test]
    public void ForkReturnsSameTypeWithIndependentModelAndForkTimeBaseline()
    {
        var model = new NeutralSessionModel { Name = "before", Version = 1 };
        var session = model.CreateEditSession();
        model.Name = "edited";

        var fork = session.Fork();

        fork.GetType().ShouldBe(session.GetType());
        ReferenceEquals(Raw(fork), model).ShouldBeFalse();
        fork.HasChanges.ShouldBeFalse();
        fork.CreateChangeSet().IsEmpty.ShouldBeTrue();
        fork.Current.Name.ShouldBe("edited");

        Raw(fork).Name = "fork edit";

        fork.HasChanges.ShouldBeTrue();
        var forkChanges = fork.CreateChangeSet();
        forkChanges.Name.Before.Value.ShouldBe("edited");
        forkChanges.Name.After.Value.ShouldBe("fork edit");
        model.Name.ShouldBe("edited");
        var receiverChanges = session.CreateChangeSet();
        receiverChanges.Name.Before.Value.ShouldBe("before");
        receiverChanges.Name.After.Value.ShouldBe("edited");
    }

    [Test]
    public void IndependentMemberEditsMergeAndRetainReceiverBaseline()
    {
        var model = new NeutralSessionModel { Name = "a", Version = 1 };
        var session = model.CreateEditSession();
        var fork = session.Fork();

        session.Observable.Name = "c";
        fork.Observable.Version = 2;

        session.TryMergeFrom(fork, out var merged, out var conflicts).ShouldBeTrue();
        conflicts.ShouldBeNull();
        merged.ShouldNotBeNull();
        ReferenceEquals(merged, session).ShouldBeFalse();
        ReferenceEquals(merged, fork).ShouldBeFalse();

        var mergedModel = Raw(merged);
        mergedModel.Name.ShouldBe("c");
        mergedModel.Version.ShouldBe(2);
        merged.Current.Name.ShouldBe("c");

        // The merged session retains the receiver baseline A, so its change
        // set spans both the receiver edit B -> C and the fork edit B -> D.
        var combined = merged.CreateChangeSet();
        combined.Name.Before.Value.ShouldBe("a");
        combined.Name.After.Value.ShouldBe("c");
        combined.Version.Before.Value.ShouldBe(1);
        combined.Version.After.Value.ShouldBe(2);

        // Neither input session was modified by the merge.
        model.Name.ShouldBe("c");
        model.Version.ShouldBe(1);
        Raw(fork).Name.ShouldBe("a");
        Raw(fork).Version.ShouldBe(2);
        session.CreateChangeSet().Version.IsChanged.ShouldBeFalse();
        fork.CreateChangeSet().Name.IsChanged.ShouldBeFalse();
    }

    [Test]
    public void ConflictingEditsReportConflictsWithoutModifyingEitherInput()
    {
        var model = new NeutralSessionModel { Name = "a" };
        var session = model.CreateEditSession();
        var fork = session.Fork();
        session.Observable.Name = "c";
        Raw(fork).Name = "d";

        session.TryMergeFrom(fork, out var merged, out var conflicts).ShouldBeFalse();

        merged.ShouldBeNull();
        conflicts.ShouldNotBeNull();
        var conflict = conflicts.ShouldHaveSingleItem();
        conflict.Path.ShouldBe(SparsePath.Root<NeutralSessionModel>().Member("Name"));
        conflict.PathText.ShouldBe(nameof(NeutralSessionModel.Name));
        conflict.Kind.ShouldBe(SparseConflictKind.Scalar);
        conflict.BaseValue.IsPresent.ShouldBeTrue();
        conflict.LocalValue.IsPresent.ShouldBeTrue();
        conflict.CurrentValue.IsPresent.ShouldBeTrue();

        model.Name.ShouldBe("c");
        Raw(fork).Name.ShouldBe("d");
        var receiverChanges = session.CreateChangeSet();
        receiverChanges.Name.Before.Value.ShouldBe("a");
        receiverChanges.Name.After.Value.ShouldBe("c");
        var forkChanges = fork.CreateChangeSet();
        forkChanges.Name.Before.Value.ShouldBe("a");
        forkChanges.Name.After.Value.ShouldBe("d");
    }

    [Test]
    public void NoOpForkMergesIntoFreshSession()
    {
        var model = new NeutralSessionModel { Name = "a", Version = 1 };
        var session = model.CreateEditSession();
        model.Name = "b";
        var fork = session.Fork();

        session.TryMergeFrom(fork, out var merged, out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        merged.ShouldNotBeNull();
        ReferenceEquals(merged, session).ShouldBeFalse();
        ReferenceEquals(Raw(merged), model).ShouldBeFalse();
        Raw(merged).Name.ShouldBe("b");
        merged.HasChanges.ShouldBeTrue();
        var combined = merged.CreateChangeSet();
        combined.Name.Before.Value.ShouldBe("a");
        combined.Name.After.Value.ShouldBe("b");

        model.Name.ShouldBe("b");
        session.CreateChangeSet().Name.After.Value.ShouldBe("b");
    }

    [Test]
    public void NestedForksMergeIntoRoot()
    {
        var model = new NeutralSessionModel { Name = "a", Version = 1 };
        var session = model.CreateEditSession();
        var first = session.Fork();
        var second = first.Fork();

        second.Observable.Name = "nested";
        session.Observable.Version = 2;

        session.TryMergeFrom(second, out var merged, out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        merged.ShouldNotBeNull();
        Raw(merged).Name.ShouldBe("nested");
        Raw(merged).Version.ShouldBe(2);
        var combined = merged.CreateChangeSet();
        combined.Name.Before.Value.ShouldBe("a");
        combined.Name.After.Value.ShouldBe("nested");
        combined.Version.Before.Value.ShouldBe(1);
        combined.Version.After.Value.ShouldBe(2);
    }

    [Test]
    public void RepeatedMergeOfSameForkIsIdempotent()
    {
        var model = new NeutralSessionModel { Name = "a", Version = 1 };
        var session = model.CreateEditSession();
        var fork = session.Fork();
        session.Observable.Name = "c";
        fork.Observable.Version = 2;

        session.TryMergeFrom(fork, out var first, out _).ShouldBeTrue();
        first.ShouldNotBeNull();
        first.TryMergeFrom(fork, out var second, out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        second.ShouldNotBeNull();
        Raw(second).Name.ShouldBe("c");
        Raw(second).Version.ShouldBe(2);
        var combined = second.CreateChangeSet();
        combined.Name.Before.Value.ShouldBe("a");
        combined.Name.After.Value.ShouldBe("c");
        combined.Version.Before.Value.ShouldBe(1);
        combined.Version.After.Value.ShouldBe(2);

        // The first merged session is untouched by the repeated merge.
        Raw(first).Name.ShouldBe("c");
        first.CreateChangeSet().Name.After.Value.ShouldBe("c");
    }

    [Test]
    public void UnrelatedSessionMergeThrowsAndLeavesInputsUntouched()
    {
        var firstModel = new NeutralSessionModel { Name = "a" };
        var secondModel = new NeutralSessionModel { Name = "a" };
        var first = firstModel.CreateEditSession();
        var second = secondModel.CreateEditSession();
        first.Observable.Name = "c";
        second.Observable.Name = "d";

        Should
            .Throw<InvalidOperationException>(() => first.TryMergeFrom(second, out _, out _))
            .Message.ShouldContain("unrelated");

        firstModel.Name.ShouldBe("c");
        secondModel.Name.ShouldBe("d");
        first.CreateChangeSet().Name.After.Value.ShouldBe("c");
        second.CreateChangeSet().Name.After.Value.ShouldBe("d");
    }

    [Test]
    public void SelfMergeThrows()
    {
        var session = new NeutralSessionModel { Name = "a" }.CreateEditSession();
        session.Observable.Name = "b";

        Should.Throw<InvalidOperationException>(() => session.TryMergeFrom(session, out _, out _));

        session.CreateChangeSet().Name.After.Value.ShouldBe("b");
    }

    [Test]
    public void NullForkThrows()
    {
        var session = new NeutralSessionModel().CreateEditSession();

        Should.Throw<ArgumentNullException>(() => session.TryMergeFrom(null!, out _, out _));
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void NestedDictionaryAndListEditsMerge()
    {
        var model = new NeutralSessionModel
        {
            Name = "a",
            Tags = ["one"],
            Counts = new() { ["first"] = 1 },
            Child = new NeutralSessionChild { Value = "old" },
        };
        var session = model.CreateEditSession();
        var fork = session.Fork();

        fork.Observable.Tags.Add("two");
        fork.Observable.Child!.Value = "new";
        session.Observable.Counts.Add("second", 2);

        session.TryMergeFrom(fork, out var merged, out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        merged.ShouldNotBeNull();
        var mergedModel = Raw(merged);
        mergedModel.Tags.ShouldBe(["one", "two"]);
        mergedModel.Child.Value.ShouldBe("new");
        mergedModel.Counts["second"].ShouldBe(2);

        var combined = merged.CreateChangeSet();
        combined.Tags.Before.Value.ShouldBe(["one"]);
        combined.Tags.After.Value.ShouldBe(["one", "two"]);
        combined.Child.Value.Before.Value.ShouldBe("old");
        combined.Child.Value.After.Value.ShouldBe("new");
        combined.Counts.Added.ContainsKey("second").ShouldBeTrue();

        model.Tags.ShouldBe(["one"]);
        model.Child.Value.ShouldBe("old");
        Raw(fork).Tags.ShouldBe(["one", "two"]);
    }

    [Test]
    public void KeyedDisjointEditsMerge()
    {
        var model = new KeyedServerHolder
        {
            Items =
            [
                new KeyedServer { Id = "a", Name = "first" },
                new KeyedServer { Id = "b", Name = "second" },
            ],
        };
        var session = model.CreateEditSession();
        var fork = session.Fork();

        session.Observable.Items[0].Name = "renamed";
        fork.Observable.Items.AddModel(new KeyedServer { Id = "c", Name = "third" });

        session.TryMergeFrom(fork, out var merged, out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        merged.ShouldNotBeNull();
        var items = Raw(merged).Items;
        items.Count.ShouldBe(3);
        items.Single(item => item.Id == "a").Name.ShouldBe("renamed");
        items.Any(item => item.Id == "c").ShouldBeTrue();

        model.Items.Count.ShouldBe(2);
        Raw(fork).Items.Count.ShouldBe(3);
        Raw(fork).Items.Single(item => item.Id == "a").Name.ShouldBe("first");
    }

    [Test]
    public void KeyedConflictingEditsReportConflictsWithoutModification()
    {
        var model = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "a", Name = "first" }],
        };
        var session = model.CreateEditSession();
        var fork = session.Fork();

        session.Observable.Items[0].Name = "local";
        fork.Observable.Items[0].Name = "remote";

        session.TryMergeFrom(fork, out var merged, out var conflicts).ShouldBeFalse();

        merged.ShouldBeNull();
        conflicts.ShouldNotBeNull();
        conflicts.Count.ShouldBeGreaterThan(0);
        model.Items[0].Name.ShouldBe("local");
        Raw(fork).Items[0].Name.ShouldBe("remote");
    }

    [Test]
    public void MergedModelDoesNotAliasEitherInput()
    {
        var model = new NeutralSessionModel
        {
            Tags = ["one"],
            Counts = new() { ["first"] = 1 },
            Child = new NeutralSessionChild { Value = "old" },
        };
        var session = model.CreateEditSession();
        var fork = session.Fork();
        fork.Observable.Tags.Add("two");

        session.TryMergeFrom(fork, out var merged, out _).ShouldBeTrue();
        merged.ShouldNotBeNull();
        var mergedModel = Raw(merged);

        ReferenceEquals(mergedModel.Tags, model.Tags).ShouldBeFalse();
        ReferenceEquals(mergedModel.Tags, Raw(fork).Tags).ShouldBeFalse();
        ReferenceEquals(mergedModel.Counts, model.Counts).ShouldBeFalse();
        ReferenceEquals(mergedModel.Child, model.Child).ShouldBeFalse();
        ReferenceEquals(mergedModel.Child, Raw(fork).Child).ShouldBeFalse();

        mergedModel.Tags.Add("merged only");
        mergedModel.Child.Value = "merged only";
        mergedModel.Counts["merged"] = 9;

        model.Tags.ShouldBe(["one"]);
        model.Child.Value.ShouldBe("old");
        model.Counts.ContainsKey("merged").ShouldBeFalse();
        Raw(fork).Tags.ShouldBe(["one", "two"]);
        Raw(fork).Child.Value.ShouldBe("old");

        Raw(fork).Tags.Add("fork only");
        Raw(fork).Child.Value = "fork only";
        mergedModel.Tags.ShouldBe(["one", "two", "merged only"]);
        mergedModel.Child.Value.ShouldBe("merged only");
    }

    [Test]
    public void ForkAndMergeKeepNotificationsIsolated()
    {
        var model = new NeutralSessionModel { Name = "a" };
        var receiverTransitions = new List<NeutralSessionModel.ChangeSet>();
        var forkTransitions = new List<NeutralSessionModel.ChangeSet>();
        var session = model.CreateEditSession();
        session.TransitionObserved += receiverTransitions.Add;
        var fork = session.Fork();
        fork.TransitionObserved += forkTransitions.Add;

        fork.Observable.Name = "fork edit";

        forkTransitions.Count.ShouldBe(1);
        receiverTransitions.ShouldBeEmpty();

        session.Observable.Name = "receiver edit";

        receiverTransitions.Count.ShouldBe(1);
        forkTransitions.Count.ShouldBe(1);

        session.TryMergeFrom(fork, out var merged, out _).ShouldBeFalse();
        merged.ShouldBeNull();
        receiverTransitions.Count.ShouldBe(1);
        forkTransitions.Count.ShouldBe(1);

        // The failed merge raised nothing on either session.
        session.HasChanges.ShouldBeTrue();
        fork.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void ForkStartsWithoutTheChangeCallback()
    {
        var notifications = 0;
        var model = new NeutralSessionModel { Name = "a" };
        var session = model.CreateEditSession(() => notifications++);
        var fork = session.Fork();

        fork.Observable.Name = "fork edit";

        notifications.ShouldBe(0);

        session.Observable.Name = "receiver edit";

        notifications.ShouldBe(1);
    }

    [Test]
    public void InitOnlyModelForkMergesWithoutInPlaceWrites()
    {
        var model = new ImmutableSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        var fork = session.Fork();

        session.TryMergeFrom(fork, out var merged, out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        merged.ShouldNotBeNull();
        ReferenceEquals(Raw(merged), model).ShouldBeFalse();
        Raw(merged).Name.ShouldBe("before");
        merged.HasChanges.ShouldBeFalse();
        merged.CreateChangeSet().IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void DuplicateKeyedStateFailsWithoutModification()
    {
        var model = new DuplicateKeySessionModel
        {
            Items = [new DuplicateKeySessionItem { Id = "same" }],
        };
        var session = model.CreateEditSession();
        var fork = session.Fork();
        Raw(fork).Items.Add(new DuplicateKeySessionItem { Id = "same" });

        Should
            .Throw<InvalidOperationException>(() => session.TryMergeFrom(fork, out _, out _))
            .Message.ShouldBe("Duplicate key in keyed collection.");

        model.Items.Count.ShouldBe(1);
        Raw(fork).Items.Count.ShouldBe(2);
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void UnassignedSentinelRowStaysPendingWithoutEnteringTheBaseline()
    {
        var model = new AssignedServerHolder
        {
            Items = [new AssignedServer { Id = 1, Name = "first" }],
        };
        var session = model.CreateEditSession();
        var fork = session.Fork();
        Raw(fork).Items.Add(new AssignedServer { Id = 0, Name = "pending" });

        // Unassigned adds rebase onto a clean receiver; the sentinel stays a
        // pending row and never enters the retained baseline.
        session.TryMergeFrom(fork, out var merged, out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        merged.ShouldNotBeNull();
        var items = Raw(merged).Items;
        items.Count.ShouldBe(2);
        items.Any(item => item.Id == 0 && item.Name == "pending").ShouldBeTrue();
        merged.CreateChangeSet().IsEmpty.ShouldBeFalse();

        // Acknowledging the merged transition as the baseline still fails.
        Should.Throw<InvalidOperationException>(() =>
            merged.AcceptChanges(merged.CreateChangeSet())
        );
        model.Items.Count.ShouldBe(1);
        Raw(fork).Items.Count.ShouldBe(2);
    }

    [Test]
    public void MergeOntoSentinelStateFailsDeterministically()
    {
        var model = new AssignedServerHolder
        {
            Items = [new AssignedServer { Id = 1, Name = "first" }],
        };
        var session = model.CreateEditSession();
        var fork = session.Fork();
        Raw(fork).Items.Add(new AssignedServer { Id = 2, Name = "fork row" });
        model.Items.Add(new AssignedServer { Id = 0, Name = "receiver pending" });

        // Rebasing onto a current state with sentinels fails instead of
        // matching rows by an unstable identity.
        Should
            .Throw<InvalidOperationException>(() => session.TryMergeFrom(fork, out _, out _))
            .Message.ShouldContain("unassigned key");

        model.Items.Count.ShouldBe(2);
        Raw(fork).Items.Count.ShouldBe(2);
        Raw(fork).Items.Any(item => item.Id == 2).ShouldBeTrue();
    }

    [Test]
    public void CyclicModelForkFailsWithoutModifyingSession()
    {
        var model = new CycleSettings
        {
            Value = 1,
            Next = new CycleSettings { Value = 2 },
        };
        var session = model.CreateEditSession();
        model.Next!.Next = model;
        var before = Raw(session);

        var exception = Should.Throw<NotSupportedException>(() => session.Fork());
        exception.Message.ShouldContain("Cyclic reference");

        ReferenceEquals(Raw(session), before).ShouldBeTrue();
        model.Value.ShouldBe(1);
        model.Next.Value.ShouldBe(2);
    }

    [Test]
    public void MergedSessionHasFreshBindingsAndCurrentView()
    {
        var model = new NeutralSessionModel { Name = "a", Version = 1 };
        var session = model.CreateEditSession();
        var fork = session.Fork();
        session.Observable.Name = "c";
        fork.Observable.Version = 2;

        session.TryMergeFrom(fork, out var merged, out _).ShouldBeTrue();
        merged.ShouldNotBeNull();

        ReferenceEquals(merged.Observable, session.Observable).ShouldBeFalse();
        ReferenceEquals(merged.Observable, fork.Observable).ShouldBeFalse();
        merged.Current.Name.ShouldBe("c");
        merged
            .Descriptors.TryGet(nameof(NeutralSessionModel.Name), out var descriptor)
            .ShouldBeTrue();
        descriptor.GetValue().ShouldBe("c");

        var mergedTransitions = new List<NeutralSessionModel.ChangeSet>();
        merged.TransitionObserved += mergedTransitions.Add;
        merged.Observable.Name = "later";

        mergedTransitions.Count.ShouldBe(1);
        model.Name.ShouldBe("c");
        Raw(fork).Name.ShouldBe("a");
        merged.Current.Name.ShouldBe("later");
    }

    [Test]
    public void ForkBaselineAdvancementKeepsLineage()
    {
        var model = new NeutralSessionModel { Name = "a", Version = 1 };
        var session = model.CreateEditSession();
        var fork = session.Fork();
        fork.Observable.Name = "b";
        fork.Observable.Version = 2;
        fork.AcceptChanges();

        fork.HasChanges.ShouldBeFalse();
        session.TryMergeFrom(fork, out var merged, out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        merged.ShouldNotBeNull();
        Raw(merged).Name.ShouldBe("a");
        Raw(merged).Version.ShouldBe(1);
    }
}
