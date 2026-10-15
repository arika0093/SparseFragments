extern alias Playground;

using System.Reflection;
using System.Text.Json;
using Playground::SparseTypes.Playground.Models;
using Playground::SparseTypes.Playground.Pages.Case4;

namespace SparseTypes.Tests;

/// <summary>Case 4 submit preview derives from the session ChangeSet snapshot.</summary>
/// <remarks>Size loops carry timeouts so a non-terminating preview fails loudly instead of hanging the run.</remarks>
public sealed class Case4SubmitPreviewTests
{
    private const string MissingText = "—";

    private static PlaygroundSettings BaseModel() =>
        new()
        {
            Enabled = true,
            RetryCount = 3,
            Label = "Draft",
            Password = "old-secret",
            Nested = new PlaygroundNested { Host = "localhost", Port = 5432 },
            Plugins = ["base-plugin"],
            Tags = new HashSet<string>(StringComparer.Ordinal),
            Tasks =
            [
                new PlaygroundTask
                {
                    Id = "t1",
                    Title = "Find the key",
                    Points = 10,
                },
                new PlaygroundTask
                {
                    Id = "t2",
                    Title = "Open the gate",
                    Points = 25,
                },
                new PlaygroundTask
                {
                    Id = "t3",
                    Title = "Clear the trial",
                    Points = 40,
                },
            ],
        };

    private static PlaygroundSettings TaskModel(int count) =>
        new()
        {
            Tasks = Enumerable
                .Range(1, count)
                .Select(index => new PlaygroundTask
                {
                    Id = $"t{index}",
                    Title = $"Task {index}",
                    Points = index,
                })
                .ToList(),
        };

    private static void AssertPreviewEqualsLegacy(
        PlaygroundSettings baseline,
        PlaygroundSettings current,
        PlaygroundSettings.ChangeSet snapshot
    )
    {
        var expected = LegacyBuild(baseline, current);
        var actual = SubmitPreview.Build(snapshot);
        actual.Count.ShouldBe(expected.Count);
        foreach (var (row, legacy) in actual.Zip(expected))
        {
            row.Target.ShouldBe(legacy.Target);
            row.Operation.ShouldBe(legacy.Operation);
            row.Before.ShouldBe(legacy.Before);
            row.After.ShouldBe(legacy.After);
        }
    }

    [Test, Timeout(30_000)]
    public void ScalarEditsMatchLegacy(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = BaseModel();
        var baseline = model.DeepClone();
        var session = model.CreateEditSession();
        session.BatchEdit(() =>
        {
            session.Observable.Enabled = false;
            session.Observable.RetryCount = 8;
            session.Observable.Label = "Batch preset";
        });

        AssertPreviewEqualsLegacy(baseline, model, session.CreateChangeSet());
    }

    [Test, Timeout(30_000)]
    public void PasswordEditMatchesLegacyAndStaysMasked(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = BaseModel();
        var baseline = model.DeepClone();
        var session = model.CreateEditSession();
        session.Observable.Password = "new-secret";

        AssertPreviewEqualsLegacy(baseline, model, session.CreateChangeSet());

        var rows = SubmitPreview.Build(session.CreateChangeSet());
        var password = rows.ShouldHaveSingleItem();
        password.Target.ShouldBe("Password");
        password.Operation.ShouldBe("Edit");
        password.Before.ShouldBe("[masked]");
        password.After.ShouldBe("[masked]");

        // The dialog rows, their serialization, and the baseline pane never
        // carry either secret.
        var serialized = JsonSerializer.Serialize(rows);
        serialized.ShouldNotContain("old-secret");
        serialized.ShouldNotContain("new-secret");
        var baselineJson = PlaygroundJson.WriteMaskedSettingsSummary(baseline);
        baselineJson.ShouldNotContain("old-secret");
        baselineJson.ShouldContain("[masked]");
    }

    [Test, Timeout(30_000)]
    public void NestedLeafEditsMatchLegacy(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = BaseModel();
        var baseline = model.DeepClone();
        var session = model.CreateEditSession();
        session.Observable.Nested!.Host = "example";
        session.Observable.Nested.Port = 1234;

        AssertPreviewEqualsLegacy(baseline, model, session.CreateChangeSet());
    }

