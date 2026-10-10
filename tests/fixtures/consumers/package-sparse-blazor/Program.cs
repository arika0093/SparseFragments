using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using SparseFragments;
using SparseFragments.Blazor;

// Canonical compile-checked mirror of docs/ui-frameworks.md (#45, #48) and the
// Blazor candidate-consumer gate (#46). Exercises the generated
// CreateEditSession() workflow against the packed SparseFragments.Blazor
// package (no ProjectReference fallback when SparseFragmentsPackageVersion is
// set): baseline-relative HasChanges, semantic CreatePatch, AcceptChanges
// re-baselining, edit-then-restore, and EditContext validation integration.

var order = new BlazorDocsOrder
{
    Number = "ORD-1",
    Lines = new List<BlazorDocsOrderLine>
    {
        new()
        {
            Sku = "a",
            Quantity = 1,
            Price = 10m,
        },
        new()
        {
            Sku = "b",
            Quantity = 2,
            Price = 20m,
        },
    },
};

var session = order.CreateEditSession();
Require(!session.HasChanges, "fresh session has no changes");

session.Observable.Number = "ORD-2";
session.Observable.Lines.AddModel(
    new BlazorDocsOrderLine
    {
        Sku = "c",
        Quantity = 3,
        Price = 30m,
    }
);
Require(session.HasChanges, "scalar and keyed-collection edits detected");

var patch = session.CreatePatch();
Require(!patch.IsEmpty, "session derives a semantic patch");
session.AcceptChanges();
Require(!session.HasChanges, "AcceptChanges re-baselines");
Require(session.CreatePatch().IsEmpty, "post-accept patch is empty");

// Edit-then-restore yields no semantic change even though fields were touched.
session.Observable.Number = "changed";
session.Observable.Number = "ORD-2";
Require(!session.HasChanges, "edit-then-restore has no semantic changes");
Require(session.CreatePatch().IsEmpty, "edit-then-restore patch is empty");

// Validation flows through the ordinary EditContext pipeline, bound to the raw model.
var editContext = session.CreateEditContext();
var store = session.CreateValidationStore(editContext);
editContext.OnValidationRequested += (sender, _) =>
{
    store.Clear();
    if (string.IsNullOrEmpty(session.Current.Number))
    {
        store.Add(session.Field(nameof(BlazorDocsOrder.Number)), "Number is required.");
    }
};
Require(editContext.Model is BlazorDocsOrder, "EditContext uses the raw model");
Require(editContext.Validate(), "valid model passes validation");
session.Observable.Number = string.Empty;
editContext.NotifyFieldChanged(session.Field(nameof(BlazorDocsOrder.Number)));
Require(!editContext.Validate(), "empty number fails validation");
Require(
    editContext.GetValidationMessages().Contains("Number is required."),
    "validation message surfaces through the store"
);

// Externally obtained errors (for example structured rebase conflicts) surface
// the same way without taking a dependency on HTTP transport.
session.AddValidationError(
    store,
    session.Field(nameof(BlazorDocsOrder.Number)),
    "Server rejected the order number."
);
Require(
    editContext.GetValidationMessages().Contains("Server rejected the order number."),
    "external error surfaces through AddValidationError"
);

// sample: ui-session
var uiOrder = new UiOrder { Number = "ORD-1" };
var uiSession = uiOrder.CreateEditSession();

uiSession.Observable.Number = "ORD-2";

// uiSession.HasChanges == true
DocsCheck.Require(uiSession.HasChanges, "scalar edit detected");

var uiChanges = uiSession.CreateChangeSet();

// uiChanges.IsEmpty == false
DocsCheck.Require(!uiChanges.IsEmpty, "semantic change set derived");

uiSession.AcceptChanges();

// uiSession.HasChanges == false
DocsCheck.Require(!uiSession.HasChanges, "re-baselined");

// /sample

await UiDocSamples.RunAsync();

Console.WriteLine("SparseFragments Blazor consumer passed.");

static void Require(bool condition, string capability)
{
    if (!condition)
    {
        throw new InvalidOperationException("Failed: " + capability);
    }
}

internal static class DocsCheck
{
    public static void Require(bool condition, string capability)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Failed: " + capability);
        }
    }
}

public static class UiDocSamples
{
    public static async Task RunAsync()
    {
        WpfObservableBinding();
        BlazorFormInput();
        await BlazorFormSaveAsync();
        BlazorFormConflict();
        await BlazorFormTransportAsync();
    }

