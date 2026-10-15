namespace SparseTypes.Tests;

[SparseModel]
public partial class CollectionEditQuest
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int Points { get; set; }
}

[SparseModel]
public partial class CollectionEditRoster
{
    public string Name { get; set; } = string.Empty;

    public List<CollectionEditQuest> Quests { get; set; } = [];

    public List<int> Scores { get; set; } = [];

    public int[] Tags { get; set; } = [];

    public List<CollectionEditQuest>? MaybeQuests { get; set; }
}

[SparseModel]
public partial class CollectionEditOrder
{
    public List<CollectionEditLine> Lines { get; set; } = [];
}

public partial class CollectionEditLine
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [SparseKey(Unassigned = 0)]
    public int Key => Id;

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }
}

/// <summary>Representative Case 3/4/5 editing flows through the collection facade.</summary>
public sealed class CollectionEditingFacadeTests
{
    [Test]
    public void AddAndReorderFlow_GroupsOneNotificationPerAction()
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

        editor.TryAdd(new() { Id = "c", Title = "Gamma" }, out var addError).ShouldBeTrue();
        addError.ShouldBe(SparseCollectionEditError.None);
        transitions.ShouldBe(1);

        editor.TryMoveByKey("a", 2, out var moveError).ShouldBeTrue();
        moveError.ShouldBe(SparseCollectionEditError.None);
        transitions.ShouldBe(2);

