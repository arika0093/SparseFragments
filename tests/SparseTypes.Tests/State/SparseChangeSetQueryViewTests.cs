using System.Diagnostics.CodeAnalysis;
using SparseTypes.Generated;

namespace SparseTypes.Tests;

/// <summary>Reusable indexed query view over one captured change set.</summary>
public sealed class SparseChangeSetQueryViewTests
{
    private sealed class StubChangeSet : ISparseChangeSet
    {
        public Func<IReadOnlyList<SparseChangeRecord>>? Records { get; set; }

        public int Calls;

        public bool IsEmpty => true;

        public bool IsChanged => false;

        public IReadOnlyList<SparseChangeRecord> EnumerateChangeRecords()
        {
            Calls++;
            return Records!();
        }

        public bool TryGetNestedChange(
            string memberName,
            [NotNullWhen(true)] out ISparseChangeSet? child
        )
        {
            child = null;
            return false;
        }

        public bool TryGetValueChange<T>(
            string memberName,
            [NotNullWhen(true)] out ISparseValueChange<T>? change
        )
        {
            change = null;
            return false;
        }

        public bool TryGetKeyedChange<TKey, TElement, TElementChange>(
            string memberName,
            [NotNullWhen(true)] out ISparseKeyedChange<TKey, TElement, TElementChange>? change
        )
        {
            change = null;
            return false;
        }

        public bool TryGetDictionaryChange<TKey, TValue, TEdited>(
            string memberName,
            [NotNullWhen(true)] out ISparseDictionaryChange<TKey, TValue, TEdited>? change
        )
        {
            change = null;
            return false;
        }
    }

    private static SparseChangeSetQueryView ViewOf(params SparseChangeRecord[] records) =>
        SparseChangeSetQueryView.Capture(new StubChangeSet { Records = () => records });

    [Test]
    public void ViewMatchesScanAcrossExactAncestorAndDescendant()
    {
        var session = new Settings().CreateEditSession();
        session.Observable.RetryCount = 4;
        session.Observable.Nested!.Host = "edited";
        var changes = session.CreateChangeSet();
        var view = changes.CaptureQueryView();

        var queries = new SparsePath[]
        {
            SparsePath.Root<Settings>(),
            SparsePath.Root<Settings>().Member("RetryCount"),
            SparsePath.Root<Settings>().Member("Enabled"),
            SparsePath.Root<Settings>().Member("Nested"),
            SparsePath.Root<Settings>().Member("Nested").Member("Host"),
            SparsePath.Root<Settings>().Member("Nested").Member("Port"),
            SparsePath.Root<Settings>().Member("Missing"),
            SparsePath.Root<Settings>().Member("Missing").Member("Deeper"),
            SparsePath.Root<Nested>().Member("Host"),
        };

        foreach (var query in queries)
        {
            var scanned = changes.FieldState(query);
            var indexed = view.FieldState(query);
            indexed.Path.ShouldBe(scanned.Path);
            indexed.IsChanged.ShouldBe(scanned.IsChanged);
            indexed.Kind.ShouldBe(scanned.Kind);
            indexed.HasAncestorChange.ShouldBe(scanned.HasAncestorChange);
            indexed.HasDescendantChange.ShouldBe(scanned.HasDescendantChange);
            indexed.Before.ShouldBe(scanned.Before);
            indexed.After.ShouldBe(scanned.After);
        }

        var typedScanned = changes.FieldState(Settings.SparsePath.RetryCount);
        var typedIndexed = view.FieldState(Settings.SparsePath.RetryCount);
        typedIndexed.ShouldBe(typedScanned);
    }

    [Test]
    public void KeyedAndTemporaryQueriesMatchScan()
    {
        var session = new KeyedServerHolder
        {
            Items = new()
            {
                new KeyedServer { Id = "a", Name = "a" },
                new KeyedServer { Id = "b", Name = "b" },
            },
        }.CreateEditSession();
        session.Observable.Items[1].Name = "edited";
        var changes = session.CreateChangeSet();
        var view = changes.CaptureQueryView();

        var queries = new SparsePath[]
        {
            KeyedServerHolder.SparsePath.Items.UntypedPath,
            KeyedServerHolder.SparsePath.Items.Key("b").UntypedPath,
            KeyedServerHolder.SparsePath.Items.Key("b").Name.UntypedPath,
            KeyedServerHolder.SparsePath.Items.Key("missing").UntypedPath,
        };

        foreach (var query in queries)
        {
            var scanned = changes.FieldState(query);
            var indexed = view.FieldState(query);
            indexed.IsChanged.ShouldBe(scanned.IsChanged);
            indexed.Kind.ShouldBe(scanned.Kind);
            indexed.HasAncestorChange.ShouldBe(scanned.HasAncestorChange);
            indexed.HasDescendantChange.ShouldBe(scanned.HasDescendantChange);
            indexed.Before.ShouldBe(scanned.Before);
            indexed.After.ShouldBe(scanned.After);
        }
    }

