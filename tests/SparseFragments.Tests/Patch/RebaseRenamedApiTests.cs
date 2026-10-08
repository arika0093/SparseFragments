using System.Text.Json;
using SparseFragments;

namespace SparseFragments.Tests;

// Issue #113: model-oriented coverage for the renamed rebase surface
// (RebaseResult.Rebased, SparseConflict/SparseConflictKind).
public sealed class RebaseRenamedApiTests
{
    [Test]
    public void CleanRebasedIsRelativeToCurrent()
    {
        var baseModel = new Settings { Label = "a", RetryCount = 1 };
        var editedModel = new Settings { Label = "a", RetryCount = 2 };
        var currentModel = new Settings { Label = "b", RetryCount = 1 };

        var changes = baseModel.CreateChangeSet(editedModel);
        var rebased = changes.RebaseOnto(currentModel);

        rebased.HasConflicts.ShouldBeFalse();
        rebased.Conflicts.ShouldBeEmpty();
        rebased.Rebased.IsEmpty.ShouldBeFalse();
        var ok = rebased.Rebased.TryApplyTo(currentModel, out var applied);
        ok.ShouldBeTrue();
        applied.ShouldNotBeNull();
        applied!.Label.ShouldBe("b");
        applied.RetryCount.ShouldBe(2);
    }

    [Test]
    public void NestedConflictViaOrdinaryModelsReportsPath()
    {
        var baseModel = new Settings
        {
            Nested = new Nested { Host = "a", Port = 1 },
        };
        var editedModel = new Settings
        {
            Nested = new Nested { Host = "b", Port = 1 },
        };
        var currentModel = new Settings
        {
            Nested = new Nested { Host = "c", Port = 1 },
        };

        var rebased = baseModel.CreateChangeSet(editedModel).RebaseOnto(currentModel);

        rebased.HasConflicts.ShouldBeTrue();
        var conflict = rebased.Conflicts.Single();
        conflict.Path.ShouldBe(["Nested", "Host"]);
        conflict.Kind.ShouldBe(SparseConflictKind.Scalar);
        conflict.BaseValue.IsPresent.ShouldBeTrue();
        conflict.LocalValue.IsPresent.ShouldBeTrue();
        conflict.CurrentValue.IsPresent.ShouldBeTrue();
    }

    [Test]
    public void KeyedConflictViaOrdinaryModelsKeepsDisjointKeys()
    {
        static KeyedServerHolder Holder(params KeyedServer[] items) =>
            new() { Items = items.ToList() };

        var baseModel = Holder(
            new KeyedServer
            {
                Id = "a",
                Name = "x",
                Count = 1,
            }
        );
        var editedModel = Holder(
            new KeyedServer
            {
                Id = "a",
                Name = "y",
                Count = 1,
            }
        );
        var currentModel = Holder(
            new KeyedServer
            {
                Id = "a",
                Name = "z",
                Count = 1,
            }
        );

        var rebased = baseModel.CreateChangeSet(editedModel).RebaseOnto(currentModel);

        rebased.HasConflicts.ShouldBeTrue();
        rebased.Conflicts.Single().Path[0].ShouldBe("Items");
        rebased.Rebased.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void FailedTryApplyToDoesNotMutateInput()
    {
        var baseModel = new Settings { Label = "a", RetryCount = 1 };
        var editedModel = new Settings { Label = "b", RetryCount = 2 };
        var currentModel = new Settings { Label = "c", RetryCount = 3 };
        var before = new Settings
        {
            Label = currentModel.Label,
            RetryCount = currentModel.RetryCount,
        };

        var changes = baseModel.CreateChangeSet(editedModel);
        var ok = changes.TryApplyTo(currentModel, out var updated, out var conflicts);

        ok.ShouldBeFalse();
        updated.ShouldBeNull();
        conflicts.ShouldNotBeNull();
        conflicts!.Count.ShouldBeGreaterThan(0);
        currentModel.Label.ShouldBe(before.Label);
        currentModel.RetryCount.ShouldBe(before.RetryCount);
    }

    [Test]
    public void NullableAndCustomStrategyRebase()
    {
        var baseModel = new Settings { Label = "a", RetryCount = 1 };
        var editedModel = new Settings { Label = null, RetryCount = 1 };
        var currentModel = new Settings { Label = "c", RetryCount = 1 };

        var nullable = baseModel.CreateChangeSet(editedModel).RebaseOnto(currentModel);
        nullable.HasConflicts.ShouldBeTrue();
        nullable.Conflicts.Single().Kind.ShouldBe(SparseConflictKind.Scalar);

        var strategyBase = new StrategySettings { Values = [1, 2] };
        var strategyEdited = new StrategySettings { Values = [1, 3] };
        var strategyCurrent = new StrategySettings { Values = [1, 4] };
        var custom = strategyBase.CreateChangeSet(strategyEdited).RebaseOnto(strategyCurrent);
        custom.HasConflicts.ShouldBeTrue();
        custom.Conflicts.Single().Kind.ShouldBe(SparseConflictKind.CustomStrategy);
    }

    [Test]
    public void CompoundNestedKeyedConflictReportsFullPath()
    {
        static ClusterHolder Holder(string serverName) =>
            new()
            {
                Groups =
                [
                    new ServerGroup
                    {
                        Name = "g",
                        Servers =
                        [
                            new KeyedServer
                            {
                                Id = "a",
                                Name = serverName,
                                Count = 1,
                            },
                        ],
                    },
                ],
            };

        var rebased = Holder("x").CreateChangeSet(Holder("y")).RebaseOnto(Holder("z"));

        rebased.HasConflicts.ShouldBeTrue();
        var conflict = rebased.Conflicts.Single();
        conflict.Path[0].ShouldBe("Groups");
        conflict.PathText.ShouldContain("Servers");
    }

    [Test]
    public void PayloadRoundTripRebaseApplyEndToEnd()
    {
        var stateA = new Settings { Label = "a", RetryCount = 1 };
        var stateB = new Settings { Label = "a", RetryCount = 2 };
        var stateC = new Settings { Label = "b", RetryCount = 1 };

        var outgoing = stateA.CreateChangeSet(stateB);
        var json = JsonSerializer.Serialize(outgoing.ToPayload());
        var incoming = JsonSerializer.Deserialize<Settings.ChangePayload>(json)!.ToChangeSet();

        var ok = incoming.TryApplyTo(stateC, out var saved, out var conflicts);

        ok.ShouldBeTrue();
        conflicts.ShouldBeNull();
        saved.ShouldNotBeNull();
        saved!.Label.ShouldBe("b");
        saved.RetryCount.ShouldBe(2);
    }

    [Test]
    public void RebaseResultExposesRebasedNotPatch()
    {
        var resultType = typeof(RebaseResult<Settings.ChangeSet>);
        resultType.GetProperty("Rebased").ShouldNotBeNull();
        resultType.GetProperty("Patch").ShouldBeNull();
        resultType
            .GetProperty("Conflicts")!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<SparseConflict>));
        typeof(SparseConflict)
            .GetProperty("Kind")!
            .PropertyType.ShouldBe(typeof(SparseConflictKind));
    }
}
