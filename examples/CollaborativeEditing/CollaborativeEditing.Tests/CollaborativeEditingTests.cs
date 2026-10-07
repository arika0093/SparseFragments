using System.Net;
using System.Text;
using CollaborativeEditing.Contracts;
using Shouldly;
using SparseFragments;
using TUnit.Core;

namespace CollaborativeEditing.Tests;

public sealed class CollaborativeEditingTests
{
    private static Optional<Workspace.Fragment?> Present(Workspace.Fragment fragment) =>
        Optional<Workspace.Fragment?>.Present(fragment);

    private static Workspace.Fragment Frag(Workspace model) => Workspace.Fragment.From(model);

    private static Workspace.Patch Diff(Workspace before, Workspace after)
    {
        var beforeFrag = Frag(before);
        var afterFrag = Frag(after);
        return Workspace.Patch.Between(Present(beforeFrag), Present(afterFrag));
    }

    [Test]
    public async Task SeedMapping_GetReturnsQuestsAndEffectiveSettings()
    {
        using var factory = new CollabFactory();
        using var client = factory.CreateSeededClient();

        var snapshot = await CollabHttp.GetSnapshotAsync(client, factory.SeedId);

        snapshot.Name.ShouldBe("Demo");
        snapshot.Quests.Count.ShouldBe(3);
        snapshot.Quests.Select(q => q.Id).ToList().ShouldBe(new List<string> { "a", "b", "c" });
        snapshot.Quests.Single(q => q.Id == "a").Title.ShouldBe("First quest");
        snapshot.Settings.Theme.ShouldBe("dark");
        snapshot.Settings.RetryCount.ShouldBe(5);
        snapshot.Settings.Notifications.ShouldBeFalse();
        // Effective layers system defaults under the override: seeded override wins.
        snapshot.EffectiveSettings.Theme.ShouldBe("dark");
        snapshot.EffectiveSettings.RetryCount.ShouldBe(5);
        snapshot.EffectiveSettings.Notifications.ShouldBeFalse();
        snapshot.Revision.ShouldBe(1);
    }

    [Test]
    public async Task JsonPatch_ImportApplyPersist_RoundTrip()
    {
        using var factory = new CollabFactory();
        using var client = factory.CreateSeededClient();
        var seed = await CollabHttp.GetSnapshotAsync(client, factory.SeedId);
        var baseModel = seed.ToWorkspace();

        var after = seed.ToWorkspace();
        after.Name = "Demo — renamed";
        after.Settings.Theme = "light";

        var patch = Diff(baseModel, after);
        patch.IsEmpty.ShouldBeFalse();

        // Export to RFC 6902 and reimport relative to the same baseline.
        var baseOpt = Present(Frag(baseModel));
        var exported = patch.ToJsonPatch(baseOpt);
        exported.Length.ShouldBeGreaterThan(0);
        var reimported = Workspace.Patch.FromJsonPatch(baseOpt, exported);
        Workspace
            .Patch.Between(reimported.Apply(baseOpt), Present(Frag(after)))
            .IsEmpty.ShouldBeTrue();

        // Persist through the server and verify the stored state.
        using var patchResponse = await CollabHttp.PatchAsync(
            client,
            factory.SeedId,
            seed.Revision,
            exported
        );
        patchResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = await CollabHttp.ReadSnapshotAsync(patchResponse);
        saved.ShouldNotBeNull();
        saved!.Name.ShouldBe("Demo — renamed");
        saved.Settings.Theme.ShouldBe("light");

        var reread = await CollabHttp.GetSnapshotAsync(client, factory.SeedId);
        reread.Name.ShouldBe("Demo — renamed");
        reread.Settings.Theme.ShouldBe("light");
        reread.Revision.ShouldBe(seed.Revision + 1);
    }