    [Test]
    public void FirstRecordWinsForRepeatedExactPaths()
    {
        var path = SparsePath.Root<Settings>().Member("RetryCount");
        var view = ViewOf(
            new SparseChangeRecord(
                path,
                Optional<object?>.Present(3),
                Optional<object?>.Present(4),
                SparseChangeKind.Changed
            ),
            new SparseChangeRecord(
                path,
                Optional<object?>.Present(4),
                Optional<object?>.Present(5),
                SparseChangeKind.Changed
            )
        );

        var state = view.FieldState(Settings.SparsePath.RetryCount);
        state.IsChanged.ShouldBeTrue();
        state.Before.ShouldBe(Optional<int>.Present(3));
        state.After.ShouldBe(Optional<int>.Present(4));
    }

    [Test]
    public void TypedKeysKeepIdentityAcrossIdenticalText()
    {
        var root = SparsePath.Root<Settings>();
        var intKey = root.Member("Items").Key(1).Member("Name");
        var stringKey = root.Member("Items").Key("1").Member("Name");
        intKey.ToString().ShouldBe(stringKey.ToString());

        var view = ViewOf(
            new SparseChangeRecord(
                intKey,
                Optional<object?>.Present("before"),
                Optional<object?>.Present("after"),
                SparseChangeKind.Changed
            )
        );

        // Distinct typed keys never merge through display text.
        view.FieldState(intKey).IsChanged.ShouldBeTrue();
        var miss = view.FieldState(stringKey);
        miss.IsChanged.ShouldBeFalse();
        miss.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void RootTypeSeparatesIdenticalSegments()
    {
        var settingsPath = SparsePath.Root<Settings>().Member("RetryCount");
        var nestedPath = SparsePath.Root<Nested>().Member("Host");
        var view = ViewOf(
            new SparseChangeRecord(
                settingsPath,
                Optional<object?>.Present(3),
                Optional<object?>.Present(4),
                SparseChangeKind.Changed
            )
        );

        view.FieldState(settingsPath).IsChanged.ShouldBeTrue();
        view.FieldState(nestedPath).HasChanges.ShouldBeFalse();
    }

    [Test]
    public void RootChangeMarksEveryDescendantAsAncestor()
    {
        var root = SparsePath.Root<Settings>();
        var leaf = root.Member("Nested").Member("Host");
        var view = ViewOf(
            new SparseChangeRecord(
                root,
                Optional<object?>.Missing,
                Optional<object?>.Missing,
                SparseChangeKind.Changed
            )
        );

        var rootState = view.FieldState(root);
        rootState.IsChanged.ShouldBeTrue();
        rootState.HasDescendantChange.ShouldBeFalse();

        var leafState = view.FieldState(leaf);
        leafState.IsChanged.ShouldBeFalse();
        leafState.HasAncestorChange.ShouldBeTrue();
        leafState.HasDescendantChange.ShouldBeFalse();
    }

    [Test]
    public void ConversionFailureMatchesScan()
    {
        var session = new Settings().CreateEditSession();
        session.Observable.RetryCount = 4;
        var changes = session.CreateChangeSet();
        var view = changes.CaptureQueryView();

        // RetryCount holds an int; reading it as a string fails on both paths.
        var bad = new SparsePath<Settings, string>(
            SparsePath.Root<Settings>().Member("RetryCount")
        );
        Should.Throw<InvalidOperationException>(() => changes.FieldState(bad));
        Should.Throw<InvalidOperationException>(() => view.FieldState(bad));
    }

    [Test]
    public void CaptureSnapshotsChangingSourcesOnce()
    {
        var path = SparsePath.Root<Settings>().Member("RetryCount");
        var stub = new StubChangeSet { Records = () => Array.Empty<SparseChangeRecord>() };
        var view = SparseChangeSetQueryView.Capture(stub);
        view.FieldState(path).HasChanges.ShouldBeFalse();

        // Later source mutations are not observed.
        stub.Records = () =>
            new[]
            {
                new SparseChangeRecord(
                    path,
                    Optional<object?>.Present(3),
                    Optional<object?>.Present(4),
                    SparseChangeKind.Changed
                ),
            };
        view.FieldState(path).HasChanges.ShouldBeFalse();
        stub.Calls.ShouldBe(1);
    }

    [Test]
    public void RepeatedQueriesProjectOnce()
    {
        var session = new Settings().CreateEditSession();
        session.Observable.RetryCount = 4;
        session.Observable.Nested!.Host = "edited";
        var view = session.CreateChangeSet().CaptureQueryView();

        for (var index = 0; index < 25; index++)
        {
            view.FieldState(Settings.SparsePath.RetryCount);
            view.FieldState(Settings.SparsePath.Nested.Host);
            view.FieldState(Settings.SparsePath.Enabled);
        }

        view.ProjectionCount.ShouldBe(1);
    }

    [Test]
    public void CaptureRejectsNull()
    {
        Should.Throw<ArgumentNullException>(() => SparseChangeSetQueryView.Capture(null!));
        Should.Throw<ArgumentNullException>(() => ((ISparseChangeSet)null!).CaptureQueryView());
    }
}
