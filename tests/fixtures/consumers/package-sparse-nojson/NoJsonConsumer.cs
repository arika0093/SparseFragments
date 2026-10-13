using System;
using System.Collections.Generic;
using SparseFragments;

// Serializer-free consumer (issues #242/#243): a netstandard2.0 class library
// with no System.Text.Json reference. Build-only (netstandard2.0 has no
// runnable host), so compilation success is the gate: it fails if generated
// code references any System.Text.Json API, because the netstandard2.0
// targeting pack ships none. Only serializer-independent APIs are used here:
// Fragment, Patch, ChangeSet, and the ChangePayload CLR projections
// (ToPayload/FromPatch/ToPatch/ToChangeSet); JSON transport
// (JsonSerializer, Fragment.JsonConverter) is unavailable by design.
[SparseFragmentModel]
public partial class NoJsonSettings
{
    public string? Label { get; set; }

    public int RetryCount { get; set; }
}

[SparseFragmentModel]
public partial class NoJsonFleet
{
    public List<NoJsonServer> Servers { get; set; } = new();
}

public partial class NoJsonServer
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;
}

public static class NoJsonConsumerCheck
{
    public static string Run()
    {
        var before = NoJsonSettings.Fragment.From(
            new NoJsonSettings { Label = "before", RetryCount = 1 }
        );
        var after = NoJsonSettings.Fragment.From(
            new NoJsonSettings { Label = "after", RetryCount = 1 }
        );
        var changes = NoJsonSettings.ChangeSet.Between(
            Optional<NoJsonSettings.Fragment?>.Present(before),
            Optional<NoJsonSettings.Fragment?>.Present(after)
        );

        // CLR-only payload round-trip: no serializer involved.
        var payload = changes.ToPayload();
        Require(payload.Version == "0.1", "payload carries the v1 version token");
        var restored = payload.ToChangeSet();
        var applied = restored.ToPatch().Apply(Optional<NoJsonSettings.Fragment?>.Present(before));
        Require(applied.Value!.Label.Value == "after", "patch replays the transition");

        // Baseline-free command envelope without JSON.
        var command = NoJsonSettings.ChangePayload.FromPatch(changes.ToPatch());
        var projected = command.ToPatch();
        Require(!projected.IsEmpty, "command envelope projects a patch");

        // Keyed CLR-only payload round-trip.
        var fleetBefore = NoJsonFleet.Fragment.From(new NoJsonFleet());
        var fleetAfter = NoJsonFleet.Fragment.From(
            new NoJsonFleet
            {
                Servers = new List<NoJsonServer>
                {
                    new() { Id = "a", Host = "one" },
                },
            }
        );
        var fleetChanges = NoJsonFleet.ChangeSet.Between(
            Optional<NoJsonFleet.Fragment?>.Present(fleetBefore),
            Optional<NoJsonFleet.Fragment?>.Present(fleetAfter)
        );
        var fleetRestored = fleetChanges.ToPayload().ToChangeSet();
        var fleetApplied = fleetRestored
            .ToPatch()
            .Apply(Optional<NoJsonFleet.Fragment?>.Present(fleetBefore));
        Require(
            fleetApplied.Value!.Servers.Value!.Count == 1,
            "keyed payload round-trips without JSON"
        );

        return "SparseFragments serializer-free consumer passed.";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("NoJson consumer check failed: " + message);
    }
}
