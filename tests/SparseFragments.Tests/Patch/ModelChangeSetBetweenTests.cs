using System.Text.Json;

namespace SparseFragments.Tests.Patch;

public sealed class ModelChangeSetBetweenTests
{
    [Test]
    [Arguments(null, "changed")]
    [Arguments("before", null)]
    [Arguments("same", "same")]
    public void ScalarModelDiffMatchesProjectedDiff(string? beforeName, string? afterName)
    {
        var before = new RedactedAccount { DisplayName = beforeName, Password = "secret" };
        var after = new RedactedAccount { DisplayName = afterName, Password = "secret" };
        var direct = RedactedAccount.ChangeSet.Between(before, after);
        var projected = RedactedAccount.ChangeSet.Between(
            Optional<RedactedAccount.Fragment?>.Present(RedactedAccount.Fragment.From(before)),
            Optional<RedactedAccount.Fragment?>.Present(RedactedAccount.Fragment.From(after))
        );
        JsonSerializer
            .Serialize(direct.ToPayload())
            .ShouldBe(JsonSerializer.Serialize(projected.ToPayload()));
        direct.ToPatch().ApplyTo(before).DisplayName.ShouldBe(afterName);
        direct.Invert().ToPatch().ApplyTo(after).DisplayName.ShouldBe(beforeName);
    }

    [Test]
    public void ScalarValueModelDiffPreservesEndpoints()
    {
        var before = new PayloadValueItem { Id = 1, Name = "before" };
        var after = new PayloadValueItem { Id = 1, Name = "after" };
        var change = PayloadValueItem.ChangeSet.Between(before, after);
        change.ToPatch().ApplyTo(before).ShouldBe(after);
        change.Invert().ToPatch().ApplyTo(after).ShouldBe(before);
        PayloadValueItem.ChangeSet.Between(before, before).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void NullModelInputsRetainProjectionValidation()
    {
        var model = new RedactedAccount();
        Should
            .Throw<ArgumentNullException>(() => RedactedAccount.ChangeSet.Between(null!, model))
            .ParamName.ShouldBe("value");
        Should
            .Throw<ArgumentNullException>(() => RedactedAccount.ChangeSet.Between(model, null!))
            .ParamName.ShouldBe("value");
    }
}
