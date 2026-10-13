using SparseTypes.Generated;

namespace SparseTypes.Tests;

/// <summary>Failure matrix for the collection editing facade (issue #251).</summary>
public sealed class CollectionEditingFacadeErrorTests
{
    [Test]
    public void ImmutableArray_ReportsNotMutableButStaysReadable()
    {
        var model = new CollectionEditRoster { Tags = [1, 2] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<int>(CollectionEditRoster.SparsePath.Tags);
        var transitions = 0;
        session.TransitionObserved += _ => transitions++;

        editor.Count.ShouldBe(2);
        editor.IsAvailable(out var availableError).ShouldBeTrue();
        availableError.ShouldBe(SparseCollectionEditError.None);

        editor.TryAdd(3, out var addError).ShouldBeFalse();
        addError.ShouldBe(SparseCollectionEditError.NotMutable);
        editor.TryInsert(0, 3, out var insertError).ShouldBeFalse();
        insertError.ShouldBe(SparseCollectionEditError.NotMutable);
        editor.TryRemoveAt(0, out var removeError).ShouldBeFalse();
        removeError.ShouldBe(SparseCollectionEditError.NotMutable);
        editor.TryMove(0, 1, out var moveError).ShouldBeFalse();
        moveError.ShouldBe(SparseCollectionEditError.NotMutable);
        editor.TryReplace(0, 9, out var replaceError).ShouldBeFalse();
        replaceError.ShouldBe(SparseCollectionEditError.NotMutable);

        model.Tags.ShouldBe([1, 2]);
        transitions.ShouldBe(0);
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void NullCollection_ReportsNullThenRecoversOnAssignment()
    {
        var model = new CollectionEditRoster { MaybeQuests = null };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(
            CollectionEditRoster.SparsePath.MaybeQuests
        );

        editor.Count.ShouldBe(0);
        editor.IsAvailable(out var availableError).ShouldBeFalse();
        availableError.ShouldBe(SparseCollectionEditError.NullCollection);
        editor.TryAdd(new() { Id = "a" }, out var addError).ShouldBeFalse();
        addError.ShouldBe(SparseCollectionEditError.NullCollection);

        session.Observable.ReplaceMaybeQuests([new() { Id = "a", Title = "Alpha" }]);
        editor.IsAvailable(out availableError).ShouldBeTrue();
        editor.TryGetByKey("a", out var quest, out _).ShouldBeTrue();
        quest!.Title.ShouldBe("Alpha");
    }

    [Test]
    public void UnknownMember_ReportsUnknownCollection()
    {
        var model = new CollectionEditRoster();
        var session = model.CreateEditSession();
        var path = SparsePath.Root<CollectionEditRoster>().Member("NoSuch");
        var editor = session.Descriptors.EditCollection<CollectionEditQuest>(
            path,
            session.BatchEdit
        );

        editor.Count.ShouldBe(0);
        editor.IsAvailable(out var availableError).ShouldBeFalse();
        availableError.ShouldBe(SparseCollectionEditError.UnknownCollection);
        editor.TryAdd(new() { Id = "a" }, out var error).ShouldBeFalse();
        error.ShouldBe(SparseCollectionEditError.UnknownCollection);
    }

    [Test]
    public void ScalarMember_ReportsNotACollection()
    {
        var model = new CollectionEditRoster { Name = "base" };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<string>(CollectionEditRoster.SparsePath.Name);

        editor.IsAvailable(out var availableError).ShouldBeFalse();
        availableError.ShouldBe(SparseCollectionEditError.NotACollection);
        editor.TryAdd("x", out var error).ShouldBeFalse();
        error.ShouldBe(SparseCollectionEditError.NotACollection);
    }

    [Test]
    public void NonMemberPath_ThrowsArgumentException()
    {
        var model = new CollectionEditRoster();
        var session = model.CreateEditSession();

        Should.Throw<ArgumentException>(() => session.EditCollection<CollectionEditQuest>(default));
        Should.Throw<ArgumentException>(() =>
            session.EditCollection<CollectionEditQuest>(
                SparsePath.Root<CollectionEditRoster>().Member("Quests").At(0)
            )
        );
    }

    [Test]
    public void KeyedOperations_OnUnkeyedCollectionThrow()
    {
        var model = new CollectionEditRoster { Scores = [1] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<int, int>(CollectionEditRoster.SparsePath.Scores);

        Should.Throw<InvalidOperationException>(() => editor.TryGetByKey(1, out _, out _));
        // Pure lookups degrade gracefully instead of throwing.
        editor.IndexOfKey(1).ShouldBe(-1);
        editor.ContainsKey(1).ShouldBeFalse();
    }

    [Test]
    public void KeyTypeMismatch_ThrowsInvalidOperation()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "a", Title = "Alpha" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, int>(
            CollectionEditRoster.SparsePath.Quests
        );

        Should.Throw<InvalidOperationException>(() => editor.TryGetByKey(1, out _, out _));
    }

    [Test]
    public void WrongElementType_ReportsInvalidItem()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "a", Title = "Alpha" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<string>(CollectionEditRoster.SparsePath.Quests);

        editor.TryGetAt(0, out _, out var getError).ShouldBeFalse();
        getError.ShouldBe(SparseCollectionEditError.InvalidItem);
        editor.TryAdd("nope", out var addError).ShouldBeFalse();
        addError.ShouldBe(SparseCollectionEditError.InvalidItem);
        model.Quests.Count.ShouldBe(1);
    }

    [Test]
    public void IndexValidation_PrecedesMutation()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "a", Title = "Alpha" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(
            CollectionEditRoster.SparsePath.Quests
        );
        var transitions = 0;
        session.TransitionObserved += _ => transitions++;

        editor.TryGetAt(7, out _, out var getError).ShouldBeFalse();
        getError.ShouldBe(SparseCollectionEditError.IndexOutOfRange);
        editor.TryRemoveAt(-1, out var removeError).ShouldBeFalse();
        removeError.ShouldBe(SparseCollectionEditError.IndexOutOfRange);
        editor.TryMove(0, 7, out var moveError).ShouldBeFalse();
        moveError.ShouldBe(SparseCollectionEditError.IndexOutOfRange);
        editor.TryInsert(2, new(), out var insertError).ShouldBeFalse();
        insertError.ShouldBe(SparseCollectionEditError.IndexOutOfRange);

        // Inserting exactly at the end appends.
        editor.TryInsert(1, new() { Id = "b", Title = "Beta" }, out _).ShouldBeTrue();
        model.Quests.Select(static quest => quest.Id).ShouldBe(["a", "b"]);

        // Moving onto the same position succeeds without a transition.
        editor.TryMove(1, 1, out var sameError).ShouldBeTrue();
        sameError.ShouldBe(SparseCollectionEditError.None);
        transitions.ShouldBe(1);
    }