    [Test, Timeout(30_000)]
    public void NulledNestedRendersMissingEndpoints(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = BaseModel();
        var baseline = model.DeepClone();
        var session = model.CreateEditSession();
        session.Observable.Nested = null;

        var rows = SubmitPreview.Build(session.CreateChangeSet());
        rows.Select(static row => (row.Target, row.Operation, row.Before, row.After))
            .ShouldBe([
                ("Nested.Host", "Edit", "\"localhost\"", MissingText),
                ("Nested.Port", "Edit", "5432", MissingText),
            ]);

        // The legacy handwritten diff rendered a JSON null where the leaf no
        // longer has a value; the typed preview marks the endpoint missing.
        var legacy = LegacyBuild(baseline, model);
        legacy
            .Select(static row => (row.Target, row.Operation, row.Before, row.After))
            .ShouldBe([
                ("Nested.Host", "Edit", "\"localhost\"", "null"),
                ("Nested.Port", "Edit", "5432", "null"),
            ]);
    }

    [Test, Timeout(30_000)]
    public void RestoredNestedRendersMissingEndpoints(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = BaseModel();
        model.Nested = null;
        var baseline = model.DeepClone();
        var session = model.CreateEditSession();
        model.Nested = new PlaygroundNested { Host = "example", Port = 1234 };

        var rows = SubmitPreview.Build(session.CreateChangeSet());
        rows.Select(static row => (row.Target, row.Operation, row.Before, row.After))
            .ShouldBe([
                ("Nested.Host", "Edit", MissingText, "\"example\""),
                ("Nested.Port", "Edit", MissingText, "1234"),
            ]);

        var legacy = LegacyBuild(baseline, model);
        legacy
            .Select(static row => (row.Target, row.Operation, row.Before, row.After))
            .ShouldBe([
                ("Nested.Host", "Edit", "null", "\"example\""),
                ("Nested.Port", "Edit", "null", "1234"),
            ]);
    }

    [Test, Timeout(30_000)]
    public void TaskAddRemoveEditMatchesLegacy(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = BaseModel();
        var baseline = model.DeepClone();
        var session = model.CreateEditSession();
        var editor = session.EditCollection<PlaygroundTask, string>(
            PlaygroundSettings.SparsePath.Tasks
        );
        editor
            .TryAdd(
                new PlaygroundTask
                {
                    Id = "t9",
                    Title = "Nine",
                    Points = 90,
                },
                out _
            )
            .ShouldBeTrue();
        editor.TryRemoveByKey("t3", out _).ShouldBeTrue();
        session.Observable.Tasks.First(static task => task.Id == "t1").Title = "One v2";
        session.Observable.Tasks.First(static task => task.Id == "t2").Points = 99;

        AssertPreviewEqualsLegacy(baseline, model, session.CreateChangeSet());
    }

    [Test, Timeout(30_000)]
    public void TaskRenameMatchesLegacyAsRemovePlusAdd(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = BaseModel();
        var baseline = model.DeepClone();
        var session = model.CreateEditSession();
        session
            .EditCollection<PlaygroundTask, string>(PlaygroundSettings.SparsePath.Tasks)
            .TryReplaceByKey(
                "t1",
                new PlaygroundTask
                {
                    Id = "t1x",
                    Title = "Find the key",
                    Points = 10,
                },
                out _
            )
            .ShouldBeTrue();

        AssertPreviewEqualsLegacy(baseline, model, session.CreateChangeSet());

        // Identity changes never surface as in-place key edits on either side.
        var rows = SubmitPreview.Build(session.CreateChangeSet());
        rows.Any(static row => row.Target.EndsWith(".Id", StringComparison.Ordinal))
            .ShouldBeFalse();
    }

    [Test, Timeout(30_000)]
    public void ReorderOnlyMatchesLegacy(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = BaseModel();
        var baseline = model.DeepClone();
        var session = model.CreateEditSession();
        session
            .EditCollection<PlaygroundTask, string>(PlaygroundSettings.SparsePath.Tasks)
            .TryMoveByKey("t1", 2, out _)
            .ShouldBeTrue();

        AssertPreviewEqualsLegacy(baseline, model, session.CreateChangeSet());
        var rows = SubmitPreview.Build(session.CreateChangeSet());
        var order = rows.ShouldHaveSingleItem();
        order.Target.ShouldBe("Tasks");
        order.Operation.ShouldBe("Order");
        order.Before.ShouldBe("[t1, t2, t3]");
        order.After.ShouldBe("[t2, t3, t1]");
    }

    [Test, Timeout(30_000)]
    public void EditThenRestoreHasNoRows(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = BaseModel();
        var session = model.CreateEditSession();
        session.Observable.Label = "changed";
        session.Observable.Label = "Draft";
        session.HasChanges.ShouldBeFalse();

        SubmitPreview.Build(session.CreateChangeSet()).ShouldBeEmpty();
    }

