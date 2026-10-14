using System.Text.Json;
using SparseTypes.Playground.Models;
using SparseTypes.Playground.Pages.Case4;

namespace SparseTypes.Tests.Patch;

public sealed class PlaygroundCase4Tests
{
    [Test]
    public void DeepCloneKeepsCase4BaselineIndependent()
    {
        var model = new PlaygroundSettings
        {
            Password = "synthetic-before-password",
            Nested = new PlaygroundNested { Host = "before-host", Port = 5432 },
            Tasks =
            [
                new PlaygroundTask
                {
                    Id = "t1",
                    Title = "before-task",
                    Points = 10,
                },
            ],
        };
        var baseline = model.DeepClone();

        baseline.Nested!.Host = "after-host";
        baseline.Tasks[0].Title = "after-task";
        baseline.Tasks.Add(new PlaygroundTask { Id = "t2", Title = "new-task" });

        model.Nested!.Host.ShouldBe("before-host");
        model.Tasks.ShouldHaveSingleItem();
        model.Tasks[0].Title.ShouldBe("before-task");
    }

    [Test]
    public void BatchPresetHistoryIsAtomicAndPayloadRedactsOnlyBeforePassword()
    {
        var model = new PlaygroundSettings
        {
            Enabled = true,
            RetryCount = 3,
            Label = "Draft",
            Password = "synthetic-before-password",
        };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory();

        session.BatchEdit(() =>
        {
            session.Observable.Enabled = false;
            session.Observable.RetryCount = 8;
            session.Observable.Label = "Batch preset";
            session.Observable.Password = "synthetic-after-password";
        });

        history.UndoCount.ShouldBe(1);
        var payloadJson = PlaygroundJson.WriteSettingsChangeSet(session.CreateChangeSet());
        using var payloadDocument = JsonDocument.Parse(payloadJson);
        var passwordChange = payloadDocument
            .RootElement.GetProperty("changes")
            .EnumerateArray()
            .Single(change => change.GetProperty("member").GetString() == "Password");
        passwordChange.GetProperty("before").GetProperty("state").GetString().ShouldBe("redacted");
        payloadJson.ShouldNotContain("synthetic-before-password");
        payloadJson.ShouldContain("synthetic-after-password");

        history.Undo();
        model.Enabled.ShouldBeTrue();
        model.RetryCount.ShouldBe(3);
        model.Label.ShouldBe("Draft");
        model.Password.ShouldBe("synthetic-before-password");

        history.Redo();
        model.Enabled.ShouldBeFalse();
        model.RetryCount.ShouldBe(8);
        model.Label.ShouldBe("Batch preset");
        model.Password.ShouldBe("synthetic-after-password");
    }

    [Test]
    public void PasswordChangesAreExcludedFromPlaygroundUndoAndRedo()
    {
        var model = new PlaygroundSettings { Label = "Draft" };
        var session = model.CreateEditSession();
        using var history = PlaygroundPasswordHistory.Create(session, model);

        session.Observable.Password = "first-secret";
        history.CanUndo.ShouldBeFalse();

        session.BatchEdit(() =>
        {
            session.Observable.Label = "Updated";
            session.Observable.Password = "second-secret";
        });

        history.Undo();
        model.Label.ShouldBe("Draft");
        model.Password.ShouldBe("second-secret");

        history.Redo();
        model.Label.ShouldBe("Updated");
        model.Password.ShouldBe("second-secret");
    }

    [Test]
    public void Case4PresentationMasksPasswordWithoutMutatingTheModel()
    {
        var model = new PlaygroundSettings { Password = "synthetic-password" };

        var json = PlaygroundJson.WriteMaskedSettingsSummary(model);

        json.ShouldContain("[masked]");
        json.ShouldNotContain("synthetic-password");
        model.Password.ShouldBe("synthetic-password");
        PlaygroundJson
            .MaskPasswordSummaryValue("Password", "\"synthetic-password\"")
            .ShouldBe("[masked]");
        PlaygroundJson.MaskPasswordSummaryValue("RetryCount", "3").ShouldBe("3");
    }
}
