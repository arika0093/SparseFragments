using System.Text.Json;
using SparseTypes.Playground.Models;

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