    private static void BlazorFormInput()
    {
        // sample: blazor-form-input
        var inputOrder = new BlazorEditFormOrder { Number = "ORD-1", Version = 7 };
        var inputSession = inputOrder.CreateEditSession();
        var inputContext = inputSession.CreateEditContext();
        var inputStore = inputSession.CreateValidationStore(inputContext);
        inputContext.OnValidationRequested += (_, _) =>
        {
            inputStore.Clear();
            if (string.IsNullOrWhiteSpace(inputSession.Current.Number))
            {
                inputStore.Add(
                    inputSession.Field(nameof(BlazorEditFormOrder.Number)),
                    "Number is required."
                );
            }
        };

        // The text input writes through the Observable proxy, then tells the
        // EditContext which field changed. EditContext.Model stays the
        // original model, so never bind an InputText expression to the proxy.
        inputSession.Observable.Number = "ORD-2";
        inputContext.NotifyFieldChanged(inputSession.Field(nameof(BlazorEditFormOrder.Number)));
        // inputContext.IsModified() == true
        // inputContext.Validate() == true
        DocsCheck.Require(inputContext.IsModified(), "observable edit marks the field modified");
        DocsCheck.Require(inputContext.Validate(), "filled number passes validation");

        inputSession.Observable.Number = string.Empty;
        inputContext.NotifyFieldChanged(inputSession.Field(nameof(BlazorEditFormOrder.Number)));
        // inputContext.Validate() == false
        // /sample
        DocsCheck.Require(!inputContext.Validate(), "empty number fails validation");
        DocsCheck.Require(
            inputContext.GetValidationMessages().Contains("Number is required."),
            "required message surfaces through the store"
        );
    }

    private static async Task BlazorFormSaveAsync()
    {
        // sample: blazor-form-save
        var saveOrder = new BlazorEditFormOrder
        {
            Number = "ORD-1",
            Note = "pick",
            Version = 7,
        };
        var saveSession = saveOrder.CreateEditSession();
        var saveContext = saveSession.CreateEditContext();
        var saveStore = saveSession.CreateValidationStore(saveContext);
        void ValidateSave(object? sender, ValidationRequestedEventArgs args)
        {
            saveStore.Clear();
            if (string.IsNullOrWhiteSpace(saveSession.Current.Number))
            {
                saveStore.Add(
                    saveSession.Field(nameof(BlazorEditFormOrder.Number)),
                    "Number is required."
                );
            }
        }
        saveContext.OnValidationRequested += ValidateSave;
        var saveService = new FakeOrderSaveService
        {
            PersistedState = new BlazorEditFormOrder
            {
                Number = "ORD-2",
                Note = "pick",
                Version = 8,
                UpdatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            },
        };

        saveSession.Observable.Number = "ord-2";
        saveContext.NotifyFieldChanged(saveSession.Field(nameof(BlazorEditFormOrder.Number)));

        var isSaving = true;
        try
        {
            if (!saveContext.Validate())
            {
                throw new InvalidOperationException("The form has validation errors.");
            }
            var submitted = saveSession.CreateChangeSet();
            var saveResult = await saveService.SaveAsync(
                submitted.ToPayload(),
                CancellationToken.None
            );
            if (saveResult is BlazorFormConflicted)
            {
                throw new InvalidOperationException("Expected the save to succeed.");
            }
            var persisted = ((BlazorFormSaved)saveResult).Persisted;
            // persisted.Number == "ORD-2"
            // persisted.Version == 8

            // On success only, adopt the authoritative state: detach the old
            // handlers, then start a fresh session and EditContext from it.
            saveContext.OnValidationRequested -= ValidateSave;
            saveSession = persisted.CreateEditSession();
            saveContext = saveSession.CreateEditContext();
            saveStore = saveSession.CreateValidationStore(saveContext);
            saveContext.OnValidationRequested += ValidateSave;
            // saveSession.HasChanges == false
            // ReferenceEquals(saveContext.Model, persisted) == true
        }
        finally
        {
            isSaving = false;
        }
        // isSaving == false
        // /sample
        DocsCheck.Require(!isSaving, "save-in-flight state clears in finally");
        DocsCheck.Require(!saveSession.HasChanges, "fresh session on persisted state is clean");
        DocsCheck.Require(
            ReferenceEquals(saveContext.Model, saveService.PersistedState),
            "fresh EditContext binds the persisted model"
        );
        DocsCheck.Require(saveSession.Current.Number == "ORD-2", "normalised number adopted");
        DocsCheck.Require(saveSession.Current.Version == 8, "server-assigned version adopted");
        DocsCheck.Require(saveService.ReceivedJson.Count == 1, "exactly one change set submitted");
    }

