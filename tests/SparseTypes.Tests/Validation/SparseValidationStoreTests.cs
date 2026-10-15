using System.ComponentModel;
using SparseTypes;

namespace SparseTypes.Tests;

/// <summary>Framework-neutral validation error store.</summary>
[SparseModel]
public partial class IntDictHolder
{
    public Dictionary<int, string> Scores { get; set; } = new();
}

public sealed class SparseValidationStoreTests
{
    private static SparseValidationStore<Settings> Store() => new();

    private static List<string?> Notifications(SparseValidationStore<Settings> store)
    {
        var names = new List<string?>();
        store.ErrorsChanged += (_, args) => names.Add(args.PropertyName);
        return names;
    }

    private static List<object?> NameErrors(INotifyDataErrorInfo store, string? propertyName) =>
        store.GetErrors(propertyName).Cast<object?>().ToList();

    [Test]
    public void Contract_AddReplaceClearRaiseNotifications()
    {
        var store = Store();
        var events = Notifications(store);
        INotifyDataErrorInfo notify = store;

        store.HasErrors.ShouldBeFalse();
        NameErrors(notify, "Label").ShouldBeEmpty();

        store.SetErrors(
            Settings.SparsePath.Label,
            SparseValidationOrigins.LocalValidation,
            ["required"]
        );
        store.HasErrors.ShouldBeTrue();
        notify.HasErrors.ShouldBeTrue();
        NameErrors(notify, "Label").ShouldBe(["required"]);
        NameErrors(notify, null).ShouldBe(["required"]);
        NameErrors(notify, string.Empty).ShouldBe(["required"]);
        events.ShouldBe(["Label", string.Empty]);

        // Replace with new content notifies again with the updated errors.
        store.SetErrors(
            Settings.SparsePath.Label,
            SparseValidationOrigins.LocalValidation,
            ["too short"]
        );
        NameErrors(notify, "Label").ShouldBe(["too short"]);
        events.ShouldBe(["Label", string.Empty, "Label", string.Empty]);

        // Clearing drops the errors and notifies once more.
        store.ClearErrors(Settings.SparsePath.Label);
        store.HasErrors.ShouldBeFalse();
        NameErrors(notify, null).ShouldBeEmpty();
        events.ShouldBe(["Label", string.Empty, "Label", string.Empty, "Label", string.Empty]);
    }

    [Test]
    public void IdenticalReplacement_RaisesNoEvents()
    {
        var store = Store();
        store.SetErrors(
            Settings.SparsePath.Label,
            SparseValidationOrigins.LocalValidation,
            ["required"]
        );
        var events = Notifications(store);

        store.SetErrors(
            Settings.SparsePath.Label,
            SparseValidationOrigins.LocalValidation,
            ["required"]
        );
        events.ShouldBeEmpty();
        store.HasErrors.ShouldBeTrue();
    }

    [Test]
    public void NestedAndKeyedPaths_ParentViewAggregatesDescendants()
    {
        var store = Store();
        var events = Notifications(store);

        var leaf = Settings.SparsePath.Nested.Host;
        store.SetErrors(leaf, SparseValidationOrigins.LocalValidation, ["host required"]);
        events.ShouldBe(["Nested.Host", "Nested", string.Empty]);

        // Exact typed query, dotted property-name query, and the parent view
        // agree.
        store.GetPathErrors(leaf).ShouldBe(["host required"]);
        NameErrors(store, "Nested.Host").ShouldBe(["host required"]);
        NameErrors(store, "Nested").ShouldBe(["host required"]);
        store.HasPathErrors(Settings.SparsePath.Nested).ShouldBeTrue();
        store.HasPathErrors(leaf).ShouldBeTrue();
        store.HasPathErrors(Settings.SparsePath.Label).ShouldBeFalse();

        // Keyed paths keep their identity and aggregate under the member.
        var keyed = SparsePath
            .Root<KeyedServerHolder>()
            .Member(nameof(KeyedServerHolder.Items))
            .Key("s-1")
            .Member(nameof(KeyedServer.Name));
        var keyedStore = new SparseValidationStore<KeyedServerHolder>();
        keyedStore.SetErrors(keyed, SparseValidationOrigins.ServerValidation, ["unknown sku"]);
        keyedStore.GetPathErrors(keyed).ShouldBe(["unknown sku"]);
        NameErrors(keyedStore, "Items").ShouldBe(["unknown sku"]);
        NameErrors(keyedStore, "Items[\"s-1\"].Name").ShouldBe(["unknown sku"]);
    }