    [Test]
    public async Task Revision_Increments_OnSuccessfulPatch_AndEtagMatches()
    {
        using var factory = new CollabFactory();
        using var client = factory.CreateSeededClient();
        var seed = await CollabHttp.GetSnapshotAsync(client, factory.SeedId);

        var after = seed.ToWorkspace();
        after.Settings.RetryCount = seed.Settings.RetryCount + 1;
        var patch = Diff(seed.ToWorkspace(), after);
        var exported = patch.ToJsonPatch(Present(Frag(seed.ToWorkspace())));

        using var response = await CollabHttp.PatchAsync(
            client,
            factory.SeedId,
            seed.Revision,
            exported
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag.ShouldNotBeNull();
        response.Headers.ETag!.Tag.ShouldBe($"\"{seed.Revision + 1}\"");

        var reread = await CollabHttp.GetSnapshotAsync(client, factory.SeedId);
        reread.Revision.ShouldBe(seed.Revision + 1);
        reread.Settings.RetryCount.ShouldBe(seed.Settings.RetryCount + 1);
    }

    [Test]
    public async Task StaleRevision_Returns412_WithLatestState()
    {
        using var factory = new CollabFactory();
        using var client = factory.CreateSeededClient();
        var seed = await CollabHttp.GetSnapshotAsync(client, factory.SeedId);

        // Advance the server once so the seed revision goes stale.
        var advance = seed.ToWorkspace();
        advance.Name = "Advanced";
        var advancePatch = Diff(seed.ToWorkspace(), advance);
        var advanceJson = advancePatch.ToJsonPatch(Present(Frag(seed.ToWorkspace())));
        using (
            var advanceResponse = await CollabHttp.PatchAsync(
                client,
                factory.SeedId,
                seed.Revision,
                advanceJson
            )
        )
        {
            advanceResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // Retry with the now-stale revision.
        var stale = seed.ToWorkspace();
        stale.Name = "Stale attempt";
        var stalePatch = Diff(seed.ToWorkspace(), stale);
        var staleJson = stalePatch.ToJsonPatch(Present(Frag(seed.ToWorkspace())));
        using var staleResponse = await CollabHttp.PatchAsync(
            client,
            factory.SeedId,
            seed.Revision,
            staleJson
        );
        staleResponse.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        staleResponse.Headers.ETag.ShouldNotBeNull();
        staleResponse.Headers.ETag!.Tag.ShouldBe($"\"{seed.Revision + 1}\"");

        var latest = await CollabHttp.ReadSnapshotAsync(staleResponse);
        latest.ShouldNotBeNull();
        latest!.Revision.ShouldBe(seed.Revision + 1);
        latest.Name.ShouldBe("Advanced");
    }

    [Test]
    public async Task DisjointEdits_RebaseAndRetry_Simulation_TwoClients()
    {
        using var factory = new CollabFactory();
        using var clientA = factory.CreateSeededClient();
        using var clientB = factory.CreateSeededClient();

        var baseA = await CollabHttp.GetSnapshotAsync(clientA, factory.SeedId);
        var baseB = await CollabHttp.GetSnapshotAsync(clientB, factory.SeedId);
        baseA.Revision.ShouldBe(baseB.Revision);
        var baseModel = baseA.ToWorkspace();

        // Client B renames (member: Name).
        var modelB = baseB.ToWorkspace();
        modelB.Name = "B renamed";
        var patchB = Diff(baseModel, modelB);
        var jsonB = patchB.ToJsonPatch(Present(Frag(baseModel)));
        using (
            var respB = await CollabHttp.PatchAsync(clientB, factory.SeedId, baseB.Revision, jsonB)
        )
        {
            respB.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // Client A edits a disjoint member (Settings.Theme) from the same baseline.
        var modelA = baseA.ToWorkspace();
        modelA.Settings.Theme = "solarized";
        var localA = Diff(baseModel, modelA);

        var staleJsonA = localA.ToJsonPatch(Present(Frag(baseModel)));
        using var staleResp = await CollabHttp.PatchAsync(
            clientA,
            factory.SeedId,
            baseA.Revision,
            staleJsonA
        );
        staleResp.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        var latest = await CollabHttp.ReadSnapshotAsync(staleResp);
        latest.ShouldNotBeNull();

        // Rebase A's local patch onto B's current state: no conflicts expected.
        var baseOpt = Present(Frag(baseModel));
        var currentOpt = Present(Frag(latest!.ToWorkspace()));
        var rebased = Workspace.Patch.Rebase(baseOpt, localA, currentOpt);
        rebased.HasConflicts.ShouldBeFalse();

        var retryJson = rebased.Patch.ToJsonPatch(currentOpt);
        using var retry = await CollabHttp.PatchAsync(
            clientA,
            factory.SeedId,
            latest.Revision,
            retryJson
        );
        retry.StatusCode.ShouldBe(HttpStatusCode.OK);

        var final = await CollabHttp.GetSnapshotAsync(clientA, factory.SeedId);
        final.Name.ShouldBe("B renamed");
        final.Settings.Theme.ShouldBe("solarized");
        final.Revision.ShouldBe(baseA.Revision + 2);
    }

    [Test]
    public async Task SameMember_Conflict_Surfaces_StructuredConflict()
    {
        using var factory = new CollabFactory();
        using var client = factory.CreateSeededClient();
        var seed = await CollabHttp.GetSnapshotAsync(client, factory.SeedId);
        var baseModel = seed.ToWorkspace();

        // Server moves Name to "server value".
        var serverModel = seed.ToWorkspace();
        serverModel.Name = "server value";
        var serverPatch = Diff(baseModel, serverModel);
        using (
            var serverResp = await CollabHttp.PatchAsync(
                client,
                factory.SeedId,
                seed.Revision,
                serverPatch.ToJsonPatch(Present(Frag(baseModel)))
            )
        )
        {
            serverResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var latest = await CollabHttp.GetSnapshotAsync(client, factory.SeedId);

        // Local moves the same member elsewhere: must conflict.
        var localModel = seed.ToWorkspace();
        localModel.Name = "local value";
        var localPatch = Diff(baseModel, localModel);

        var rebase = Workspace.Patch.Rebase(
            Present(Frag(baseModel)),
            localPatch,
            Present(Frag(latest.ToWorkspace()))
        );
        rebase.HasConflicts.ShouldBeTrue();
        var conflict = rebase.Conflicts.Single(c => c.PathText == "Name");
        conflict.Path.ShouldBe(new List<string> { "Name" });
        conflict.Kind.ShouldBe(SparsePatchConflictKind.Scalar);
        conflict.BaseValue.Value.ShouldBe(baseModel.Name);
        conflict.LocalValue.Value.ShouldBe("local value");
        conflict.CurrentValue.Value.ShouldBe("server value");

        // The stale send itself is a 412 carrying the latest state.
        var staleJson = localPatch.ToJsonPatch(Present(Frag(baseModel)));
        using var staleResp = await CollabHttp.PatchAsync(
            client,
            factory.SeedId,
            seed.Revision,
            staleJson
        );
        staleResp.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
    }

    [Test]
    public async Task AlreadyApplied_Patch_BecomesNoOp()
    {
        using var factory = new CollabFactory();
        using var client = factory.CreateSeededClient();
        var seed = await CollabHttp.GetSnapshotAsync(client, factory.SeedId);
        var baseModel = seed.ToWorkspace();

        var after = seed.ToWorkspace();
        after.Settings.RetryCount = 42;
        var patch = Diff(baseModel, after);
        patch.IsEmpty.ShouldBeFalse();

        var applied = patch.Apply(Present(Frag(baseModel)));
        Diff(after, applied.Value!.ToModel()).IsEmpty.ShouldBeTrue();
        Workspace.Patch.Between(applied, applied).IsEmpty.ShouldBeTrue();

        // Persist once, then confirm the stored state already contains the edit:
        // diffing stored against desired is empty.
        var json = patch.ToJsonPatch(Present(Frag(baseModel)));
        using (var resp = await CollabHttp.PatchAsync(client, factory.SeedId, seed.Revision, json))
        {
            resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var stored = await CollabHttp.GetSnapshotAsync(client, factory.SeedId);
        Diff(stored.ToWorkspace(), after).IsEmpty.ShouldBeTrue();

        // Rebase of the same local patch onto the new current state is a semantic no-op.
        var rebase = Workspace.Patch.Rebase(
            Present(Frag(baseModel)),
            patch,
            Present(Frag(stored.ToWorkspace()))
        );
        rebase.HasConflicts.ShouldBeFalse();
        rebase
            .Patch.Apply(Present(Frag(stored.ToWorkspace())))
            .Value!.ToModel()
            .Settings.RetryCount.ShouldBe(42);
    }

    [Test]
    public async Task Keyed_AddRemoveEditReorder_RoundTrip()
    {
        // Granular keyed patch: add d, remove b, edit a/c, reorder to c,a,d.
        var baseModel = new Workspace
        {
            Name = "Demo",
            Settings = new WorkspaceSettings
            {
                Theme = "dark",
                RetryCount = 5,
                Notifications = false,
            },
            Quests = new List<Quest>
            {
                new()
                {
                    Id = "a",
                    Title = "First quest",
                    Points = 10,
                    Scores = new List<int> { 1, 2 },
                },
                new()
                {
                    Id = "b",
                    Title = "Second quest",
                    Points = 20,
                    Scores = new List<int> { 3 },
                },
                new()
                {
                    Id = "c",
                    Title = "Third quest",
                    Points = 30,
                    Scores = new List<int>(),
                },
            },
        };

        var patch = new Workspace.Patch();
        patch.Quests.Add(
            new Quest
            {
                Id = "d",
                Title = "Fourth quest",
                Points = 40,
                Scores = new List<int> { 7 },
            }
        );
        patch.Quests.Remove("b");
        patch.Quests.Edit("a").Title = "First quest (edited)";
        patch.Quests.Edit("a").Points = 11;
        patch.Quests.Edit("c").Scores = new List<int> { 9 };
        patch.Quests.SetOrder(new[] { "c", "a", "d" });
        patch.IsEmpty.ShouldBeFalse();

        var baseOpt = Present(Frag(baseModel));
        var applied = patch.Apply(baseOpt);
        var appliedModel = applied.Value!.ToModel();
        appliedModel.Quests.Select(q => q.Id).ToList().ShouldBe(new List<string> { "c", "a", "d" });
        appliedModel.Quests.Single(q => q.Id == "a").Title.ShouldBe("First quest (edited)");
        appliedModel.Quests.Single(q => q.Id == "a").Points.ShouldBe(11);
        appliedModel.Quests.Single(q => q.Id == "c").Scores.ShouldBe(new List<int> { 9 });
        appliedModel.Quests.Any(q => q.Id == "b").ShouldBeFalse();

        // JSON Patch export/import preserves the keyed semantics.
        var exported = patch.ToJsonPatch(baseOpt);
        exported.Length.ShouldBeGreaterThan(0);
        var reimported = Workspace.Patch.FromJsonPatch(baseOpt, exported);
        Workspace.Patch.Between(reimported.Apply(baseOpt), applied).IsEmpty.ShouldBeTrue();

        // Same-state rebase (server unchanged) preserves the local patch.
        var same = Workspace.Patch.Rebase(baseOpt, patch, baseOpt);
        same.HasConflicts.ShouldBeFalse();
        same.Patch.Apply(baseOpt)
            .Value!.ToModel()
            .Quests.Select(q => q.Id)
            .ToList()
            .ShouldBe(new List<string> { "c", "a", "d" });

        // Cross-instance rebase: per-key base/current comparison uses element
        // instance equality for POCO elements, so a touched key present on both
        // sides surfaces a Nested conflict under Quests (the WPF client lists
        // these Path/PathText/kind/values entries instead of auto-retrying).
        // A concurrent server-side retitle of "a" must therefore conflict.
        var serverModel = new Workspace
        {
            Name = "Demo",
            Settings = new WorkspaceSettings
            {
                Theme = "dark",
                RetryCount = 5,
                Notifications = false,
            },
            Quests = baseModel
                .Quests.Select(q => new Quest
                {
                    Id = q.Id,
                    Title = q.Id == "a" ? "Server title" : q.Title,
                    Points = q.Points,
                    Scores = new List<int>(q.Scores),
                })
                .ToList(),
        };
        var conflicted = Workspace.Patch.Rebase(baseOpt, patch, Present(Frag(serverModel)));
        conflicted.HasConflicts.ShouldBeTrue();
        conflicted
            .Conflicts.Any(c => c.Path.Count >= 2 && c.Path[0] == "Quests" && c.Path[1] == "a")
            .ShouldBeTrue();

        // Duplicate quest keys cannot even be diffed (server would 400 before persistence).
        var dupModel = new Workspace
        {
            Name = "Demo",
            Settings = new WorkspaceSettings
            {
                Theme = "dark",
                RetryCount = 5,
                Notifications = false,
            },
            Quests = new List<Quest>
            {
                new()
                {
                    Id = "a",
                    Title = "One",
                    Points = 1,
                    Scores = new List<int>(),
                },
                new()
                {
                    Id = "a",
                    Title = "Two",
                    Points = 2,
                    Scores = new List<int>(),
                },
            },
        };
        Should.Throw<InvalidOperationException>(() => Diff(baseModel, dupModel));

        // HTTP persist round trip for the keyed patch against the seed workspace.
        using var factory = new CollabFactory();
        using var client = factory.CreateSeededClient();
        var seed = await CollabHttp.GetSnapshotAsync(client, factory.SeedId);
        var seedPatch = Diff(seed.ToWorkspace(), appliedModel);
        seedPatch.IsEmpty.ShouldBeFalse();
        using var response = await CollabHttp.PatchAsync(
            client,
            factory.SeedId,
            seed.Revision,
            seedPatch.ToJsonPatch(Present(Frag(seed.ToWorkspace())))
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stored = await CollabHttp.GetSnapshotAsync(client, factory.SeedId);
        stored.Revision.ShouldBe(seed.Revision + 1);
        var storedModel = stored.ToWorkspace();
        storedModel.Quests.Select(q => q.Id).ToList().ShouldBe(new List<string> { "c", "a", "d" });
        storedModel.Quests.Single(q => q.Id == "a").Title.ShouldBe("First quest (edited)");
        storedModel.Quests.Single(q => q.Id == "c").Scores.ShouldBe(new List<int> { 9 });
        storedModel.Quests.Any(q => q.Id == "b").ShouldBeFalse();
    }

    [Test]
    public async Task ScalarScores_WholeValue_RoundTrip()
    {
        // Quest.Scores is a scalar sequence: whole-value replace semantics.
        var before = new Workspace
        {
            Name = "Demo",
            Settings = new WorkspaceSettings
            {
                Theme = "dark",
                RetryCount = 5,
                Notifications = false,
            },
            Quests = new List<Quest>
            {
                new()
                {
                    Id = "a",
                    Title = "First quest",
                    Points = 10,
                    Scores = new List<int> { 1, 2 },
                },
            },
        };
        var after = new Workspace
        {
            Name = "Demo",
            Settings = new WorkspaceSettings
            {
                Theme = "dark",
                RetryCount = 5,
                Notifications = false,
            },
            Quests = new List<Quest>
            {
                new()
                {
                    Id = "a",
                    Title = "First quest",
                    Points = 10,
                    Scores = new List<int> { 1, 2, 3 },
                },
            },
        };

        var patch = Diff(before, after);
        patch.IsEmpty.ShouldBeFalse();
        var baseOpt = Present(Frag(before));
        var exported = patch.ToJsonPatch(baseOpt);
        var reimported = Workspace.Patch.FromJsonPatch(baseOpt, exported);
        var applied = reimported.Apply(baseOpt).Value!.ToModel();
        applied.Quests.Single().Scores.ShouldBe(new List<int> { 1, 2, 3 });
    }

    [Test]
    public async Task InvalidModel_Returns400_BeforePersistence()
    {
        using var factory = new CollabFactory();
        using var client = factory.CreateSeededClient();
        var seed = await CollabHttp.GetSnapshotAsync(client, factory.SeedId);

        // Empty Name fails WorkspaceMapper.Validate.
        var invalid = seed.ToWorkspace();
        invalid.Name = "   ";
        var patch = Diff(seed.ToWorkspace(), invalid);
        var exported = patch.ToJsonPatch(Present(Frag(seed.ToWorkspace())));

        using var response = await CollabHttp.PatchAsync(
            client,
            factory.SeedId,
            seed.Revision,
            exported
        );
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("Name");

        // Nothing was persisted: revision and content are unchanged.
        var reread = await CollabHttp.GetSnapshotAsync(client, factory.SeedId);
        reread.Revision.ShouldBe(seed.Revision);
        reread.Name.ShouldBe(seed.Name);

        // Unknown JSON Patch paths are also 400 (no persistence).
        var badJson = Encoding.UTF8.GetBytes(
            """[{"op":"replace","path":"/NoSuchMember","value":1}]"""
        );
        using var badResponse = await CollabHttp.PatchAsync(
            client,
            factory.SeedId,
            seed.Revision,
            badJson
        );
        badResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CollabHttp.GetSnapshotAsync(client, factory.SeedId)).Revision.ShouldBe(
            seed.Revision
        );
    }

    [Test]
    public async Task Get_MissingWorkspace_Returns404()
    {
        using var factory = new CollabFactory();
        using var client = factory.CreateSeededClient();
        using var response = await client.GetAsync($"/api/workspaces/{Guid.NewGuid()}");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
