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

// Validation flows through the ordinary EditContext pipeline.
var store = session.CreateValidationStore();
session.EditContext.OnValidationRequested += (sender, _) =>
{
    store.Clear();
    if (string.IsNullOrEmpty(session.Model.Number))
    {
        store.Add(session.Field(nameof(BlazorDocsOrder.Number)), "Number is required.");
    }
};
Require(session.EditContext.Validate(), "valid model passes validation");
session.Model.Number = string.Empty;
session.EditContext.NotifyFieldChanged(session.Field(nameof(BlazorDocsOrder.Number)));
Require(!session.EditContext.Validate(), "empty number fails validation");
Require(
    session.EditContext.GetValidationMessages().Contains("Number is required."),
    "validation message surfaces through the store");

// Externally obtained errors (for example structured rebase conflicts) surface
// the same way without taking a dependency on HTTP transport.
SparseEditSession<BlazorDocsOrder, BlazorDocsOrder.Fragment, BlazorDocsOrder.Patch>.AddValidationError(
    store,
    session.Field(nameof(BlazorDocsOrder.Number)),
    "Server rejected the order number.");
Require(
    session.EditContext.GetValidationMessages().Contains("Server rejected the order number."),
    "external error surfaces through AddValidationError");

Console.WriteLine("SparseFragments Blazor consumer passed.");

static void Require(bool condition, string capability)
{
    if (!condition)
    {
        throw new InvalidOperationException("Failed: " + capability);
    }
}

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