    [Test, Timeout(30_000)]
    public void PluginsAndTagsGainTypedRows(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = BaseModel();
        var baseline = model.DeepClone();
        var session = model.CreateEditSession();
        session.Observable.Plugins = ["a", "b"];
        session.Observable.Tags.Add("x");

        // The legacy handwritten diff ignored both members; the typed preview
        // covers every supported member without a second comparison branch.
        LegacyBuild(baseline, model).ShouldBeEmpty();
        var rows = SubmitPreview.Build(session.CreateChangeSet());
        rows.Select(static row => (row.Target, row.Operation, row.Before, row.After))
            .ShouldBe([
                ("Plugins", "Edit", "[\"base-plugin\"]", "[\"a\", \"b\"]"),
                ("Tags", "Edit", "[]", "[\"x\"]"),
            ]);
    }

    [Test, Timeout(30_000)]
    public void PreviewSnapshotIgnoresLaterEdits(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = BaseModel();
        var session = model.CreateEditSession();
        session.Observable.Label = "first";

        // RequestSubmit freezes CreateChangeSet before showing the dialog.
        var snapshot = session.CreateChangeSet();
        var frozen = SubmitPreview.Build(snapshot);

        session.Observable.Label = "second";
        session.Observable.RetryCount = 9;

        SubmitPreview.Build(snapshot).ShouldBe(frozen);
        var later = SubmitPreview.Build(session.CreateChangeSet());
        later.ShouldNotBe(frozen);
        later.Count.ShouldBeGreaterThan(frozen.Count);
    }

    [Test, Timeout(30_000)]
    public void BaselineReservationSurvivesRemovalAndSubmission(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = BaseModel();
        var session = model.CreateEditSession();

        // CaptureBaseline keeps the submitted IDs, including removed rows.
        var reserved = model.Tasks.Select(static task => task.Id).ToHashSet(StringComparer.Ordinal);
        session
            .EditCollection<PlaygroundTask, string>(PlaygroundSettings.SparsePath.Tasks)
            .TryRemoveByKey("t2", out _)
            .ShouldBeTrue();
        PlaygroundIds
            .Allocate("t", session.Observable.Tasks.Select(static task => task.Id).Concat(reserved))
            .ShouldBe("t4");

        // Submission advances the baseline: the removed ID is free again.
        session.AcceptChanges();
        reserved = model.Tasks.Select(static task => task.Id).ToHashSet(StringComparer.Ordinal);
        reserved.SetEquals(["t1", "t3"]).ShouldBeTrue();
        PlaygroundIds
            .Allocate("t", session.Observable.Tasks.Select(static task => task.Id).Concat(reserved))
            .ShouldBe("t2");
    }

    [Test, Timeout(30_000)]
    public void PageKeepsOnlySerializedBaselineAndReservedIds(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var playground = typeof(SubmitPreview).Assembly;
        var page = playground.GetType("SparseTypes.Playground.Pages.Case4.EditSessionUndoRedo");
        page.ShouldNotBeNull();

        // The second model baseline is gone; only the serialized pane and the
        // reserved ID set are retained.
        page!
            .GetField("_baselineModel", BindingFlags.NonPublic | BindingFlags.Instance)
            .ShouldBeNull();
        page.GetField("_baselineJson", BindingFlags.NonPublic | BindingFlags.Instance)
            .ShouldNotBeNull();
        page.GetField("_baselineTaskIds", BindingFlags.NonPublic | BindingFlags.Instance)
            .ShouldNotBeNull();

        var editor = playground.GetType("SparseTypes.Playground.Pages.Case4.TaskListEditor");
        editor.ShouldNotBeNull();
        editor!.GetProperty("BaselineTaskIds").ShouldNotBeNull();
        editor.GetProperty("BaselineTasks").ShouldBeNull();

        typeof(SubmitPreview)
            .GetMethod("Build")!
            .GetParameters()
            .Select(static parameter => parameter.ParameterType)
            .ShouldBe([typeof(PlaygroundSettings.ChangeSet)]);
    }