    [Test]
    public void DictionaryPaths_WorkByTextAndTyped()
    {
        var store = new SparseValidationStore<ScalarDictHolder>();
        var entry = ScalarDictHolder.SparsePath.Scores.Key("a");
        store.SetErrors(entry, SparseValidationOrigins.LocalValidation, ["must be positive"]);

        store.GetPathErrors(entry).ShouldBe(["must be positive"]);
        NameErrors(store, "Scores[\"a\"]").ShouldBe(["must be positive"]);
        NameErrors(store, "Scores").ShouldBe(["must be positive"]);
        store.HasPathErrors(ScalarDictHolder.SparsePath.Scores).ShouldBeTrue();
    }

    [Test]
    public void IntKeyedDictionary_TextViewFindsErrors()
    {
        // Parsed text erases key types to strings; the wire-text fallback keeps
        // the property-name view working for non-string keys.
        var store = new SparseValidationStore<IntDictHolder>();
        var entry = SparsePath.Root<IntDictHolder>().Member(nameof(IntDictHolder.Scores)).Key(1);
        store.SetErrors(entry, SparseValidationOrigins.LocalValidation, ["nope"]);

        store.GetPathErrors(entry).ShouldBe(["nope"]);
        NameErrors(store, "Scores[\"1\"]").ShouldBe(["nope"]);
    }

    [Test]
    public void DeletedOrMissingTargets_AreRetainedNeverDropped()
    {
        var store = new SparseValidationStore<KeyedServerHolder>();
        var removed = SparsePath
            .Root<KeyedServerHolder>()
            .Member(nameof(KeyedServerHolder.Items))
            .Key("gone")
            .Member(nameof(KeyedServer.Name));
        store.SetErrors(removed, SparseValidationOrigins.RebaseConflict, ["entry vanished"]);

        // Unresolvable paths stay stored: exact, text, and aggregate queries
        // find them.
        store.GetPathErrors(removed).ShouldBe(["entry vanished"]);
        NameErrors(store, "Items[\"gone\"].Name").ShouldBe(["entry vanished"]);
        NameErrors(store, null).ShouldBe(["entry vanished"]);

        var missing = SparsePath.Root<Settings>().Member("Missing").Member("Deeper");
        var settings = Store();
        settings.SetErrors(missing, SparseValidationOrigins.LocalValidation, ["stale"]);
        settings.GetPathErrors(missing).ShouldBe(["stale"]);
        settings.HasErrors.ShouldBeTrue();
    }

    [Test]
    public void ForeignRootPaths_AreStoredButKeptOutOfTextViews()
    {
        var store = Store();
        var foreign = SparsePath.Root<Nested>().Member(nameof(Nested.Host));
        store.SetErrors(foreign, SparseValidationOrigins.LocalValidation, ["foreign"]);

        // Never dropped: typed and aggregate queries find the entry.
        store.GetPathErrors(foreign).ShouldBe(["foreign"]);
        NameErrors(store, null).ShouldBe(["foreign"]);
        store.HasErrors.ShouldBeTrue();

        // The property-name view only matches this store's root type.
        NameErrors(store, "Host").ShouldBeEmpty();
    }

    [Test]
    public void Origins_ReplaceAndClearSelectively()
    {
        var store = Store();
        var path = Settings.SparsePath.Label;

        store.SetErrors(path, SparseValidationOrigins.LocalValidation, ["local-1"]);
        store.SetErrors(path, SparseValidationOrigins.ServerValidation, ["server-1"]);
        store.SetErrors(path, SparseValidationOrigins.RebaseConflict, ["conflict-1"]);
        store.GetPathErrors(path).ShouldBe(["conflict-1", "local-1", "server-1"]);

        // Replacing one origin leaves the others untouched.
        store.SetErrors(path, SparseValidationOrigins.LocalValidation, ["local-2"]);
        store.GetPathErrors(path).ShouldBe(["conflict-1", "local-2", "server-1"]);

        // Clearing one origin leaves the others untouched.
        store.ClearErrors(path, SparseValidationOrigins.LocalValidation);
        store.GetPathErrors(path).ShouldBe(["conflict-1", "server-1"]);
        store.HasErrors.ShouldBeTrue();

        // Clearing the path drops every origin at once.
        store.ClearErrors(path);
        store.GetPathErrors(path).ShouldBeEmpty();
        store.HasErrors.ShouldBeFalse();

        // Origin-wide clearing spans paths but spares other origins.
        store.SetErrors(path, SparseValidationOrigins.LocalValidation, ["local"]);
        store.SetErrors(
            Settings.SparsePath.RetryCount,
            SparseValidationOrigins.LocalValidation,
            ["retry"]
        );
        store.SetErrors(path, SparseValidationOrigins.ServerValidation, ["server"]);
        store.ClearOrigin(SparseValidationOrigins.LocalValidation);
        store.GetPathErrors(path).ShouldBe(["server"]);
        store.HasPathErrors(Settings.SparsePath.RetryCount).ShouldBeFalse();

        // Whole-store clearing empties everything.
        store.Clear();
        store.HasErrors.ShouldBeFalse();
        NameErrors(store, null).ShouldBeEmpty();
    }