    [Test]
    public void ReplaceWithTakenKey_ReportsDuplicate()
    {
        var model = new CollectionEditRoster
        {
            Quests = [new() { Id = "a", Title = "Alpha" }, new() { Id = "b", Title = "Beta" }],
        };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(
            CollectionEditRoster.SparsePath.Quests
        );
        var transitions = 0;
        session.TransitionObserved += _ => transitions++;

        // Changing identity is remove plus add: taking another row's key fails.
        editor
            .TryReplaceByKey("b", new() { Id = "a", Title = "Clash" }, out var error)
            .ShouldBeFalse();
        error.ShouldBe(SparseCollectionEditError.DuplicateKey);
        model.Quests[1].Title.ShouldBe("Beta");
        transitions.ShouldBe(0);
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void MoveOnly_ReordersChangeSetOrder()
    {
        var model = new CollectionEditRoster
        {
            Quests = [new() { Id = "a", Title = "Alpha" }, new() { Id = "b", Title = "Beta" }],
        };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(
            CollectionEditRoster.SparsePath.Quests
        );

        editor.TryMove(0, 1, out _).ShouldBeTrue();

        var changes = session.CreateChangeSet();
        changes.Quests.OrderChanged.ShouldBeTrue();
        changes.Quests.BeforeOrder.ShouldBe(["a", "b"]);
        changes.Quests.AfterOrder.ShouldBe(["b", "a"]);
        changes.Quests.Added.ShouldBeEmpty();
        changes.Quests.Removed.ShouldBeEmpty();
    }

    [Test]
    public void StaleItemDescriptors_FailAfterCollectionReplacement()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "a", Title = "Alpha" }] };
        var session = model.CreateEditSession();
        session.Descriptors.TryGet("Quests", out var member).ShouldBeTrue();
        var retained = member.Array!.GetItemDescriptors(0);
        retained.ShouldNotBeNull();

        session.Observable.ReplaceQuests([new() { Id = "fresh", Title = "Fresh" }]);

        // Retained item descriptors observe the captured instance; writes fail
        // instead of mutating the orphan, while the facade re-resolves live.
        retained!.TryGet("Title", out var title).ShouldBeTrue();
        title.TrySetValue("Mutated").ShouldBeFalse();
        model.Quests[0].Title.ShouldBe("Fresh");

        var editor = session.EditCollection<CollectionEditQuest, string>(
            CollectionEditRoster.SparsePath.Quests
        );
        editor.TryGetByKey("fresh", out var fresh, out _).ShouldBeTrue();
        fresh!.Title.ShouldBe("Fresh");
    }
}