    [Test, Timeout(120_000)]
    public void PreviewSizesComplete(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var count in new[] { 10, 100, 1000 })
        {
            var model = TaskModel(count);
            var baseline = model.DeepClone();
            var session = model.CreateEditSession();
            session.Observable.Tasks[0].Title = "Edited";
            session
                .EditCollection<PlaygroundTask, string>(PlaygroundSettings.SparsePath.Tasks)
                .TryMoveByKey("t1", count - 1, out _)
                .ShouldBeTrue();

            var rows = SubmitPreview.Build(session.CreateChangeSet());
            // One title edit plus the whole-list order row; the per-current-row
            // linear baseline scan is gone.
            rows.Count.ShouldBe(2);
            rows.Any(static row => row.Target == "Tasks[t1].Title" && row.Operation == "Edit")
                .ShouldBeTrue();
            rows.Any(static row => row.Target == "Tasks" && row.Operation == "Order")
                .ShouldBeTrue();

            AssertPreviewEqualsLegacy(baseline, model, session.CreateChangeSet());
        }
    }

    // The pre-SubmitPreview spelling, kept as the parity oracle. It diffs a
    // separately cloned baseline model instead of the session ChangeSet.
    private static List<(string Target, string Operation, string Before, string After)> LegacyBuild(
        PlaygroundSettings baseline,
        PlaygroundSettings current
    )
    {
        var changes = new List<(string Target, string Operation, string Before, string After)>();
        LegacyAdd(
            changes,
            nameof(PlaygroundSettings.Enabled),
            "Edit",
            baseline.Enabled,
            current.Enabled
        );
        LegacyAdd(
            changes,
            nameof(PlaygroundSettings.RetryCount),
            "Edit",
            baseline.RetryCount,
            current.RetryCount
        );
        LegacyAdd(changes, nameof(PlaygroundSettings.Label), "Edit", baseline.Label, current.Label);
        LegacyAdd(
            changes,
            nameof(PlaygroundSettings.Password),
            "Edit",
            baseline.Password,
            current.Password
        );
        LegacyAdd(
            changes,
            $"{nameof(PlaygroundSettings.Nested)}.{nameof(PlaygroundNested.Host)}",
            "Edit",
            baseline.Nested?.Host,
            current.Nested?.Host
        );
        LegacyAdd(
            changes,
            $"{nameof(PlaygroundSettings.Nested)}.{nameof(PlaygroundNested.Port)}",
            "Edit",
            baseline.Nested?.Port,
            current.Nested?.Port
        );

        var present = new HashSet<PlaygroundTask>();
        foreach (var task in current.Tasks)
        {
            var match = baseline.Tasks.FirstOrDefault(item => item.Id == task.Id);
            if (match is null)
            {
                changes.Add(($"Tasks[{task.Id}]", "Add", MissingText, LegacyFormatTask(task)));
                continue;
            }

            present.Add(match);
            var target = $"Tasks[{match.Id}]";
            LegacyAdd(changes, $"{target}.Id", "Edit", match.Id, task.Id);
            LegacyAdd(changes, $"{target}.Title", "Edit", match.Title, task.Title);
            LegacyAdd(changes, $"{target}.Points", "Edit", match.Points, task.Points);
        }

        foreach (var task in baseline.Tasks)
        {
            if (!present.Contains(task))
            {
                changes.Add(($"Tasks[{task.Id}]", "Remove", LegacyFormatTask(task), MissingText));
            }
        }

        var beforeOrder = baseline.Tasks.Select(static task => task.Id).ToArray();
        var afterOrder = current.Tasks.Select(static task => task.Id).ToArray();
        if (!beforeOrder.SequenceEqual(afterOrder, StringComparer.Ordinal))
        {
            changes.Add(
                ("Tasks", "Order", LegacyFormatOrder(beforeOrder), LegacyFormatOrder(afterOrder))
            );
        }

        return changes.OrderBy(static change => LegacyOperationOrder(change.Operation)).ToList();
    }

    private static int LegacyOperationOrder(string operation) =>
        operation switch
        {
            "Add" => 0,
            "Remove" => 1,
            "Edit" => 2,
            "Order" => 3,
            _ => 4,
        };

    private static void LegacyAdd<T>(
        List<(string Target, string Operation, string Before, string After)> changes,
        string target,
        string operation,
        T before,
        T after
    )
    {
        if (!EqualityComparer<T>.Default.Equals(before, after))
        {
            changes.Add(
                (
                    target,
                    operation,
                    PlaygroundJson.MaskPasswordSummaryValue(
                        target,
                        JsonSerializer.Serialize(before)
                    ),
                    PlaygroundJson.MaskPasswordSummaryValue(target, JsonSerializer.Serialize(after))
                )
            );
        }
    }

    private static string LegacyFormatTask(PlaygroundTask task) =>
        $"{{ Title = {JsonSerializer.Serialize(task.Title)}, Points = {task.Points} }}";

    private static string LegacyFormatOrder(IEnumerable<string> ids) =>
        $"[{string.Join(", ", ids)}]";
}
