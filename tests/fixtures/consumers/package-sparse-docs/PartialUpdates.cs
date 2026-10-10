using System.Text.Json;
using SparseFragments;

// Canonical compile-checked mirror of docs/how-to/partial-updates.md.
// One endpoint with two legitimate inputs: a baseline-free Patch command and
// a baseline-aware ChangeSet from an editor. Regions execute as part of Run()
// so the documented baseline guarantees and the reversible versus
// irreversible distinction are verified, not just compiled.
public static class PartialUpdateSamples
{
    public static void Run()
    {
        PartialCommand();
        PartialTransition();
        PartialPayload();
        PartialAudit();
        PartialRedacted();
    }

    private static void PartialCommand()
    {
        // sample: partial-patch
        var stored = new PartialProfile { DisplayName = "Ann", Phone = "old-phone" };
        var storedState = PartialProfile.Fragment.From(stored);

        // The caller never saw a baseline: a Patch carries no preconditions.
        var command = new PartialProfile.Patch { DisplayName = "Ada" };
        var commanded = command.ApplyTo(stored);
        // commanded.DisplayName == "Ada"
        // commanded.Phone == "old-phone"
        // /sample
        DocsCheck.Require(commanded.DisplayName == "Ada", "command sets DisplayName");
        DocsCheck.Require(commanded.Phone == "old-phone", "command keeps Phone");
        DocsCheck.Require(storedState.DisplayName.Value == "Ann", "stored state untouched");
    }

    private static void PartialTransition()
    {
        // sample: partial-changeset
        var editorBase = new PartialProfile { DisplayName = "Ann", Phone = "old-phone" };
        var editorEdited = new PartialProfile { DisplayName = "Ada", Phone = "old-phone" };

        // The editor saw the baseline, so the transition keeps its before-state.
        var editorChanges = editorBase.CreateChangeSet(editorEdited);
        // editorChanges.DisplayName.IsChanged == true
        // editorChanges.DisplayName.Before.Value == "Ann"
        // editorChanges.DisplayName.After.Value == "Ada"
        // editorChanges.Phone.IsChanged == false
        // /sample
        DocsCheck.Require(editorChanges.DisplayName.IsChanged, "DisplayName transition observed");
        DocsCheck.Require(editorChanges.DisplayName.Before.Value == "Ann", "Before preserved");
        DocsCheck.Require(editorChanges.DisplayName.After.Value == "Ada", "After preserved");
        DocsCheck.Require(!editorChanges.Phone.IsChanged, "untouched member unchanged");
    }

    private static void PartialPayload()
    {
        // sample: partial-payload
        var payloadBase = new PartialProfile { DisplayName = "Ann", Phone = "old-phone" };
        var payloadEdited = new PartialProfile { DisplayName = "Ada", Phone = "old-phone" };
        var transition = payloadBase.CreateChangeSet(payloadEdited);

        // Both inputs travel through the same envelope type.
        var transitionPayload = transition.ToPayload();
        var commandPayload = PartialProfile.ChangePayload.FromPatch(
            new PartialProfile.Patch { DisplayName = "Ada" }
        );

        // ToPatch is the information-loss boundary: the before-state is gone,
        // so the projection can replay but no longer rebase or report conflicts.
        var replay = transition.ToPatch();
        var replayed = replay.ApplyTo(payloadBase);
        // replayed.DisplayName == "Ada"
        // /sample
        DocsCheck.Require(replayed.DisplayName == "Ada", "projected patch replays the after-state");
        DocsCheck.Require(
            JsonSerializer.Serialize(transitionPayload).Contains("Ada"),
            "transition payload carries the after-state"
        );
        DocsCheck.Require(
            JsonSerializer.Serialize(commandPayload).Contains("Ada"),
            "command payload carries the desired state"
        );
    }

    private static void PartialAudit()
    {
        // sample: partial-audit
        var auditBase = new PartialProfile { DisplayName = "Ann", Phone = "old-phone" };
        var auditEdited = new PartialProfile { DisplayName = "Ada", Phone = "old-phone" };
        var auditChanges = auditBase.CreateChangeSet(auditEdited);

        // Undo needs the before-state, so only the complete ChangeSet inverts.
        var undo = auditChanges.Invert();
        var restored = undo.ToPatch().ApplyTo(auditEdited);
        // restored.DisplayName == "Ann"
        // /sample
        DocsCheck.Require(restored.DisplayName == "Ann", "inverted transition walks back");
    }

    private static void PartialRedacted()
    {
        // sample: partial-redacted
        var secretBase = new PartialProfile { DisplayName = "Ann", Phone = "old-phone" };
        var secretEdited = new PartialProfile { DisplayName = "Ann", Phone = "new-phone" };
        var rotation = secretBase.CreateChangeSet(secretEdited);

        var wire = JsonSerializer.Serialize(rotation.ToPayload());
        // wire carries "new-phone" but never "old-phone"
        var incoming = JsonSerializer.Deserialize<PartialProfile.ChangePayload>(wire)!;

        // The redacted payload still projects to a baseline-free command.
        var replayed = incoming.ToPatch().ApplyTo(secretBase);
        // replayed.Phone == "new-phone"

        // It cannot become a baseline-aware ChangeSet again: the before-state
        // is gone, so there is no reversible history to reconstruct.
        var rejected = false;
        try
        {
            incoming.ToChangeSet();
        }
        catch (ArgumentException)
        {
            rejected = true;
        }
        // rejected == true
        // /sample
        DocsCheck.Require(!wire.Contains("old-phone"), "redacted before-state stays undisclosed");
        DocsCheck.Require(wire.Contains("new-phone"), "desired state travels");
        DocsCheck.Require(replayed.Phone == "new-phone", "command projection replays the rotation");
        DocsCheck.Require(rejected, "redacted payload cannot form a ChangeSet");
    }
}

// sample: partial-models
[SparseFragmentModel]
public partial class PartialProfile
{
    public string? DisplayName { get; set; }

    [SparseRedactBefore]
    public string? Phone { get; set; }
}

// /sample