    private static void BlazorFormConflict()
    {
        // sample: blazor-form-conflict
        var conflictOrder = new BlazorEditFormOrder { Number = "ORD-1", Version = 7 };
        var conflictSession = conflictOrder.CreateEditSession();
        var conflictContext = conflictSession.CreateEditContext();
        var conflictStore = conflictSession.CreateValidationStore(conflictContext);
        conflictContext.OnValidationRequested += (_, _) =>
        {
            conflictStore.Clear();
            if (string.IsNullOrWhiteSpace(conflictSession.Current.Number))
            {
                conflictStore.Add(
                    conflictSession.Field(nameof(BlazorEditFormOrder.Number)),
                    "Number is required."
                );
            }
        };
        var conflictService = new FakeOrderSaveService
        {
            Mode = BlazorFormOutcome.Conflict,
            ServerState = new BlazorEditFormOrder { Number = "SERVER", Version = 8 },
        };

        conflictSession.Observable.Number = "ORD-2";
        conflictContext.NotifyFieldChanged(
            conflictSession.Field(nameof(BlazorEditFormOrder.Number))
        );
        var submitted = conflictSession.CreateChangeSet();
        var serverState = conflictService.ServerState;
        if (submitted.TryApplyTo(serverState, out _, out var surfacingConflicts))
        {
            throw new InvalidOperationException("Expected a semantic conflict.");
        }
        foreach (var conflict in surfacingConflicts)
        {
            conflictSession.AddValidationError(
                conflictStore,
                conflict.PathText,
                "Server kept a newer value."
            );
        }
        // surfacingConflicts.Single().PathText == "Number"
        // conflictSession.HasChanges == true
        // conflictSession.Current.Number == "ORD-2"
        DocsCheck.Require(
            surfacingConflicts.Single().PathText == nameof(BlazorEditFormOrder.Number),
            "structured conflict names the member"
        );
        DocsCheck.Require(conflictContext.IsModified(), "field stays marked modified");
        DocsCheck.Require(
            conflictSession.HasChanges && conflictSession.Current.Number == "ORD-2",
            "original session and pending edit kept"
        );

        // The conflict needs an explicit user decision. Reloading the
        // conflicting state reports the same conflict and keeps everything.
        var reload = conflictSession.Reload(serverState);
        // reload.HasConflicts == true
        // conflictSession.Current.Number == "ORD-2"
        DocsCheck.Require(reload.HasConflicts, "conflicted reload reports conflicts");
        DocsCheck.Require(
            conflictSession.Current.Number == "ORD-2" && conflictSession.HasChanges,
            "conflicted reload leaves the live model and baseline untouched"
        );

        // The user takes the server value, then the pending change is gone.
        conflictSession.Observable.Number = "SERVER";
        conflictContext.NotifyFieldChanged(
            conflictSession.Field(nameof(BlazorEditFormOrder.Number))
        );
        var resolved = conflictSession.Reload(serverState);
        // resolved.HasConflicts == false
        // conflictSession.HasChanges == false
        // /sample
        DocsCheck.Require(!resolved.HasConflicts, "taking the server value resolves the conflict");
        DocsCheck.Require(!conflictSession.HasChanges, "resolved session is clean");
    }

    private static async Task BlazorFormTransportAsync()
    {
        // sample: blazor-form-transport
        var retryOrder = new BlazorEditFormOrder { Number = "ORD-1", Version = 7 };
        var retrySession = retryOrder.CreateEditSession();
        var retryContext = retrySession.CreateEditContext();
        var retryStore = retrySession.CreateValidationStore(retryContext);
        retryContext.OnValidationRequested += (_, _) =>
        {
            retryStore.Clear();
            if (string.IsNullOrWhiteSpace(retrySession.Current.Number))
            {
                retryStore.Add(
                    retrySession.Field(nameof(BlazorEditFormOrder.Number)),
                    "Number is required."
                );
            }
        };
        var retryService = new FakeOrderSaveService
        {
            Mode = BlazorFormOutcome.TransportError,
            PersistedState = new BlazorEditFormOrder { Number = "ORD-2", Version = 8 },
        };

        retrySession.Observable.Number = "ORD-2";
        retryContext.NotifyFieldChanged(retrySession.Field(nameof(BlazorEditFormOrder.Number)));

        var isSaving = true;
        var transportFailed = false;
        try
        {
            var submitted = retrySession.CreateChangeSet();
            try
            {
                await retryService.SaveAsync(submitted.ToPayload(), CancellationToken.None);
            }
            catch (HttpRequestException)
            {
                transportFailed = true;
            }
            // transportFailed == true
            // retrySession.HasChanges == true
            // retrySession.Current.Number == "ORD-2"
        }
        finally
        {
            isSaving = false;
        }
        // isSaving == false

        // Retry with the same session: the baseline never moved, so the
        // pending change is still intact.
        retryService.Mode = BlazorFormOutcome.Saved;
        var retrySubmitted = retrySession.CreateChangeSet();
        var retryResult = await retryService.SaveAsync(
            retrySubmitted.ToPayload(),
            CancellationToken.None
        );
        var persisted = ((BlazorFormSaved)retryResult).Persisted;
        retrySession = persisted.CreateEditSession();
        // retrySession.HasChanges == false
        // /sample
        DocsCheck.Require(transportFailed, "transport failure observed");
        DocsCheck.Require(!isSaving, "editing unblocked in finally");
        DocsCheck.Require(
            retrySubmitted.Number.Before.Value == "ORD-1"
                && retrySubmitted.Number.After.Value == "ORD-2",
            "retry carries the original pending change"
        );
        DocsCheck.Require(!retrySession.HasChanges, "retry from persisted state is clean");
    }