    [Test]
    public void Errors_DoNotChangeChangeSets()
    {
        var session = new Settings().CreateEditSession();
        session.Observable.RetryCount = 4;
        var before = (ISparseChangeSet)session.CreateChangeSet();
        before.IsEmpty.ShouldBeFalse();

        var store = Store();
        store.SetErrors(
            Settings.SparsePath.RetryCount,
            SparseValidationOrigins.LocalValidation,
            ["bad"]
        );
        store.SetErrors(
            Settings.SparsePath.Label,
            SparseValidationOrigins.ServerValidation,
            ["stale"]
        );
        store.AddConflicts([
            new SparseConflict(
                Settings.SparsePath.Label,
                SparseConflictKind.Scalar,
                Optional<object?>.Missing,
                Optional<object?>.Missing,
                Optional<object?>.Missing,
                null
            ),
        ]);

        // The session diff is byte-for-byte the same with errors recorded.
        var after = (ISparseChangeSet)session.CreateChangeSet();
        after.IsEmpty.ShouldBeFalse();
        after
            .EnumerateChangeRecords()
            .Select(static record => record.PathText)
            .ShouldBe(before.EnumerateChangeRecords().Select(static record => record.PathText));
        session.FieldState(Settings.SparsePath.Label).HasChanges.ShouldBeFalse();
    }

    [Test]
    public void OptOut_SessionsCarryNoValidationState()
    {
        var session = new Settings().CreateEditSession();
        session.GetType().GetInterfaces().ShouldNotContain(typeof(INotifyDataErrorInfo));
        session.GetType().GetEvents().Select(static e => e.Name).ShouldNotContain("ErrorsChanged");

        // Stores are independent: mutating one never leaks into another.
        var first = Store();
        var second = Store();
        first.SetErrors(Settings.SparsePath.Label, SparseValidationOrigins.LocalValidation, ["x"]);
        first.HasErrors.ShouldBeTrue();
        second.HasErrors.ShouldBeFalse();
    }

    [Test]
    public void ExternalResults_And_RebasedConflicts_FlowToNotifications()
    {
        // External validators run app-side and feed results explicitly.
        var store = Store();
        var events = Notifications(store);
        store.SetErrors(
            Settings.SparsePath.Label,
            SparseValidationOrigins.LocalValidation,
            ["Label is required.", 42]
        );
        var stored = store.GetPathErrors(Settings.SparsePath.Label);
        stored.Count.ShouldBe(2);
        stored[0].ShouldBe("Label is required.");
        stored[1].ShouldBe(42);

        // A genuine rebase produces conflicts that map into the store
        // untouched.
        var baseModel = new Settings();
        var edited = new Settings { Label = "local" };
        var current = new Settings { Label = "server" };
        var rebase = baseModel
            .CreateChangeSet(edited)
            .RebaseOnto(Optional<Settings.Fragment?>.Present(Settings.Fragment.From(current)));
        rebase.HasConflicts.ShouldBeTrue();
        rebase.Conflicts.Select(static conflict => conflict.PathText).ShouldContain("Label");

        var added = store.AddConflicts(rebase.Conflicts);
        added.ShouldBe(rebase.Conflicts.Count);
        rebase.HasConflicts.ShouldBeTrue();

        var labelErrors = store.GetPathErrors(Settings.SparsePath.Label);
        labelErrors.Count.ShouldBe(3);
        (labelErrors[0]?.ToString() ?? string.Empty).ShouldContain("Conflict at 'Label'");
        labelErrors[1].ShouldBe("Label is required.");
        labelErrors[2].ShouldBe(42);
        events.ShouldContain("Label");
        events.ShouldContain(string.Empty);
        NameErrors(store, null).Count.ShouldBe(3);
    }

    [Test]
    public void AddConflicts_ReplaceOriginFirst_And_CustomMessages()
    {
        var store = Store();
        var path = Settings.SparsePath.Label;
        var conflict = new SparseConflict(
            path,
            SparseConflictKind.Scalar,
            Optional<object?>.Missing,
            Optional<object?>.Missing,
            Optional<object?>.Missing,
            "boom"
        );

        store.SetErrors(path, SparseValidationOrigins.LocalValidation, ["local"]);
        var options = new SparseValidationConflictOptions
        {
            MessageFactory = static c => $"CUSTOM:{c.PathText}",
        };
        store.AddConflicts([conflict], options).ShouldBe(1);
        store.GetPathErrors(path).ShouldBe(["CUSTOM:Label", "local"]);

        // Replace-first clears only the conflict origin, sparing local errors.
        store
            .AddConflicts(
                [conflict, conflict],
                new SparseValidationConflictOptions { ReplaceOriginFirst = true }
            )
            .ShouldBe(2);
        var merged = store.GetPathErrors(path);
        merged.Count.ShouldBe(3);
        (merged[0]?.ToString() ?? string.Empty).ShouldContain("Conflict at 'Label'");
        (merged[1]?.ToString() ?? string.Empty).ShouldContain("Conflict at 'Label'");
        merged[2].ShouldBe("local");
    }