        model.Quests.Select(static quest => quest.Id).ShouldBe(["b", "c", "a"]);
        var changes = session.CreateChangeSet();
        changes.Quests.Added.Select(static quest => quest.Id).ShouldBe(["c"]);
        changes.Quests.OrderChanged.ShouldBeTrue();
        changes.Quests.AfterOrder.ShouldBe(["b", "c", "a"]);
    }

    [Test]
    public void ScalarList_ReordersWithoutRemoveThenInsert()
    {
        var model = new CollectionEditRoster { Scores = [10, 20, 30] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<int>(CollectionEditRoster.SparsePath.Scores);

        editor.TryMove(0, 2, out var error).ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);
        model.Scores.ShouldBe([20, 30, 10]);

        editor.TryRemoveAt(0, out error).ShouldBeTrue();
        editor.TryInsert(0, 99, out error).ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);
        model.Scores.ShouldBe([99, 30, 10]);
    }

    [Test]
    public void FacadeCall_JoinsOuterBatchEdit()
    {
        var model = new CollectionEditRoster { Name = "base" };
        var session = model.CreateEditSession();
        var transitions = 0;
        session.TransitionObserved += _ => transitions++;

        session.BatchEdit(() =>
        {
            session.Observable.Name = "edited";
            var editor = session.EditCollection<CollectionEditQuest, string>(
                CollectionEditRoster.SparsePath.Quests
            );
            editor.TryAdd(new() { Id = "a", Title = "Alpha" }, out _).ShouldBeTrue();
        });

        transitions.ShouldBe(1);
        var changes = session.CreateChangeSet();
        changes.Name.After.Value.ShouldBe("edited");
        changes.Quests.Added.ShouldHaveSingleItem();
    }

    [Test]
    public void KeyedLookup_StaysStableAcrossReorder()
    {
        var model = new CollectionEditRoster
        {
            Quests = [new() { Id = "a", Title = "Alpha" }, new() { Id = "b", Title = "Beta" }],
        };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(
            CollectionEditRoster.SparsePath.Quests
        );

        editor.TryGetByKey("a", out var before, out _).ShouldBeTrue();
        editor.TryMove(0, 1, out _).ShouldBeTrue();

        editor.TryGetByKey("a", out var after, out var error).ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);
        ReferenceEquals(before, after).ShouldBeTrue();
        after!.Title.ShouldBe("Alpha");
        editor.IndexOfKey("a").ShouldBe(1);
        editor.ContainsKey("a").ShouldBeTrue();
        editor.ContainsKey("zzz").ShouldBeFalse();
    }

    [Test]
    public void RemoveByKey_ReportsRemovedInChangeSet()
    {
        var model = new CollectionEditRoster
        {
            Quests = [new() { Id = "a", Title = "Alpha" }, new() { Id = "b", Title = "Beta" }],
        };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(
            CollectionEditRoster.SparsePath.Quests
        );

        editor.TryRemoveByKey("a", out var error).ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);
        model.Quests.Select(static quest => quest.Id).ShouldBe(["b"]);

        var changes = session.CreateChangeSet();
        changes.Quests.Removed.Select(static quest => quest.Id).ShouldBe(["a"]);
        editor.ContainsKey("a").ShouldBeFalse();
    }

    [Test]
    public void ReplaceByKey_SameKeyEditsInPlace()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "b", Title = "Beta" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(
            CollectionEditRoster.SparsePath.Quests
        );

        editor
            .TryReplaceByKey("b", new() { Id = "b", Title = "Beta v2" }, out var error)
            .ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);

        var changes = session.CreateChangeSet();
        changes.Quests.GetChange("b").IsEdited.ShouldBeTrue();
        model.Quests[0].Title.ShouldBe("Beta v2");
    }

    [Test]
    public void DuplicateAdd_FailsWithoutMutationOrNotification()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "a", Title = "Alpha" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(
            CollectionEditRoster.SparsePath.Quests
        );
        var transitions = 0;
        session.TransitionObserved += _ => transitions++;

        editor.TryAdd(new() { Id = "a", Title = "Clash" }, out var error).ShouldBeFalse();
        error.ShouldBe(SparseCollectionEditError.DuplicateKey);
        model.Quests.Count.ShouldBe(1);
        model.Quests[0].Title.ShouldBe("Alpha");
        transitions.ShouldBe(0);
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void KeyedLookup_ErrorsAreDeterministic()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "a", Title = "Alpha" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(
            CollectionEditRoster.SparsePath.Quests
        );

        editor.TryGetByKey("zzz", out _, out var getError).ShouldBeFalse();
        getError.ShouldBe(SparseCollectionEditError.UnknownKey);
        editor.TryRemoveByKey("zzz", out var removeError).ShouldBeFalse();
        removeError.ShouldBe(SparseCollectionEditError.UnknownKey);
        editor.TryMoveByKey("zzz", 0, out var moveError).ShouldBeFalse();
        moveError.ShouldBe(SparseCollectionEditError.UnknownKey);
        editor.TryReplaceByKey("zzz", new(), out var replaceError).ShouldBeFalse();
        replaceError.ShouldBe(SparseCollectionEditError.UnknownKey);
    }

    [Test]
    public void SentinelKey_NeverServesAsDurableIdentity()
    {
        var model = new CollectionEditOrder
        {
            Lines = [new() { Id = 7, Name = "saved" }, new() { Name = "legacy-pending" }],
        };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditLine, int>(
            CollectionEditOrder.SparsePath.Lines
        );

        editor.IsUnassignedKey(0).ShouldBeTrue();
        editor.IsUnassignedKey(7).ShouldBeFalse();
        editor.IndexOfKey(0).ShouldBe(-1);
        editor.ContainsKey(0).ShouldBeFalse();

        editor.TryGetByKey(0, out _, out var getError).ShouldBeFalse();
        getError.ShouldBe(SparseCollectionEditError.UnassignedKey);
        editor.TryRemoveByKey(0, out var removeError).ShouldBeFalse();
        removeError.ShouldBe(SparseCollectionEditError.UnassignedKey);

        editor.TryGetByKey(7, out var saved, out _).ShouldBeTrue();
        saved!.Name.ShouldBe("saved");
    }

    [Test]
    public void TemporaryIdentity_FlowSurvivesReorder()
    {
        var model = new CollectionEditOrder { Lines = [new() { Id = 7, Name = "saved" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditLine, int>(
            CollectionEditOrder.SparsePath.Lines
        );
        var temp = Guid.NewGuid();

        editor.TryAdd(new() { Name = "pending", TemporaryId = temp }, out _).ShouldBeTrue();
        editor.TryGetByTemporaryKey(temp, out var pending, out _).ShouldBeTrue();
        pending!.Name.ShouldBe("pending");

        editor.TryMoveByKey(7, 1, out _).ShouldBeTrue();
        editor.TryGetByTemporaryKey(temp, out var moved, out var error).ShouldBeTrue();
        error.ShouldBe(SparseCollectionEditError.None);
        ReferenceEquals(pending, moved).ShouldBeTrue();

        var changes = session.CreateChangeSet();
        changes.Lines.GetTemporaryChange(temp).IsAdded.ShouldBeTrue();

        editor.TryRemoveByTemporaryKey(temp, out _).ShouldBeTrue();
        editor.TryGetByTemporaryKey(temp, out _, out var missing).ShouldBeFalse();
        missing.ShouldBe(SparseCollectionEditError.UnknownTemporaryKey);
        editor.TryGetByTemporaryKey(Guid.Empty, out _, out var empty).ShouldBeFalse();
        empty.ShouldBe(SparseCollectionEditError.UnknownTemporaryKey);
    }

    [Test]
    public void ReplacedCollection_EditorKeepsWorkingOnCurrentInstance()
    {
        var model = new CollectionEditRoster { Quests = [new() { Id = "old", Title = "Old" }] };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(
            CollectionEditRoster.SparsePath.Quests
        );

        session.Observable.ReplaceQuests([new() { Id = "fresh", Title = "Fresh" }]);

        editor.Count.ShouldBe(1);
        editor.TryGetByKey("old", out _, out var gone).ShouldBeFalse();
        gone.ShouldBe(SparseCollectionEditError.UnknownKey);
        editor.TryGetByKey("fresh", out var fresh, out _).ShouldBeTrue();
        fresh!.Title.ShouldBe("Fresh");
        editor.TryAdd(new() { Id = "added", Title = "Added" }, out _).ShouldBeTrue();
        model.Quests.Count.ShouldBe(2);
    }

    [Test]
    public void UiRowKeys_ReadLivePerPosition()
    {
        var model = new CollectionEditRoster
        {
            Quests = [new() { Id = "a", Title = "Alpha" }, new() { Id = "b", Title = "Beta" }],
        };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<CollectionEditQuest, string>(
            CollectionEditRoster.SparsePath.Quests
        );

        editor.IsKeyed.ShouldBeTrue();
        editor.KeyType.ShouldBe(typeof(string));
        editor.KeyPropertyNames.ShouldBe(["Id"]);

        var keys = new List<string?>();
        for (var index = 0; index < editor.Count; index++)
        {
            editor.TryGetKeyAt(index, out var key, out _).ShouldBeTrue();
            keys.Add(key);
        }

        keys.ShouldBe(["a", "b"]);
    }
}