    private static void WpfObservableBinding()
    {
        // sample: ui-wpf-session
        var stockModel = new UiOrder { Number = "ORD-1" };
        var saveEnabled = false;
        var stockSession = stockModel.CreateEditSession(onChanged: () =>
        {
            saveEnabled = true;
        });
        var stockView = stockSession.Observable;

        stockView.Number = "ORD-2";
        stockView.Items.AddModel(new UiOrderItem { Id = "line-1", Name = "First item" });
        // saveEnabled == true
        // stockModel.Number == "ORD-2"
        // stockSession.Current.Number == "ORD-2"

        var stockChanges = stockSession.CreateChangeSet();
        // stockChanges.Number.After.Value == "ORD-2"
        // /sample
        DocsCheck.Require(saveEnabled, "observable edits raise the change callback");
        DocsCheck.Require(
            stockModel.Number == "ORD-2" && stockSession.Current.Number == "ORD-2",
            "observable writes through to the live model behind the read-only view"
        );
        DocsCheck.Require(
            stockChanges.Number.After.Value == "ORD-2",
            "session derives the semantic transition"
        );
    }
}

// sample: ui-session-models
[SparseFragmentModel]
public partial class UiOrder
{
    public string Number { get; set; } = string.Empty;

    public List<UiOrderItem> Items { get; set; } = new();
}

[SparseFragmentModel]
public partial class UiOrderItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

// /sample

[SparseFragmentModel]
public partial class BlazorDocsOrderLine
{
    [SparseKey]
    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal Price { get; set; }
}

// sample: blazor-form-models
[SparseFragmentModel]
public partial class BlazorEditFormOrder
{
    [Required]
    public string Number { get; set; } = string.Empty;

    public string? Note { get; set; }

    public int Version { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public enum BlazorFormOutcome
{
    ValidationFailed,
    Saved,
    Conflict,
    TransportError,
}

public abstract record BlazorFormSaveResult;

public sealed record BlazorFormSaved : BlazorFormSaveResult
{
    public BlazorEditFormOrder Persisted { get; init; } = new();
}

public sealed record BlazorFormConflicted : BlazorFormSaveResult
{
    public BlazorEditFormOrder ServerState { get; init; } = new();
}

// The application owns transport. This fake stands in for the real endpoint:
// success returns the persisted row, conflict returns the current server row,
// and transport failure throws before anything is persisted.
public sealed class FakeOrderSaveService
{
    public BlazorFormOutcome Mode { get; set; } = BlazorFormOutcome.Saved;

    public BlazorEditFormOrder ServerState { get; set; } = new() { Number = "SERVER" };

    public BlazorEditFormOrder PersistedState { get; set; } = new() { Number = "ORD-2" };

    public List<string> ReceivedJson { get; } = new();

    public async Task<BlazorFormSaveResult> SaveAsync(
        BlazorEditFormOrder.ChangePayload payload,
        CancellationToken cancellationToken
    )
    {
        ReceivedJson.Add(JsonSerializer.Serialize(payload));
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        if (Mode == BlazorFormOutcome.TransportError)
        {
            throw new HttpRequestException("The server did not respond.");
        }
        if (Mode == BlazorFormOutcome.Conflict)
        {
            return new BlazorFormConflicted { ServerState = ServerState };
        }
        return new BlazorFormSaved { Persisted = PersistedState };
    }
}

// /sample

[SparseFragmentModel]
public partial class BlazorDocsOrder
{
    public string Number { get; set; } = string.Empty;

    public List<BlazorDocsOrderLine> Lines { get; set; } = new();
}
