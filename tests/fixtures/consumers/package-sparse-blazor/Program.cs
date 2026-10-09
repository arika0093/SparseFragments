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
        new() { Sku = "a", Quantity = 1, Price = 10m },
        new() { Sku = "b", Quantity = 2, Price = 20m },
    },
};

var session = order.CreateEditSession();
Require(!session.HasChanges, "fresh session has no changes");

session.Model.Number = "ORD-2";
session.Model.Lines.Add(new BlazorDocsOrderLine { Sku = "c", Quantity = 3, Price = 30m });
Require(session.HasChanges, "scalar and keyed-collection edits detected");

var patch = session.CreatePatch();
Require(!patch.IsEmpty, "session derives a semantic patch");
session.AcceptChanges();
Require(!session.HasChanges, "AcceptChanges re-baselines");
Require(session.CreatePatch().IsEmpty, "post-accept patch is empty");

// Edit-then-restore yields no semantic change even though fields were touched.
session.Model.Number = "changed";
session.Model.Number = "ORD-2";
Require(!session.HasChanges, "edit-then-restore has no semantic changes");
Require(session.CreatePatch().IsEmpty, "edit-then-restore patch is empty");

// Validation flows through the ordinary EditContext pipeline, bound to the raw model.
var editContext = session.CreateEditContext();
var store = session.CreateValidationStore(editContext);
editContext.OnValidationRequested += (sender, _) =>
{
    store.Clear();
    if (string.IsNullOrEmpty(session.Model.Number))
    {
        store.Add(session.Field(nameof(BlazorDocsOrder.Number)), "Number is required.");
    }
};
Require(editContext.Model is BlazorDocsOrder, "EditContext uses the raw model");
Require(editContext.Validate(), "valid model passes validation");
session.Model.Number = string.Empty;
editContext.NotifyFieldChanged(session.Field(nameof(BlazorDocsOrder.Number)));
Require(!editContext.Validate(), "empty number fails validation");
Require(
    editContext.GetValidationMessages().Contains("Number is required."),
    "validation message surfaces through the store");

// Externally obtained errors (for example structured rebase conflicts) surface
// the same way without taking a dependency on HTTP transport.
session.AddValidationError(
    store,
    session.Field(nameof(BlazorDocsOrder.Number)),
    "Server rejected the order number.");
Require(
    editContext.GetValidationMessages().Contains("Server rejected the order number."),
    "external error surfaces through AddValidationError");

// sample: ui-session
var uiOrder = new UiOrder { Number = "ORD-1" };
var uiSession = uiOrder.CreateEditSession();

uiSession.Model.Number = "ORD-2";
// uiSession.HasChanges == true
DocsCheck.Require(uiSession.HasChanges, "scalar edit detected");

var uiChanges = uiSession.CreateChangeSet();
// uiChanges.IsEmpty == false
DocsCheck.Require(!uiChanges.IsEmpty, "semantic change set derived");

uiSession.AcceptChanges();
// uiSession.HasChanges == false
DocsCheck.Require(!uiSession.HasChanges, "re-baselined");
// /sample

UiDocSamples.Run();

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
    public static void Run()
    {
        SubmitRefreshAndConflict();
        WpfObservableBinding();
    }

    private static void SubmitRefreshAndConflict()
    {
        // sample: ui-blazor-form
        var formOrder = new BlazorDocsOrder { Number = "ORD-1" };
        var formSession = formOrder.CreateEditSession();
        var formContext = formSession.CreateEditContext();
        var formStore = formSession.CreateValidationStore(formContext);
        formContext.OnValidationRequested += (_, _) =>
        {
            formStore.Clear();
            if (string.IsNullOrEmpty(formSession.Model.Number))
            {
                formStore.Add(
                    formSession.Field(nameof(BlazorDocsOrder.Number)),
                    "Number is required."
                );
            }
        };

        formSession.Model.Number = "ORD-2";
        if (!formContext.Validate())
        {
            throw new InvalidOperationException("The form has validation errors.");
        }

        // Send formSession.CreateChangeSet().ToPayload() through the
        // application transport. The server rebases it onto the current row:
        // unchanged here, the server kept a newer Number instead.
        var submitted = formSession.CreateChangeSet();
        var serverState = new BlazorDocsOrder { Number = "SERVER" };
        var surfacedConflicts = 0;
        var surfacedPath = string.Empty;
        if (!submitted.TryApplyTo(serverState, out _, out var formConflicts))
        {
            foreach (var formConflict in formConflicts)
            {
                formSession.AddValidationError(
                    formStore,
                    formConflict.PathText,
                    "Server kept a newer value."
                );
                surfacedConflicts++;
                surfacedPath = formConflict.PathText;
            }
        }

        // surfacedConflicts == 1
        // surfacedPath == "Number"
        // The next save starts from the authoritative persisted state.
        var persisted = new BlazorDocsOrder { Number = "ORD-2" };
        formSession = persisted.CreateEditSession();
        formContext = formSession.CreateEditContext();
        // formSession.HasChanges == false
        // /sample
        DocsCheck.Require(
            surfacedConflicts == 1 && surfacedPath == nameof(BlazorDocsOrder.Number),
            "structured conflict names the member"
        );
        DocsCheck.Require(
            !formSession.HasChanges,
            "fresh session on persisted state is clean"
        );
    }

    private static void WpfObservableBinding()
    {
        // sample: ui-wpf-session
        var stockModel = new UiOrder { Number = "ORD-1" };
        var saveEnabled = false;
        var stockSession = stockModel.CreateEditSession(
            onChanged: () =>
            {
                saveEnabled = true;
            }
        );
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

[SparseFragmentModel]
public partial class BlazorDocsOrder
{
    public string Number { get; set; } = string.Empty;

    public List<BlazorDocsOrderLine> Lines { get; set; } = new();
}