    [Test]
    public void DescriptorPath_UsesExplicitMetadataFlow()
    {
        var session = new Settings().CreateEditSession();
        session.Descriptors.TryGet(nameof(Settings.Label), out var descriptor).ShouldBeTrue();
        descriptor.Attributes.ShouldNotBeNull();

        var store = Store();
        store.SetErrors(descriptor, SparseValidationOrigins.LocalValidation, ["picked by app"]);
        store.GetPathErrors(descriptor.Path).ShouldBe(["picked by app"]);
        NameErrors(store, descriptor.PathText).ShouldBe(["picked by app"]);

        store.ClearErrors(descriptor, SparseValidationOrigins.LocalValidation);
        store.HasErrors.ShouldBeFalse();
    }

    [Test]
    public void RootErrors_SurfaceAtEntityLevel()
    {
        var store = Store();
        var events = Notifications(store);
        store.SetErrors(
            SparsePath.Root<Settings>(),
            SparseValidationOrigins.ServerValidation,
            ["stale"]
        );

        store.HasErrors.ShouldBeTrue();
        store.HasPathErrors(SparsePath.Root<Settings>()).ShouldBeTrue();
        store.HasPathErrors(Settings.SparsePath.Label).ShouldBeFalse();
        NameErrors(store, null).ShouldBe(["stale"]);
        NameErrors(store, string.Empty).ShouldBe(["stale"]);
        NameErrors(store, "$root").ShouldBe(["stale"]);
        events.ShouldBe([string.Empty]);
    }

    [Test]
    public void Dispose_ClearsNotifiesAndRejectsUse()
    {
        var store = Store();
        var events = Notifications(store);
        store.SetErrors(Settings.SparsePath.Label, SparseValidationOrigins.LocalValidation, ["x"]);
        events.Clear();

        store.Dispose();
        store.IsDisposed.ShouldBeTrue();
        events.ShouldBe(["Label", string.Empty]);
        store.IsDisposed.ShouldBeTrue();

        Should.Throw<ObjectDisposedException>(() => store.HasErrors.ShouldBeFalse());
        Should.Throw<ObjectDisposedException>(() =>
            store.SetErrors(
                Settings.SparsePath.Label,
                SparseValidationOrigins.LocalValidation,
                ["y"]
            )
        );
        Should.Throw<ObjectDisposedException>(() => store.Clear());
        Should.Throw<ObjectDisposedException>(() => store.GetPathErrors(Settings.SparsePath.Label));

        // Disposal is idempotent and never throws.
        store.Dispose();
    }

    [Test]
    public void ConcurrentUse_StaysConsistent()
    {
        var store = Store();
        System.Threading.Tasks.Parallel.For(
            0,
            64,
            index =>
                store.SetErrors(
                    SparsePath.Root<Settings>().Member($"Field{index}"),
                    SparseValidationOrigins.LocalValidation,
                    [$"error{index}"]
                )
        );

        store.HasErrors.ShouldBeTrue();
        store.GetAllErrors().Count.ShouldBe(64);
        NameErrors(store, null).Count.ShouldBe(64);

        store.Clear();
        store.HasErrors.ShouldBeFalse();
    }

    [Test]
    public void GetAllErrors_ReturnsDeterministicSnapshots()
    {
        var store = Store();
        store.SetErrors(
            Settings.SparsePath.RetryCount,
            SparseValidationOrigins.ServerValidation,
            ["s"]
        );
        store.SetErrors(Settings.SparsePath.Label, SparseValidationOrigins.LocalValidation, ["l"]);

        var snapshot = store.GetAllErrors();
        snapshot.Count.ShouldBe(2);
        snapshot[0].PathText.ShouldBe("Label");
        snapshot[0].Origin.ShouldBe(SparseValidationOrigins.LocalValidation);
        snapshot[0].Error.ShouldBe("l");
        snapshot[1].PathText.ShouldBe("RetryCount");

        // Later mutations never rewrite previously returned snapshots.
        store.Clear();
        snapshot.Count.ShouldBe(2);
    }
}
