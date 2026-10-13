namespace SparseFragments.Tests;

public sealed class EditSessionScopeTests
{
    [Test]
    public void NestedScopeReResolvesAReplacementChildBeforeCommit()
    {
        var model = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        using var scope = session.OpenScope(ObservableHolder.SparsePath.Child);
        scope.Observable.Name = "scoped";

        var replacement = new ObservableChild { Name = "before", Count = 2 };
        model.Child = replacement;

        scope.TryCommit(out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        ReferenceEquals(model.Child, replacement).ShouldBeTrue();
        model.Child.Name.ShouldBe("scoped");
        model.Child.Count.ShouldBe(2);
    }

    [Test]
    public void ScopeCommitsDisjointParentChangesAndPreservesTheBaseline()
    {
        var model = new KeyedServerHolder
        {
            Items =
            [
                new KeyedServer
                {
                    Id = "server",
                    Name = "before",
                    Count = 1,
                },
            ],
        };
        var session = model.CreateEditSession();
        using var scope = session.OpenScope(KeyedServerHolder.SparsePath.Items.Key("server"));

        scope.Observable.Name = "scoped";
        session.Observable.Items[0].Count = 2;

        scope.HasChanges.ShouldBeTrue();
        scope.TryCommit(out var conflicts).ShouldBeTrue();
        conflicts.ShouldBeNull();
        scope.State.ShouldBe(KeyedServerHolder.EditSession.ScopeState.Committed);
        session.Observable.Items[0].Name.ShouldBe("scoped");
        session.Observable.Items[0].Count.ShouldBe(2);
        var changes = session.CreateChangeSet().EnumerateChanges();
        changes.Any(change => change.PathText == "Items[\"server\"].Name").ShouldBeTrue();
        changes.Any(change => change.PathText == "Items[\"server\"].Count").ShouldBeTrue();
        model.Items[0].Name.ShouldBe("scoped");
    }

    [Test]
    public void ConflictingScopeCommitLeavesTheParentUntouched()
    {
        var model = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "server", Name = "before" }],
        };
        var session = model.CreateEditSession();
        using var scope = session.OpenScope(KeyedServerHolder.SparsePath.Items.Key("server"));

        scope.Observable.Name = "draft";
        session.Observable.Items[0].Name = "parent";

        scope.TryCommit(out var conflicts).ShouldBeFalse();
        conflicts.ShouldNotBeNull();
        conflicts!.Count.ShouldBeGreaterThan(0);
        conflicts[0].PathText.ShouldBe("Items[\"server\"].Name");
        scope.State.ShouldBe(KeyedServerHolder.EditSession.ScopeState.Open);
        session.Observable.Items[0].Name.ShouldBe("parent");
    }

    [Test]
    public void KeyedScopeTracksTheTargetAfterReordering()
    {
        var model = new KeyedServerHolder
        {
            Items =
            [
                new KeyedServer { Id = "first", Name = "one" },
                new KeyedServer { Id = "second", Name = "two" },
            ],
        };
        var session = model.CreateEditSession();
        using var scope = session.OpenScope(KeyedServerHolder.SparsePath.Items.Key("second"));
        scope.Observable.Name = "updated";

        session.Observable.Items.Move(1, 0);

        scope.TryCommit(out var conflicts).ShouldBeTrue();
        conflicts.ShouldBeNull();
        session.Observable.Items[0].Id.ShouldBe("second");
        session.Observable.Items[0].Name.ShouldBe("updated");
    }

    [Test]
    public void DeletedTargetClosesTheScopeAsMissing()
    {
        var model = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "server", Name = "before" }],
        };
        var session = model.CreateEditSession();
        using var scope = session.OpenScope(KeyedServerHolder.SparsePath.Items.Key("server"));
        scope.Observable.Name = "draft";
        session.Observable.Items.RemoveAt(0);

        scope.TryCommit(out var conflicts).ShouldBeFalse();
        conflicts.ShouldBeNull();
        scope.State.ShouldBe(KeyedServerHolder.EditSession.ScopeState.TargetMissing);
        session.Observable.Items.Count.ShouldBe(0);
    }

    [Test]
    public void CancelAndNoOpCommitDoNotChangeTheParent()
    {
        var model = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "server", Name = "before" }],
        };
        var session = model.CreateEditSession();
        using (var canceled = session.OpenScope(KeyedServerHolder.SparsePath.Items.Key("server")))
        {
            canceled.Observable.Name = "discarded";
            canceled.Cancel();
            canceled.State.ShouldBe(KeyedServerHolder.EditSession.ScopeState.Canceled);
        }

        using var unchanged = session.OpenScope(KeyedServerHolder.SparsePath.Items.Key("server"));
        unchanged.HasChanges.ShouldBeFalse();
        unchanged.TryCommit(out var conflicts).ShouldBeTrue();
        conflicts.ShouldBeNull();
        session.HasChanges.ShouldBeFalse();
        model.Items[0].Name.ShouldBe("before");
    }

    [Test]
    public void TemporaryKeyScopeCommitsAnUnassignedElement()
    {
        var temporaryId = Guid.NewGuid();
        var model = new TempOrderHolder
        {
            Lines =
            [
                new TempOrderLine
                {
                    Id = 0,
                    TemporaryId = temporaryId,
                    Name = "before",
                },
            ],
        };
        var session = model.CreateEditSession();
        using var scope = session.OpenScope(
            TempOrderHolder.SparsePath.Lines.TemporaryKey(temporaryId)
        );
        scope.Observable.Name = "updated";

        scope.TryCommit(out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        session.Observable.Lines[0].Name.ShouldBe("updated");
        session.Observable.Lines[0].TemporaryId.ShouldBe(temporaryId);
    }

    [Test]
    public void DictionaryValueScopeCommitsThroughItsKey()
    {
        var model = new StructuralDictHolder
        {
            Servers = new()
            {
                ["server"] = new KeyedServer { Id = "server", Name = "before" },
            },
        };
        var session = model.CreateEditSession();
        using var scope = session.OpenScope(StructuralDictHolder.SparsePath.Servers.Key("server"));
        scope.Observable.Name = "updated";

        scope.TryCommit(out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        session.Observable.Servers["server"].Name.ShouldBe("updated");
    }

    [Test]
    public void ScopeImplementsTheChildEditSessionContract()
    {
        var model = new ClusterHolder
        {
            Groups =
            [
                new ServerGroup
                {
                    Name = "group",
                    Servers =
                    [
                        new KeyedServer
                        {
                            Id = "server",
                            Name = "before",
                            Count = 1,
                        },
                    ],
                },
            ],
        };
        var session = model.CreateEditSession();
        using var scope = session.OpenScope(ClusterHolder.SparsePath.Groups.Key("group"));
        ISparseEditSession<ServerGroup> childSession = scope;

        childSession.Model.Servers[0].Count = 2;
        childSession.HasChanges.ShouldBeTrue();
        childSession.AcceptChanges();

        scope.State.ShouldBe(ClusterHolder.EditSession.ScopeState.Committed);
        session.Observable.Groups[0].Servers[0].Count.ShouldBe(2);
    }

    [Test]
    public void OpenDraftStateTracksRootAndNestedScopeLifetimes()
    {
        var model = new ClusterHolder
        {
            Groups =
            [
                new ServerGroup
                {
                    Name = "group",
                    Servers = [new KeyedServer { Id = "server", Name = "before" }],
                },
            ],
        };
        var session = model.CreateEditSession();
        ISparseEditSessionOpenDraftState rootDraftState = session;
        rootDraftState.HasOpenDrafts.ShouldBeFalse();

        using var groupScope = session.OpenScope(ClusterHolder.SparsePath.Groups.Key("group"));
        ISparseEditSessionOpenDraftState groupDraftState = groupScope;
        rootDraftState.HasOpenDrafts.ShouldBeTrue();
        groupDraftState.HasOpenDrafts.ShouldBeFalse();

        using var serverScope = groupScope.OpenScope(ServerGroup.SparsePath.Servers.Key("server"));
        groupDraftState.HasOpenDrafts.ShouldBeTrue();
        rootDraftState.HasOpenDrafts.ShouldBeTrue();

        serverScope.Cancel();

        groupDraftState.HasOpenDrafts.ShouldBeFalse();
        rootDraftState.HasOpenDrafts.ShouldBeTrue();

        groupScope.Cancel();

        rootDraftState.HasOpenDrafts.ShouldBeFalse();
    }

    [Test]
    public void NestedScopesCommitIntoTheirParentDraft()
    {
        var model = new ClusterHolder
        {
            Groups =
            [
                new ServerGroup
                {
                    Name = "group",
                    Servers = [new KeyedServer { Id = "server", Name = "before" }],
                },
            ],
        };
        var session = model.CreateEditSession();
        using var groupScope = session.OpenScope(ClusterHolder.SparsePath.Groups.Key("group"));
        using var serverScope = groupScope.OpenScope(ServerGroup.SparsePath.Servers.Key("server"));
        serverScope.Observable.Name = "nested";

        serverScope.AcceptChanges();
        serverScope.State.ShouldBe(ClusterHolder.EditSession.ScopeState.Committed);
        groupScope.HasOpenDrafts.ShouldBeFalse();
        session.HasOpenDrafts.ShouldBeTrue();
        groupScope.HasChanges.ShouldBeTrue();
        groupScope.AcceptChanges();

        session.HasOpenDrafts.ShouldBeFalse();
        session.Observable.Groups[0].Servers[0].Name.ShouldBe("nested");
    }

    [Test]
    public void NestedScopeFailsAfterItsParentIsCanceled()
    {
        var model = new ClusterHolder
        {
            Groups =
            [
                new ServerGroup
                {
                    Name = "group",
                    Servers = [new KeyedServer { Id = "server", Name = "before" }],
                },
            ],
        };
        var session = model.CreateEditSession();
        using var groupScope = session.OpenScope(ClusterHolder.SparsePath.Groups.Key("group"));
        using var serverScope = groupScope.OpenScope(ServerGroup.SparsePath.Servers.Key("server"));
        serverScope.Observable.Name = "nested";
        groupScope.Cancel();

        Should.Throw<InvalidOperationException>(() => serverScope.TryCommit(out _));

        serverScope.State.ShouldBe(ClusterHolder.EditSession.ScopeState.ParentInvalidated);
        serverScope.HasOpenDrafts.ShouldBeFalse();
        groupScope.HasOpenDrafts.ShouldBeFalse();
        session.HasOpenDrafts.ShouldBeFalse();
        session.Observable.Groups[0].Servers[0].Name.ShouldBe("before");
    }

    [Test]
    public void NestedScopeFailsAfterItsParentIsCommitted()
    {
        var model = new ClusterHolder
        {
            Groups =
            [
                new ServerGroup
                {
                    Name = "group",
                    Servers = [new KeyedServer { Id = "server", Name = "before" }],
                },
            ],
        };
        var session = model.CreateEditSession();
        using var groupScope = session.OpenScope(ClusterHolder.SparsePath.Groups.Key("group"));
        using var serverScope = groupScope.OpenScope(ServerGroup.SparsePath.Servers.Key("server"));
        serverScope.Observable.Name = "nested";
        groupScope.AcceptChanges();

        Should.Throw<InvalidOperationException>(() => serverScope.TryCommit(out _));

        serverScope.State.ShouldBe(ClusterHolder.EditSession.ScopeState.ParentInvalidated);
        serverScope.HasOpenDrafts.ShouldBeFalse();
        groupScope.HasOpenDrafts.ShouldBeFalse();
        session.HasOpenDrafts.ShouldBeFalse();
        session.Observable.Groups[0].Servers[0].Name.ShouldBe("before");
    }

    [Test]
    public void RootOpenScopeNullThrowsArgumentNullWithoutDraft()
    {
        var model = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        ISparseEditSessionOpenDraftState draftState = session;

        Should.Throw<ArgumentNullException>(() => session.OpenScope<ObservableChild>(null!));

        session.HasChanges.ShouldBeFalse();
        draftState.HasOpenDrafts.ShouldBeFalse();
        model.Child.Name.ShouldBe("before");
    }

    [Test]
    public void NestedOpenScopeNullThrowsArgumentNullWithoutDraft()
    {
        var model = new ClusterHolder
        {
            Groups =
            [
                new ServerGroup
                {
                    Name = "group",
                    Servers = [new KeyedServer { Id = "server", Name = "before" }],
                },
            ],
        };
        var session = model.CreateEditSession();
        using var groupScope = session.OpenScope(ClusterHolder.SparsePath.Groups.Key("group"));
        ISparseEditSessionOpenDraftState groupDraftState = groupScope;

        Should.Throw<ArgumentNullException>(() => groupScope.OpenScope<KeyedServer>(null!));

        groupDraftState.HasOpenDrafts.ShouldBeFalse();
        groupScope.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void RootOpenScopeWrongTypeThrowsWithoutDraft()
    {
        var model = new ClusterHolder
        {
            Groups =
            [
                new ServerGroup
                {
                    Name = "group",
                    Servers = [new KeyedServer { Id = "server", Name = "before" }],
                },
            ],
        };
        var session = model.CreateEditSession();
        ISparseEditSessionOpenDraftState draftState = session;

        // The path addresses a ServerGroup, not a KeyedServer. The raw
        // struct wrapper only checks the root type, so the value mismatch
        // surfaces at runtime from OpenScope validation.
        var mismatched = new SparsePath<ClusterHolder, KeyedServer>(
            ClusterHolder.SparsePath.Groups.Key("group")
        );
        Should.Throw<ArgumentException>(() => session.OpenScope(mismatched));

        session.HasChanges.ShouldBeFalse();
        draftState.HasOpenDrafts.ShouldBeFalse();
        session.Observable.Groups[0].Name.ShouldBe("group");
    }

    [Test]
    public void RootOpenScopeMissingKeyThrowsWithoutDraft()
    {
        var model = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "server", Name = "before" }],
        };
        var session = model.CreateEditSession();
        ISparseEditSessionOpenDraftState draftState = session;

        Should.Throw<ArgumentException>(() =>
            session.OpenScope(KeyedServerHolder.SparsePath.Items.Key("missing"))
        );

        session.HasChanges.ShouldBeFalse();
        draftState.HasOpenDrafts.ShouldBeFalse();
    }

    [Test]
    public void RootOpenScopeNonWritablePathThrowsWithoutDraft()
    {
        var model = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        ISparseEditSessionOpenDraftState draftState = session;
        // The raw typed path resolves to the child but reports itself as
        // non-writable, unlike generated SparsePaths builders.
        var readOnly = new SparsePath<ObservableHolder, ObservableChild>(
            ObservableHolder.SparsePath.Child
        );

        var exception = Should.Throw<ArgumentException>(() => session.OpenScope(readOnly));
        exception.Message.ShouldContain("generated model value");

        session.HasChanges.ShouldBeFalse();
        draftState.HasOpenDrafts.ShouldBeFalse();
    }

    [Test]
    public void NestedOpenScopeWrongTypeThrowsWithoutDraft()
    {
        var model = new ClusterHolder
        {
            Groups =
            [
                new ServerGroup
                {
                    Name = "group",
                    Servers = [new KeyedServer { Id = "server", Name = "before" }],
                },
            ],
        };
        var session = model.CreateEditSession();
        using var groupScope = session.OpenScope(ClusterHolder.SparsePath.Groups.Key("group"));
        ISparseEditSessionOpenDraftState groupDraftState = groupScope;

        // The path addresses a KeyedServer, not a ServerGroup.
        var mismatched = new SparsePath<ServerGroup, ServerGroup>(
            ServerGroup.SparsePath.Servers.Key("server")
        );
        Should.Throw<ArgumentException>(() => groupScope.OpenScope(mismatched));

        groupDraftState.HasOpenDrafts.ShouldBeFalse();
        groupScope.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void InvalidOpenScopeDoesNotCloneTheRoot()
    {
        var model = new KeyedServerHolder
        {
            Items = Enumerable
                .Range(0, 5000)
                .Select(index => new KeyedServer
                {
                    Id = "server-" + index,
                    Name = new string('n', 64),
                    Count = index,
                })
                .ToList(),
        };
        var session = model.CreateEditSession();
        var valid = KeyedServerHolder.SparsePath.Items.Key("server-0");

        // Warm up JIT and descriptor caches so measured bytes are model data.
        // Each distinct failure shape is warmed up: first-time path resolution
        // populates descriptor caches even when the key is missing.
        try
        {
            session.OpenScope<KeyedServer>(null!);
        }
        catch (ArgumentNullException) { }

        try
        {
            session.OpenScope(KeyedServerHolder.SparsePath.Items.Key("missing"));
        }
        catch (ArgumentException) { }

        using (session.OpenScope(valid)) { }

        var before = GC.GetAllocatedBytesForCurrentThread();
        using (session.OpenScope(valid)) { }
        var successBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        successBytes.ShouldBeGreaterThan(0);

        before = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            session.OpenScope<KeyedServer>(null!);
        }
        catch (ArgumentNullException) { }

        var nullBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            session.OpenScope(KeyedServerHolder.SparsePath.Items.Key("missing"));
        }
        catch (ArgumentException) { }

        var missingBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        // Failed opens validate before Fork, so no whole-root clone happens:
        // a null path allocates only the exception, and a missing key pays
        // just the (pre-existing, linear) key-scan resolution. Both stay below
        // a successful open, which additionally clones the entire root.
        nullBytes.ShouldBeLessThan(successBytes / 10);
        missingBytes.ShouldBeLessThan(successBytes);
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void DescriptorSetResolvesNestedMemberKeyAndTemporaryKeyValues()
    {
        var temporaryId = Guid.NewGuid();
        var model = new TempOrderHolder
        {
            Lines = [new TempOrderLine { Id = 0, TemporaryId = temporaryId }],
        };
        var session = model.CreateEditSession();

        session
            .Descriptors.ResolveModel(TempOrderHolder.SparsePath.Lines.TemporaryKey(temporaryId))
            .ShouldBeSameAs(model.Lines[0]);
        session
            .Descriptors.ResolveModel(TempOrderHolder.SparsePath.Lines.TemporaryKey(Guid.NewGuid()))
            .ShouldBeNull();
    }
}
